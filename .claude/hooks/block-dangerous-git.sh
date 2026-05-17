#!/usr/bin/env bash
set -euo pipefail

INPUT="$(cat)"

# Prefer jq; fall back to a crude grep if jq is unavailable.
if command -v jq >/dev/null 2>&1; then
  COMMAND="$(printf '%s' "$INPUT" | jq -r '.tool_input.command // empty')"
else
  COMMAND="$(printf '%s' "$INPUT" | sed -n 's/.*"command":"\([^"]*\)".*/\1/p')"
fi

[ -z "$COMMAND" ] && exit 0

DANGEROUS_PATTERNS=(
  "git push"
  "git reset --hard"
  "git clean -fd"
  "git clean -f"
  "git branch -D"
  "git checkout \."
  "git restore \."
  "push --force"
  "reset --hard"
)

for pattern in "${DANGEROUS_PATTERNS[@]}"; do
  if printf '%s' "$COMMAND" | grep -Eq "$pattern"; then
    echo "BLOCKED: '$COMMAND' matches dangerous pattern '$pattern'. The user has prevented you from doing this." >&2
    exit 2
  fi
done

exit 0
