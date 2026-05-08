#!/usr/bin/env bash
# Chạy một lần để chuẩn bị Ubuntu server.
# Yêu cầu: Ubuntu 22.04+, chạy với sudo

set -euo pipefail

echo "=== Cài Docker ==="
apt-get update -y
apt-get install -y ca-certificates curl gnupg

install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
  | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
chmod a+r /etc/apt/keyrings/docker.gpg

echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
  | tee /etc/apt/sources.list.d/docker.list

apt-get update -y
apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin

systemctl enable --now docker

echo "=== Tạo thư mục deploy ==="
mkdir -p /opt/hdos
# Copy docker-compose files lên đây thủ công hoặc qua scp

echo "=== Tạo file .env cho production ==="
cat > /opt/hdos/.env << 'EOF'
# Thay đổi các giá trị này trước khi deploy
JWT_SECRET=replace-me-with-a-long-random-secret-min-32-chars
GHCR_OWNER=hoanggggf
EOF

echo ""
echo "=== Xong! Các bước tiếp theo ==="
echo "1. Sửa /opt/hdos/.env với giá trị thật"
echo "2. Copy docker-compose.yml và docker-compose.prod.yml lên /opt/hdos/"
echo "3. Thêm GitHub Secrets:"
echo "   - SERVER_HOST: IP hoặc domain của server"
echo "   - SERVER_USER: user SSH (thường là ubuntu)"
echo "   - SSH_PRIVATE_KEY: nội dung private key"
echo "   - GHCR_USER: GitHub username"
echo "   - GHCR_TOKEN: GitHub PAT với quyền read:packages"
