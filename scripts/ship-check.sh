#!/usr/bin/env bash
# ship-check.sh — Vibe & Verify Bước 6: Ship Readiness Check
# Chạy: bash scripts/ship-check.sh

set -euo pipefail

DOTNET="${HOME}/.dotnet/dotnet"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PASS=0
FAIL=0

green()  { printf '\033[32m✓\033[0m %s\n' "$1"; }
red()    { printf '\033[31m✗\033[0m %s\n' "$1"; }
yellow() { printf '\033[33m⚠\033[0m %s\n' "$1"; }
header() { printf '\n\033[1m%s\033[0m\n' "$1"; }

check() {
    local label="$1"; shift
    if "$@" > /tmp/ship-check-out.txt 2>&1; then
        green "$label"
        PASS=$((PASS + 1))
    else
        red "$label"
        printf '    %s\n' "$(tail -5 /tmp/ship-check-out.txt | sed 's/^/    /')"
        FAIL=$((FAIL + 1))
    fi
}

# ── Build ────────────────────────────────────────────────────────────────────
header "1. Build"
if [ -f "$DOTNET" ]; then
    check "dotnet build (no errors)" "$DOTNET" build "$ROOT" --no-incremental -clp:NoSummary -v q
else
    check "dotnet build (no errors)" dotnet build "$ROOT" --no-incremental -clp:NoSummary -v q
fi

# ── Tests ────────────────────────────────────────────────────────────────────
header "2. Tests"
if [ -d "$ROOT/tests" ] && ls "$ROOT/tests"/*.Tests/*.csproj 2>/dev/null | head -1 | grep -q .; then
    if [ -f "$DOTNET" ]; then
        check "dotnet test (all pass)" "$DOTNET" test "$ROOT" --no-build -v q
    else
        check "dotnet test (all pass)" dotnet test "$ROOT" --no-build -v q
    fi
else
    yellow "tests/ không có project — bỏ qua"
fi

# ── Scope ────────────────────────────────────────────────────────────────────
header "3. Scope Check"
CHANGED=$(git -C "$ROOT" diff --name-only HEAD 2>/dev/null || true)
STAGED=$(git -C "$ROOT" diff --name-only --cached 2>/dev/null || true)
ALL_CHANGED=$(printf '%s\n%s' "$CHANGED" "$STAGED" | sort -u | grep -v '^$' || true)

if [ -n "$ALL_CHANGED" ]; then
    yellow "Files thay đổi — verify thủ công đây là đúng scope:"
    echo "$ALL_CHANGED" | sed 's/^/    /'
else
    green "Không có file thay đổi nào"
fi

# ── Secrets ──────────────────────────────────────────────────────────────────
header "4. Secret Scan"
SECRET_PATTERNS='(password|secret|connectionstring|apikey)\s*=\s*"[^"]{4,}'
FOUND=$(git -C "$ROOT" diff --cached -U0 2>/dev/null | grep -iE "^\+" | grep -iE "$SECRET_PATTERNS" || true)
if [ -n "$FOUND" ]; then
    red "Có thể có secret trong staged code:"
    echo "$FOUND" | head -5 | sed 's/^/    /'
    FAIL=$((FAIL + 1))
else
    green "Không phát hiện secret trong staged changes"
fi

# ── Docs ─────────────────────────────────────────────────────────────────────
header "5. Docs Check"
FEATURE_STAGED=$(echo "$STAGED" | grep -E "src/Services/.+/Features/.+\.cs$" || true)
DOCS_STAGED=$(echo "$STAGED" | grep -E "^docs/[0-9]+-.*\.md$" || true)

if [ -n "$FEATURE_STAGED" ] && [ -z "$DOCS_STAGED" ]; then
    yellow "Feature code staged nhưng chưa thấy docs/NN-*.md — đã tạo chưa?"
    NEXT_NUM=$(ls "$ROOT/docs/"[0-9][0-9]-*.md 2>/dev/null | sed 's/.*\/\([0-9]*\)-.*/\1/' | sort -n | tail -1 | awk '{printf "%02d", $1+1}')
    printf '    Số doc tiếp theo: docs/%s-<ten-feature>.md\n' "$NEXT_NUM"
elif [ -n "$DOCS_STAGED" ]; then
    green "Docs đã có: $DOCS_STAGED"
else
    green "Không có feature code staged — docs không bắt buộc"
fi

# ── Summary ──────────────────────────────────────────────────────────────────
printf '\n'
printf '─%.0s' {1..50}
printf '\n'
printf 'PASS: \033[32m%d\033[0m  |  FAIL: ' "$PASS"
if [ "$FAIL" -eq 0 ]; then
    printf '\033[32m0\033[0m\n'
    printf '\n\033[32m✓ Ship readiness check passed. Sẵn sàng commit!\033[0m\n\n'
else
    printf '\033[31m%d\033[0m\n' "$FAIL"
    printf '\n\033[31m✗ %d vấn đề cần fix trước khi ship.\033[0m\n\n' "$FAIL"
    exit 1
fi
