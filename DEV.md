# MaxiAffiliates: developer guide (local box, regtest only)

Plugin `BTCPayServer.Plugins.MaxiAffiliates`, built against BTCPay Server **v2.4.5** (`submodules/btcpayserver`, commit 5d0745c) with .NET SDK 10 (`~/.dotnet`).

## Environment
```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$HOME/.dotnet/tools:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
pgrep dockerd >/dev/null || (sudo dockerd >/tmp/dockerd.log 2>&1 &)   # no systemd on the box
```

## Regtest stack (bitcoind + Postgres + NBXplorer, no Lightning)
```bash
cd dev && sudo docker compose up -d      # start (~150 MB total, mem limits set)
sudo docker compose ps
sudo docker compose stop                 # stop when idle (keeps data)
sudo docker compose down -v              # wipe everything
```
Ports (127.0.0.1 only): Postgres 39372, bitcoind RPC 43782, NBXplorer 32838. The stack uses host networking because container-to-container bridge networking doesn't work on this box.

## Build and test
```bash
dotnet build src/BTCPayServer.Plugins.MaxiAffiliates -m:2
dotnet build tests/BTCPayServer.Plugins.MaxiAffiliates.Tests -m:2
dotnet build-server shutdown   # after building: frees ~2 GB (Roslyn server + MSBuild nodes) on the shared box
tests/BTCPayServer.Plugins.MaxiAffiliates.Tests/bin/Debug/net10.0/BTCPayServer.Plugins.MaxiAffiliates.Tests    # xUnit v3 runner
#   integration tests need the Postgres from the stack; filter with: -trait- "Category=Integration"
```
(`dotnet test` needs a root global.json for Microsoft.Testing.Platform. Run the test executable directly, as above.)

## Run BTCPay with the plugin
```bash
./plugin-register.sh                       # once: writes DEBUG_PLUGINS into submodule appsettings.dev.json
dotnet build submodules/btcpayserver/BTCPayServer -m:2
nohup dev/run-btcpay.sh > /tmp/btcpay.log 2>&1 &     # http://127.0.0.1:14142, GC heap capped ~768 MB
curl -s 127.0.0.1:14142/plugins/maxiaffiliates/health  # plugin + migration status (JSON)
pkill -f BTCPayServer.dll                  # stop
```
The dev admin login (regtest only) is in `dev/.btcpay-data/dev-admin.txt`, which is gitignored and not printed. Admin: store menu → Affiliates (`/stores/{storeId}/plugins/maxiaffiliates/affiliates`). Public signup: `/plugins/maxiaffiliates/{storeId}/signup`.

## M2 acceptance walk-through + screenshots
```bash
python3 -m venv ~/.venvs/pw && ~/.venvs/pw/bin/pip install playwright   # once; uses the installed google-chrome
~/.venvs/pw/bin/python dev/screens_m2.py    # needs stack + server running; writes docs/screens/m2/*.png
```

## Migrations
The plugin project doesn't copy BTCPay assemblies, so `dotnet ef` uses the helper startup project `dev/MaxiAffiliates.DesignTime`. It needs the dev Postgres running.
```bash
dotnet build dev/MaxiAffiliates.DesignTime
dotnet ef migrations add <Name> --no-build --project src/BTCPayServer.Plugins.MaxiAffiliates \
  --startup-project dev/MaxiAffiliates.DesignTime --context MaxiAffiliatesDbContext --output-dir Data/Migrations
```
Then follow BTCPay's convention: move the `[DbContext]`/`[Migration]` attributes from `.Designer.cs` into the migration, delete the `.Designer.cs`, and remove `Down()`. Migrations run automatically at server start (`AddPluginDbContext`).

## TODO placeholders (unconfirmed)
- MIT license holder (LICENSE): TODO
- README tip address: TODO
- Public repo (planned under felipehuicochea): not created; no remote configured.
