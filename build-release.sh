#!/usr/bin/env bash
# Release build + .btcpay package (BTCPay PluginPacker). Output: dist/BTCPayServer.Plugins.MaxiAffiliates/<version>/
set -euo pipefail
cd "$(dirname "$0")"
P=BTCPayServer.Plugins.MaxiAffiliates
rm -rf "dist/publish"
dotnet publish "src/$P/$P.csproj" -c Release -o "dist/publish" -m:2
# Never ship dev artifacts
find dist/publish -maxdepth 1 \( -name '*.pdb' -o -name 'appsettings*.json' \) -delete
dotnet run --project submodules/btcpayserver/BTCPayServer.PluginPacker -c Release -- "dist/publish" "$P" "dist"
dotnet build-server shutdown >/dev/null 2>&1 || true
find dist/$P -name '*.btcpay' -printf '%p %s bytes\n'
