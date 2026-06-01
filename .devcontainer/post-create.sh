#!/usr/bin/env bash
set -euo pipefail

sudo apt-get update && sudo apt-get install -y tmux

cp .devcontainer/tmux.conf "$HOME/.tmux.conf"

dotnet tool install -g aspire.cli

(cd Homestead.WebApp && npm install)

(cd Homestead.E2E && npm install && npx playwright install --with-deps)

npm install -g @anthropic-ai/claude-code

npx -y skills@latest add angular/skills -g -s angular-developer -a claude-code -y --copy
