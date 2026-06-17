"""
Hdos Superset — Flask app config.

Loaded bởi Superset qua env var SUPERSET_CONFIG_PATH=/app/pythonpath/superset_config.py
(set trong Dockerfile).

Phase 1: standalone behind nginx subpath /superset/. ✅
Phase 2: SSO với AuthService qua custom Security Manager.    ✅
Phase 4: Embedded dashboard cho FE qua guest token.          ✅
"""

import logging
import os

from security_manager import HdosSecurityManager

# ── Security ──────────────────────────────────────────────────────────────
# PRODUCTION: bắt buộc set SUPERSET_SECRET_KEY (>=32 ký tự ngẫu nhiên).
# Default ở đây chỉ phục vụ local dev — sẽ refuse run nếu key < 32 ký tự ở prod.
SECRET_KEY = os.environ.get(
    "SUPERSET_SECRET_KEY",
    "CHANGE-ME-LOCAL-DEV-INSECURE-32-CHARS-X",
)

# ── Metadata database (Postgres riêng cho Superset) ───────────────────────
SQLALCHEMY_DATABASE_URI = os.environ.get(
    "SUPERSET_DATABASE_URI",
    "postgresql+psycopg2://superset_user:superset_pass"
    "@postgres-superset:5432/SupersetDb",
)
SQLALCHEMY_TRACK_MODIFICATIONS = False

# ── Behind nginx reverse proxy (port 8444 dedicated, root path) ──────────
# Superset chạy như root path trên port 8444 riêng (không subpath).
# nginx terminate TLS, forward / → http://superset:8088/. ProxyFix đọc
# X-Forwarded-* để Superset biết URL gốc client thấy là https:// + port 8444.
ENABLE_PROXY_FIX = True
PROXY_FIX_CONFIG = {
    "x_for": 1,
    "x_proto": 1,
    "x_host": 1,
    "x_port": 1,
}

# Session cookie ở root path mặc định (không subpath).
SESSION_COOKIE_SAMESITE = "Lax"

# Public URL — dùng cho email/alert/screenshot links (Phase 5+).
WEBDRIVER_BASEURL = os.environ.get(
    "SUPERSET_PUBLIC_URL",
    "https://localhost:8443/superset/",
)
WEBDRIVER_BASEURL_USER_FRIENDLY = WEBDRIVER_BASEURL

# ── Caching ───────────────────────────────────────────────────────────────
# Phase 1: in-memory SimpleCache (per-worker, không share).
# Phase 4 sẽ chuyển sang Redis khi cần guest token cache + async query.
CACHE_CONFIG = {
    "CACHE_TYPE": "SimpleCache",
    "CACHE_DEFAULT_TIMEOUT": 300,
}
DATA_CACHE_CONFIG = CACHE_CONFIG
FILTER_STATE_CACHE_CONFIG = CACHE_CONFIG
EXPLORE_FORM_DATA_CACHE_CONFIG = CACHE_CONFIG

# ── Feature flags ─────────────────────────────────────────────────────────
FEATURE_FLAGS = {
    "DASHBOARD_NATIVE_FILTERS": True,
    "DASHBOARD_CROSS_FILTERS": True,
    "ALERT_REPORTS": False,
    # Phase 4: bật để cho phép guest token cho dashboard embedded
    "EMBEDDED_SUPERSET": True,
}

# ── Phase 2: SSO với AuthService qua custom Security Manager ──────────────
# JWT secret PHẢI bằng Jwt__Secret của AuthService (HS256, >=32 ký tự).
# Set qua env HDOS_JWT_SECRET = ${JWT_SECRET} trong docker-compose.
CUSTOM_SECURITY_MANAGER = HdosSecurityManager
HDOS_JWT_SECRET = os.environ.get("HDOS_JWT_SECRET", "")

# ── Phase 4: Embedded dashboard cho FE ────────────────────────────────────
# Guest token TTL: 5 phút (đủ cho FE render dashboard; refresh trước khi hết).
GUEST_TOKEN_JWT_EXP_SECONDS = 300

# CORS allow FE domain embed iframe Superset
# Thêm domain prod vào HDOS_FE_ORIGINS env (comma-separated) khi deploy.
_fe_origins = os.environ.get("HDOS_FE_ORIGINS", "https://localhost:8443,http://localhost:4000")
ENABLE_CORS = True
CORS_OPTIONS = {
    "supports_credentials": True,
    "allow_headers": ["*"],
    "resources": ["*"],
    "origins": [o.strip() for o in _fe_origins.split(",") if o.strip()],
}

# Cho phép iframe nhúng từ FE — Phase 4 cần (mặc định Superset chặn iframe).
HTTP_HEADERS = {"X-Frame-Options": "ALLOWALL"}
TALISMAN_ENABLED = False

# ── Logging ───────────────────────────────────────────────────────────────
logging.basicConfig(
    format="%(asctime)s %(levelname)s [%(name)s] %(message)s",
    level=logging.INFO,
)
