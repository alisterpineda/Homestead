#!/usr/bin/env bash
set -euo pipefail

sudo apt-get update && sudo apt-get install -y tmux tesseract-ocr

cp .devcontainer/tmux.conf "$HOME/.tmux.conf"

# Local tools (dotnet-ef, aieval) are not restored implicitly by build/test.
dotnet tool restore

(cd Homestead.WebApp && npm install)

(cd Homestead.E2E && npm install && npx playwright install --with-deps)

npx -y skills@latest add angular/skills -g -s angular-developer -a claude-code -y --copy

# Models come from here, not apt language packs, so dev/test/prod use byte-identical
# tessdata_best (eng+fra). Ocr__Engines__tesseract__TessDataPath points at this path.
sudo sh scripts/fetch-tessdata.sh /usr/share/tessdata
