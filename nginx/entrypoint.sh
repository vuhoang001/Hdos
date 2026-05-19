#!/bin/sh
# Auto-generates a self-signed TLS cert if none exists, then starts nginx.
# Cert is stored in a named Docker volume so it survives container restarts.
set -e

SSL_DIR=/etc/nginx/ssl
KEY="$SSL_DIR/hdos.key"
CERT="$SSL_DIR/hdos.crt"

if [ ! -f "$KEY" ] || [ ! -f "$CERT" ]; then
    # nginx:alpine ships only the SSL shared library, not the openssl CLI.
    command -v openssl >/dev/null 2>&1 || apk add --no-cache openssl >/dev/null 2>&1

    echo "[nginx] Generating self-signed TLS certificate..."
    mkdir -p "$SSL_DIR"
    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
        -keyout "$KEY" \
        -out    "$CERT" \
        -subj   "/CN=hdos-dev/O=Hdos Dev/C=VN" \
        -addext "subjectAltName=DNS:localhost,DNS:hdos-nginx,IP:127.0.0.1,IP:192.168.100.60" \
        2>/dev/null
    echo "[nginx] Certificate ready."
else
    echo "[nginx] Certificate already exists, skipping."
fi

exec nginx -g "daemon off;"
