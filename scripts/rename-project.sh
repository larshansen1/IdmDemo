#!/usr/bin/env bash
# Usage: bash scripts/rename-project.sh MyNewProject
set -euo pipefail

NEW_NAME="${1:-}"
OLD_NAME="Template.Core"

if [ -z "$NEW_NAME" ]; then
    echo "Usage: bash scripts/rename-project.sh <ProjectName>"
    echo "  ProjectName must be PascalCase with no underscores (e.g. MyApp, IdmDemo, OrderService)"
    exit 1
fi

if echo "$NEW_NAME" | grep -qP '[^a-zA-Z0-9.]'; then
    echo "ERROR: '$NEW_NAME' contains invalid characters. Use PascalCase (e.g. MyApp)."
    exit 1
fi

if echo "$NEW_NAME" | grep -qP '^[a-z]'; then
    echo "ERROR: '$NEW_NAME' must start with an uppercase letter (PascalCase)."
    exit 1
fi

if echo "$NEW_NAME" | grep -q '_'; then
    echo "ERROR: '$NEW_NAME' contains underscores. Use PascalCase (e.g. MyApp not my_app)."
    exit 1
fi

echo "==> Renaming project from '$OLD_NAME' to '$NEW_NAME'..."

# 1. Rename directories
mv "src/$OLD_NAME"        "src/$NEW_NAME"
mv "tests/${OLD_NAME}.Tests" "tests/${NEW_NAME}.Tests"

# 2. Rename .csproj files
mv "src/$NEW_NAME/${OLD_NAME}.csproj"              "src/$NEW_NAME/${NEW_NAME}.csproj"
mv "tests/${NEW_NAME}.Tests/${OLD_NAME}.Tests.csproj" "tests/${NEW_NAME}.Tests/${NEW_NAME}.Tests.csproj"

# 3. Replace namespace/assembly references in all source files
find src tests -type f \( -name "*.cs" -o -name "*.csproj" \) \
    -exec sed -i "s/${OLD_NAME}/${NEW_NAME}/g" {} +

# 4. Update solution file
dotnet sln remove "src/$OLD_NAME/${OLD_NAME}.csproj"           2>/dev/null || true
dotnet sln remove "tests/${OLD_NAME}.Tests/${OLD_NAME}.Tests.csproj" 2>/dev/null || true
dotnet sln add "src/$NEW_NAME/${NEW_NAME}.csproj"
dotnet sln add "tests/${NEW_NAME}.Tests/${NEW_NAME}.Tests.csproj"

# 5. Verify
echo "==> Verifying build..."
dotnet build --no-incremental -v quiet

echo ""
echo "Done. Project renamed to '$NEW_NAME'."
