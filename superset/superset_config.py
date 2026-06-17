"""
Hdos Superset — Flask app config.

Loaded bởi Superset qua env var SUPERSET_CONFIG_PATH=/app/pythonpath/superset_config.py
(set trong Dockerfile).

Phase 1: standalone behind nginx subpath /superset/.
Phase 2 sẽ override AUTH_TYPE để SSO với AuthService (custom security manager).
Phase 4 sẽ enable EMBEDDED_SUPERSET cho FE nhúng dashboard.
"""

import logging
import os

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

# ── Behind nginx reverse proxy at /superset/ ──────────────────────────────
# Nginx phải set header `X-Forwarded-Prefix: /superset` để Werkzeug ProxyFix
# gán SCRIPT_NAME = /superset → Flask url_for() sinh URL có prefix đúng.
ENABLE_PROXY_FIX = True
PROXY_FIX_CONFIG = {
    "x_for": 1,
    "x_proto": 1,
    "x_host": 1,
    "x_port": 1,
    "x_prefix": 1,
}

# Session cookie chỉ scope trong /superset (không leak sang frontend Next.js).
SESSION_COOKIE_PATH = "/superset"
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
    "EMBEDDED_SUPERSET": False,
}

# ── Logging ───────────────────────────────────────────────────────────────
logging.basicConfig(
    format="%(asctime)s %(levelname)s [%(name)s] %(message)s",
    level=logging.INFO,
)
