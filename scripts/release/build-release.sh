#!/usr/bin/env bash
# Builds the offline GHOST release package (Windows x64) on a build machine WITH internet. Not for the customer PC.
# Needs: dotnet SDK 10 (+ dotnet-ef tool), node + pnpm, curl, zip. Output: dist/GHOST-Release-<version>.zip
# Contents: api (self-contained, no .NET install needed), bootstrap tool, migration bundle, frontend (standalone) + node.exe,
# installer scripts, docs. SQL Server Express is NOT bundled (licence and size): put the Microsoft installer into prereq\.
set -euo pipefail
VERSION="${1:-$(date +%Y%m%d)-$(git rev-parse --short HEAD)}"
NODE_VERSION="${NODE_VERSION:-22.11.0}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/dist/GHOST-Release-$VERSION"
rm -rf "$OUT"; mkdir -p "$OUT"/{api,bootstrap,migrations,frontend,prereq,installer,docs}
cd "$ROOT/backend"
echo "1/6 API (self-contained win-x64)"
dotnet publish src/GHOST.API/GHOST.API.csproj -c Release -r win-x64 --self-contained true -o "$OUT/api" --nologo
echo "2/6 Bootstrap tool"
dotnet publish src/GHOST.Bootstrap/GHOST.Bootstrap.csproj -c Release -r win-x64 --self-contained true -o "$OUT/bootstrap" --nologo
echo "3/6 Migration bundle"
export ConnectionStrings__DefaultConnection='Server=localhost;Database=GHOST_EF_DESIGN_ONLY;Integrated Security=True;TrustServerCertificate=True'
export Authentication__Jwt__Issuer=x Authentication__Jwt__Audience=y Authentication__Jwt__SigningKey=0123456789012345678901234567890123456789 Authentication__Jwt__AccessTokenLifetimeMinutes=10
dotnet-ef migrations bundle -p src/GHOST.Infrastructure -s src/GHOST.API --self-contained -r win-x64 --force -o "$OUT/migrations/ghost-migrate.exe"
unset ConnectionStrings__DefaultConnection
echo "4/6 Frontend (standalone)"
cd "$ROOT/frontend"
export NEXT_TELEMETRY_DISABLED=1 NEXT_PUBLIC_GHOST_API_BASE_URL="http://localhost:5153"
pnpm install --frozen-lockfile
pnpm build
cp -r .next/standalone/. "$OUT/frontend/"
mkdir -p "$OUT/frontend/.next"; cp -r .next/static "$OUT/frontend/.next/static"
[ -d public ] && cp -r public "$OUT/frontend/public"
rm -f tsconfig.tsbuildinfo next-env.d.ts
echo "5/6 Node runtime $NODE_VERSION"
curl -fsSL "https://nodejs.org/dist/v$NODE_VERSION/win-x64/node.exe" -o "$OUT/frontend/node.exe"
echo "6/6 Installer and docs"
cp "$ROOT"/deploy/installer/* "$OUT/installer/"
cp "$ROOT/docs/deployment/INSTALL-GUIDE.md" "$ROOT/docs/deployment/CLEAN-MACHINE-TEST-CHECKLIST.md" "$OUT/docs/"
cp "$ROOT/deploy/installer/README-FIRST.txt" "$OUT/README-FIRST.txt"
( cd "$OUT" && find . -type f ! -name SHA256SUMS.txt -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS.txt )
( cd "$ROOT/dist" && zip -qr "GHOST-Release-$VERSION.zip" "GHOST-Release-$VERSION" )
echo "Done: $ROOT/dist/GHOST-Release-$VERSION.zip"
