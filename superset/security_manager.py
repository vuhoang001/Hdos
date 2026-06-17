"""
Hdos Superset — custom Security Manager (Phase 2 SSO).

Tự động login Superset khi browser gửi request có chứa Hdos JWT, theo thứ tự ưu tiên:

  1. Cookie  `hdos_jwt` — set bởi AuthService /auth/superset/sso (browser navigate)
  2. Header  `Authorization: Bearer <jwt>` — dùng cho gọi API trực tiếp

JWT là HS256 ký bằng `Jwt__Secret` chia sẻ giữa AuthService và Superset
(env var `HDOS_JWT_SECRET` ở phía Superset, `Jwt__Secret` ở AuthService — cùng giá trị).

Mapping claim → Superset role:
  - `permission` chứa `superset:admin` → Admin (full access)
  - `permission` chứa `superset:editor` → Alpha (create dashboard/chart)
  - mặc định → Gamma (read-only)

User được auto-create lần đầu, role được update mỗi lần login nếu JWT đổi permission.

Trade-off: HS256 share secret = Superset bị compromise có thể forge JWT cho mọi
service. Tech debt: migrate sang RS256 + JWKS (chỉ public key share với Superset).
Xem docs/65-superset-phase2-sso.md section "Bảo mật".
"""

from __future__ import annotations

import logging
from typing import Any

import jwt as pyjwt
from flask import current_app, request
from flask_login import current_user, login_user
from superset.security import SupersetSecurityManager

log = logging.getLogger(__name__)

HDOS_COOKIE_NAME = "hdos_jwt"
HDOS_JWT_ISSUER = "hdos-auth"
HDOS_JWT_AUDIENCE = "hdos-api"

# Permission → role mapping
PERM_ADMIN = "superset:admin"
PERM_EDITOR = "superset:editor"
ROLE_ADMIN = "Admin"
ROLE_EDITOR = "Alpha"
ROLE_VIEWER = "Gamma"


class HdosSecurityManager(SupersetSecurityManager):
    """Auto-login Superset user khi có Hdos JWT hợp lệ trong cookie/header."""

    def __init__(self, appbuilder):
        super().__init__(appbuilder)
        # Đăng ký hook NGAY khi SM được instantiate — Flask app đã exist tại đây
        appbuilder.app.before_request(self._sso_before_request)
        log.info("HdosSecurityManager: SSO before_request hook registered")

    # ── Public hook ──────────────────────────────────────────────────────
    # Skip paths: static, healthcheck, FAB auth pages (để không can thiệp form login),
    # API endpoints (FAB API tự xử lý auth bằng JWT của nó, không phải Hdos JWT).
    _SKIP_PREFIXES = (
        "/static/",
        "/favicon",
        "/health",
        "/ping",
        "/login/",       # FAB login form — KHÔNG can thiệp
        "/logout/",
        "/users/",       # FAB user mgmt
        "/api/v1/security/",  # FAB security API
        "/resetpassword/",
        "/resetmypassword/",
    )

    def _sso_before_request(self) -> None:
        """Flask before_request: validate Hdos JWT + auto-login nếu present.

        Defensive: bọc tất cả trong try/except, KHÔNG BAO GIỜ raise — fail silently
        để không break Superset native login form. Hook chỉ run trên GET (browser
        navigate); POST (form submit) bỏ qua hoàn toàn để tránh race với FAB auth.
        """
        try:
            # 1. Chỉ act trên GET request (browser navigate). Bỏ qua POST/PUT/DELETE
            #    để không interfere với form submits (login, save dashboard, etc).
            if request.method != "GET":
                return None

            # 2. Skip static + auth-related paths
            path = request.path or ""
            if path.startswith(self._SKIP_PREFIXES):
                return None

            # 3. Đã login session-based → skip
            if current_user.is_authenticated:
                return None

            # 4. Lấy Hdos JWT (header / query / cookie)
            token = self._extract_token()
            if not token:
                return None

            # 5. Validate JWT
            claims = self._validate_jwt(token)
            if not claims:
                return None

            # 6. Map → Superset user và login
            user = self._find_or_create_user(claims)
            if not user:
                return None

            login_user(user, remember=False)
            log.info(
                "Hdos SSO login OK: user=%s roles=%s path=%s",
                user.username,
                [r.name for r in user.roles],
                path,
            )
        except Exception as e:
            # Catch-all: bất kỳ lỗi gì cũng KHÔNG được làm sập request.
            # Native Superset login form sẽ tự xử lý sau hook.
            log.warning("HdosSecurityManager.before_request error (ignored): %s", e)
        return None

    # ── Helpers ──────────────────────────────────────────────────────────
    @staticmethod
    def _extract_token() -> str | None:
        # Ưu tiên: Authorization header (API call)
        auth = request.headers.get("Authorization", "")
        if auth.lower().startswith("bearer "):
            return auth[7:].strip() or None

        # SSO redirect: ?access_token=<jwt> (AuthService /auth/superset/sso trả về)
        # JWT trong URL chỉ thấy 1 lần lúc redirect; sau khi login Superset set
        # session cookie riêng, không cần access_token trong các request sau.
        q = request.args.get("access_token")
        if q:
            return q.strip() or None

        # Fallback: cookie (legacy, giữ cho compatibility nếu setup cookie phù hợp)
        cookie = request.cookies.get(HDOS_COOKIE_NAME)
        return cookie.strip() if cookie else None

    @staticmethod
    def _validate_jwt(token: str) -> dict[str, Any] | None:
        secret = current_app.config.get("HDOS_JWT_SECRET")
        if not secret:
            log.warning("HDOS_JWT_SECRET not configured → SSO disabled")
            return None
        try:
            return pyjwt.decode(
                token,
                key=secret,
                algorithms=["HS256"],
                issuer=HDOS_JWT_ISSUER,
                audience=HDOS_JWT_AUDIENCE,
                options={"require": ["sub", "exp"]},
            )
        except pyjwt.InvalidTokenError as e:
            log.info("Invalid Hdos JWT: %s", e)
            return None

    def _resolve_role(self, claims: dict[str, Any]) -> str:
        perms = claims.get("permission", [])
        if isinstance(perms, str):
            perms = [perms]
        if PERM_ADMIN in perms:
            return ROLE_ADMIN
        if PERM_EDITOR in perms:
            return ROLE_EDITOR
        return ROLE_VIEWER

    def _find_or_create_user(self, claims: dict[str, Any]):
        username = (
            claims.get("preferred_username")
            or claims.get("email")
            or claims.get("sub")
        )
        if not username:
            log.warning("JWT thiếu username claim (preferred_username/email/sub)")
            return None

        email = claims.get("email") or f"{username}@hdos.local"
        full_name = (claims.get("name") or username).strip()
        parts = full_name.split(" ", 1)
        first_name = parts[0]
        last_name = parts[1] if len(parts) > 1 else "(Hdos)"

        target_role_name = self._resolve_role(claims)
        target_role = self.find_role(target_role_name)
        if target_role is None:
            log.error("Superset role '%s' không tồn tại — chạy `superset init`?", target_role_name)
            return None

        user = self.find_user(username=username)
        if user is None:
            user = self.add_user(
                username=username,
                first_name=first_name,
                last_name=last_name,
                email=email,
                role=target_role,
            )
            if user is None:
                log.error("add_user thất bại cho username=%s", username)
            return user

        # Update role nếu JWT đổi (idempotent)
        if target_role not in user.roles:
            user.roles = [target_role]
            self.update_user(user)
            log.info("Updated role for user=%s → %s", username, target_role_name)
        return user
