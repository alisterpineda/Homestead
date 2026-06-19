#!/usr/bin/env bash
# Stop gate — blocks a premature "done" while the FAST signal is red, but only when code changed.
#
# Runs ONLY at Stop (there is intentionally no per-edit PostToolUse hook). Fast signals only:
#   backend changed  -> dotnet build Homestead.WebApi + unit tests (EF InMemory, deterministic)
#   frontend changed -> ng build (template + type errors) + ng test (Vitest unit tests, single-run)
# Slow signals (integration tests, E2E, Aspire cold start) are NEVER run here.
#
# Contract: exit 0 = allow stop; exit 2 = block stop and feed stderr back to the model.
# Loop guard: if we're already continuing from a prior block (stop_hook_active), we don't block again.

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT" || exit 0

INPUT="$(cat)"

# stop_hook_active = true means this stop is itself the result of a previous block. Don't loop.
ACTIVE="$(printf '%s' "$INPUT" | python3 -c 'import sys, json
try:
    print(str(json.load(sys.stdin).get("stop_hook_active", False)).lower())
except Exception:
    print("false")' 2>/dev/null || echo false)"

command -v git >/dev/null 2>&1 || exit 0

# Changed code in the working tree (staged + unstaged + untracked). cut -c4- strips the "XY " status prefix.
CHANGED="$(git status --porcelain 2>/dev/null | cut -c4- || true)"
BACKEND="$(printf '%s\n' "$CHANGED" | grep -E '\.cs$' || true)"
FRONTEND="$(printf '%s\n' "$CHANGED" | grep -E '^Homestead\.WebApp/.*\.ts$' || true)"

# Nothing to gate — docs/config-only edits or a pure Q&A turn. Allow stop immediately.
[ -z "$BACKEND" ] && [ -z "$FRONTEND" ] && exit 0

FAIL=""
LOG="$(mktemp)"

if [ -n "$BACKEND" ] && command -v dotnet >/dev/null 2>&1; then
  echo "## Backend gate: dotnet build + unit tests" >>"$LOG"
  if ! dotnet build Homestead.WebApi -clp:ErrorsOnly >>"$LOG" 2>&1; then
    FAIL="backend build"
  elif ! dotnet test --project Homestead.WebApi.Tests.Unit >>"$LOG" 2>&1; then
    FAIL="backend unit tests"
  fi
fi

if [ -z "$FAIL" ] && [ -n "$FRONTEND" ] && command -v npm >/dev/null 2>&1; then
  echo "## Frontend gate: ng build + unit tests" >>"$LOG"
  # --watch=false is the default outside a TTY (as here), but we pass it explicitly so the
  # gate can never hang in watch mode. No --coverage: the gate wants a fast pass/fail, not a report.
  if ! ( cd Homestead.WebApp && npm run build >>"$LOG" 2>&1 ); then
    FAIL="frontend build (npm run build)"
  elif ! ( cd Homestead.WebApp && npm test -- --watch=false >>"$LOG" 2>&1 ); then
    FAIL="frontend unit tests (npm test)"
  fi
fi

if [ -z "$FAIL" ]; then
  rm -f "$LOG"
  exit 0   # green — allow stop
fi

{
  echo "Fast gate failed: ${FAIL}. Fix it before finishing — this gate runs build + unit tests on changed code."
  echo "--- last 60 lines of output ---"
  tail -n 60 "$LOG"
} >&2
rm -f "$LOG"

# One-shot enforcement: block once with the failure; if we're already in a continuation, let it stop
# (the developer has been told) rather than loop. Claude Code also caps consecutive blocks as a backstop.
if [ "$ACTIVE" = "true" ]; then
  echo "(gate still red, but allowing stop to avoid a hook loop — please address the failure above)" >&2
  exit 0
fi
exit 2
