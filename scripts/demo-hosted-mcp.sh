#!/usr/bin/env bash
set -euo pipefail

# - Usage -
#
#   bash scripts/demo-hosted-mcp.sh [-v|--verbose]
#
#   -v / --verbose   Print full request and response for every call.
#
#   Environment overrides:
#   Compatibility wrapper for:
#     bash scripts/demo-mcp-local-hosted-development.sh

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
exec "$SCRIPT_DIR/demo-mcp-local-hosted-development.sh" "$@"
