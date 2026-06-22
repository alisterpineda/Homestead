#!/usr/bin/env bash
set -euo pipefail

sudo apt-get update && sudo apt-get install -y tmux

cp .devcontainer/tmux.conf "$HOME/.tmux.conf"

dotnet tool install -g aspire.cli

(cd Homestead.WebApp && npm install)

(cd Homestead.E2E && npm install && npx playwright install --with-deps)

npm install -g @anthropic-ai/claude-code

npx -y skills@latest add angular/skills -g -s angular-developer -a claude-code -y --copy

# OCR: Tesseract binary only — the pinned tessdata_best models (eng+fra) come from the
# provisioning script, not apt language packs, so dev/test/prod use byte-identical models.
sudo apt-get install -y tesseract-ocr
sudo sh scripts/fetch-tessdata.sh /usr/share/tessdata
