#!/bin/sh
# Provision pinned Tesseract `tessdata_best` models into a destination directory.
#
# Source of truth (both committed, resolved relative to this script):
#   Homestead.WebApi/tessdata.manifest — one language code per line (e.g. "eng")
#   Homestead.WebApi/tessdata.ref      — a pinned tessdata_best commit SHA
#
# For each manifest language it downloads "{lang}.traineddata" from that EXACT ref, so
# dev / test / prod all converge on byte-identical models that never float with the OS.
#
# Usage: sh scripts/fetch-tessdata.sh [DEST_DIR]   (DEST_DIR default: /usr/share/tessdata)
#
# Idempotent (skips files already present), atomic (temp file -> mv, no corrupt partials),
# and portable (curl or wget, POSIX sh under Alpine busybox and Ubuntu bash). The .NET build
# (`dotnet build` / `dotnet test`) never runs this — it is invoked only from each
# environment's provisioning hook (Dockerfile RUN, devcontainer post-create, CI step).
set -eu

# Resolve paths relative to this script so it works from any CWD (repo root, Docker /app, ...).
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
MANIFEST="$REPO_ROOT/Homestead.WebApi/tessdata.manifest"
REF_FILE="$REPO_ROOT/Homestead.WebApi/tessdata.ref"

DEST="${1:-/usr/share/tessdata}"

[ -f "$MANIFEST" ] || { echo "fetch-tessdata: manifest not found: $MANIFEST" >&2; exit 1; }
[ -f "$REF_FILE" ] || { echo "fetch-tessdata: ref not found: $REF_FILE" >&2; exit 1; }

REF=$(tr -d '[:space:]' < "$REF_FILE")
[ -n "$REF" ] || { echo "fetch-tessdata: ref file is empty: $REF_FILE" >&2; exit 1; }

# Pick a downloader once. busybox wget needs ca-certificates installed for HTTPS.
if command -v curl >/dev/null 2>&1; then
    DOWNLOADER=curl
elif command -v wget >/dev/null 2>&1; then
    DOWNLOADER=wget
else
    echo "fetch-tessdata: neither curl nor wget is available" >&2
    exit 1
fi

download() {
    # $1 = url, $2 = output path. Fails loudly (no silent half-downloads).
    #
    # Retries transient failures with backoff. raw.githubusercontent.com rate-limits
    # shared CI IP pools with HTTP 429, so a single fetch is flaky under load. curl's
    # own --retry already backs off on transient errors (incl. 429 since curl 7.66,
    # honouring any Retry-After); the outer loop is the portable safety net that also
    # covers busybox wget, which has no retry flags.
    attempt=1
    max_attempts=5
    while :; do
        if [ "$DOWNLOADER" = curl ]; then
            curl -fsSL --connect-timeout 20 --retry 3 --retry-delay 2 "$1" -o "$2" && return 0
        else
            wget -q -O "$2" "$1" && return 0
        fi
        [ "$attempt" -ge "$max_attempts" ] && return 1
        delay=$((attempt * 4))
        echo "fetch-tessdata: download failed (attempt $attempt/$max_attempts), retrying in ${delay}s" >&2
        sleep "$delay"
        attempt=$((attempt + 1))
    done
}

BASE_URL="https://raw.githubusercontent.com/tesseract-ocr/tessdata_best/$REF"

mkdir -p "$DEST"

while IFS= read -r lang || [ -n "$lang" ]; do
    # Strip all whitespace (handles CRLF); skip blank lines and "# comments".
    lang=$(printf '%s' "$lang" | tr -d '[:space:]')
    [ -n "$lang" ] || continue
    case "$lang" in \#*) continue ;; esac

    target="$DEST/$lang.traineddata"
    if [ -f "$target" ]; then
        echo "fetch-tessdata: $lang.traineddata present, skip"
        continue
    fi

    tmp="$target.tmp.$$"
    url="$BASE_URL/$lang.traineddata"
    echo "fetch-tessdata: downloading $lang.traineddata @ ${REF}"
    # Treat a download error OR an empty result as failure, so a bad fetch never
    # gets promoted to a "present" model (which the skip-if-present check would
    # then never re-download). Both curl -f and busybox wget already exit non-zero
    # on HTTP 404/error; the -s check is belt-and-suspenders against a 0-byte body.
    if ! download "$url" "$tmp" || [ ! -s "$tmp" ]; then
        rm -f "$tmp"
        echo "fetch-tessdata: failed to download $url" >&2
        exit 1
    fi
    mv "$tmp" "$target"
done < "$MANIFEST"

echo "fetch-tessdata: done -> $DEST"
