# ── Hdos Superset image ───────────────────────────────────────────────────
# Base: Apache Superset 3.1.1 (stable, support EMBEDDED_SUPERSET ở Phase 4)
FROM apache/superset:3.1.1

USER root

# ── Optional Python deps (Phase 1: chưa có) ───────────────────────────────
COPY requirements-local.txt /app/requirements-local.txt
RUN pip install --no-cache-dir -r /app/requirements-local.txt

# ── App config + custom Security Manager (Phase 2 SSO) ────────────────────
COPY superset_config.py /app/pythonpath/superset_config.py
COPY security_manager.py /app/pythonpath/security_manager.py
ENV SUPERSET_CONFIG_PATH=/app/pythonpath/superset_config.py
ENV PYTHONPATH=/app/pythonpath:$PYTHONPATH

# ── Init script (chỉ chạy trong service superset-init) ────────────────────
COPY docker-init.sh /app/docker-init.sh
RUN chmod +x /app/docker-init.sh

USER superset
