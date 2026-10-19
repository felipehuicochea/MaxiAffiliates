#!/usr/bin/env bash
# Runs BTCPay Server 2.4.5 (from the submodule) on regtest with the MaxiAffiliates plugin loaded via DEBUG_PLUGINS.
# Requires: dev stack up (dev/docker-compose.yml) and ./plugin-register.sh run once.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export DOTNET_GCHeapHardLimit=0x30000000   # ~768 MB cap, shared box
export BTCPAY_NETWORK=regtest BTCPAY_CHAINS=btc \
       BTCPAY_BTCEXPLORERURL=http://127.0.0.1:32838/ \
       BTCPAY_POSTGRES="User ID=postgres;Host=127.0.0.1;Port=39372;Database=btcpayserver" \
       BTCPAY_BIND=127.0.0.1:14142 BTCPAY_DATADIR="$ROOT/dev/.btcpay-data"
cd "$ROOT/submodules/btcpayserver/BTCPayServer"
exec env "BTCPAY_ALLOW-ADMIN-REGISTRATION=true" "BTCPAY_DISABLE-REGISTRATION=false" dotnet bin/Debug/net10.0/BTCPayServer.dll
