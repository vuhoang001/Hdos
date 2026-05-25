#!/usr/bin/env bash
set -euo pipefail

CONNECT_URL="${KAFKA_CONNECT_URL:-http://localhost:8083}"
CONNECTOR_FILE="${1:-$(dirname "$0")/sqlserver-connector.json}"

echo "Waiting for Kafka Connect at $CONNECT_URL..."
until curl -sf "$CONNECT_URL/connectors" > /dev/null; do
  sleep 3
done

CONNECTOR_NAME=$(jq -r '.name' "$CONNECTOR_FILE")

if curl -sf "$CONNECT_URL/connectors/$CONNECTOR_NAME" > /dev/null 2>&1; then
  echo "Connector '$CONNECTOR_NAME' already exists — updating config..."
  curl -s -X PUT "$CONNECT_URL/connectors/$CONNECTOR_NAME/config" \
    -H "Content-Type: application/json" \
    -d "$(jq '.config' "$CONNECTOR_FILE")"
else
  echo "Registering connector '$CONNECTOR_NAME'..."
  curl -s -X POST "$CONNECT_URL/connectors" \
    -H "Content-Type: application/json" \
    -d @"$CONNECTOR_FILE"
fi

echo ""
echo "Done. Current connectors:"
curl -s "$CONNECT_URL/connectors" | jq .
