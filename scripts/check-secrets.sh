#!/usr/bin/env bash
set -euo pipefail

echo "==> Scanning for secrets and credentials..."

if ! command -v gitleaks &>/dev/null; then
    echo "ERROR: 'gitleaks' not found."
    echo "Install: https://github.com/gitleaks/gitleaks#installing"
    echo "  macOS:  brew install gitleaks"
    echo "  Linux:  download from https://github.com/gitleaks/gitleaks/releases"
    exit 1
fi

gitleaks detect --source . --config .gitleaks.toml --redact --no-banner

echo "No secrets detected."
