#!/usr/bin/env bash
set -euo pipefail

sudo apt-get update && sudo apt-get install -y tmux tesseract-ocr poppler-utils

cp .devcontainer/tmux.conf "$HOME/.tmux.conf"

# Local tools (dotnet-ef) are not restored implicitly by build/test.
dotnet tool restore

(cd Homestead.WebApp && npm install)

(cd Homestead.E2E && npm install && npx playwright install --with-deps)

# playwright-cli drives the self-qa subagent (.claude/agents/self-qa.md); pinned — the CLI is still 0.x.
# The global install ships no browser: its bundled playwright-core pins its own chromium revision, which the
# Homestead.E2E install above does not provide (different revision), so install it explicitly.
npm install -g @playwright/cli@0.1.19
playwright-cli install-browser chromium

npx -y skills@latest add angular/skills -g -s angular-developer -a claude-code -y --copy

# Models come from here, not apt language packs, so dev/test/prod use byte-identical
# tessdata_best (eng+fra). Ocr__Engines__tesseract__TessDataPath points at this path.
sudo sh scripts/fetch-tessdata.sh /usr/share/tessdata
