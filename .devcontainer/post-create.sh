#!/usr/bin/env bash
set -euo pipefail

dotnet tool install -g aspire.cli

(cd Homestead.WebApp && npm install)

(cd Homestead.E2E && npm install && npx playwright install --with-deps)

npm install -g @anthropic-ai/claude-code

npx -y skills@latest add angular/skills -g -s angular-developer -a claude-code -y --copy
