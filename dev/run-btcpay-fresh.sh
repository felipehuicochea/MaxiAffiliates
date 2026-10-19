#!/usr/bin/env bash
# Fresh BTCPay 2.4.5 (Release build, Production environment, NO plugin preloaded, NO SSRF exception) for the
# .btcpay upload test. Separate datadir and database; regtest only.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export DOTNET_GCHeapHardLimit=0x30000000 ASPNETCORE_ENVIRONMENT=Production
export BTCPAY_NETWORK=regtest BTCPAY_CHAINS=btc \
       BTCPAY_BTCEXPLORERURL=http://127.0.0.1:32838/ \
       BTCPAY_POSTGRES="User ID=postgres;Host=127.0.0.1;Port=39372;Database=btcpayfresh" \
       BTCPAY_BIND=127.0.0.1:14160 BTCPAY_DATADIR="$ROOT/dev/.btcpay-fresh"
cd "$ROOT/submodules/btcpayserver/BTCPayServer"   # content root (wwwroot); Release build never reads appsettings.dev.json
exec env "BTCPAY_ALLOW-ADMIN-REGISTRATION=true" dotnet bin/Release/net10.0/BTCPayServer.dll
