using BTCPayServer.Plugins.MaxiAffiliates.Attribution;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BTCPayServer.Plugins.MaxiAffiliates.Tests;

public class AttributionTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    readonly FirstClickAttributionStrategy _s = new();

    [Fact] public void DefaultsAreFirstClick30Days()
    {
        Assert.Equal("first-click", _s.Name);
        Assert.Equal(TimeSpan.FromDays(30), _s.CookieLifetime);
        Assert.Equal(TimeSpan.FromDays(30), AttributionDefaults.CookieLifetime);
    }
    [Fact] public void FirstCodeWinsWithinWindow() =>
        Assert.Equal("ALICE", _s.Resolve("ALICE", Now.AddDays(-29), "bob", Now));
    [Fact] public void ExpiredCookieIsReplaced() =>
        Assert.Equal("BOB", _s.Resolve("ALICE", Now.AddDays(-31), " bob ", Now));
    [Fact] public void NoExistingTakesIncomingNormalized() =>
        Assert.Equal("BOB", _s.Resolve(null, null, "bob", Now));
    [Fact] public void NothingYieldsNull() =>
        Assert.Null(_s.Resolve(null, null, "  ", Now));
}

public class ModelTests
{
    static MaxiAffiliatesDbContext Ctx() => new(new DbContextOptionsBuilder<MaxiAffiliatesDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=39372;User ID=postgres;Database=unused").Options);

    [Fact] public void AllTablesInPluginSchema()
    {
        using var db = Ctx();
        var types = db.Model.GetEntityTypes().ToList();
        Assert.Equal(12, types.Count);
        Assert.All(types, t => Assert.Equal(MaxiAffiliatesDbContext.Schema, t.GetSchema()));
    }

    [Theory]
    [InlineData(typeof(Affiliate), "StoreId,Code")]
    [InlineData(typeof(Affiliate), "StoreId,Email")]
    [InlineData(typeof(Referral), "StoreId,CustomerKey")]
    [InlineData(typeof(Commission), "StoreId,InvoiceId")]
    public void UniqueIndexes(Type entity, string cols)
    {
        using var db = Ctx();
        var idx = db.Model.FindEntityType(entity)!.GetIndexes()
            .Single(i => string.Join(",", i.Properties.Select(p => p.Name)) == cols);
        Assert.True(idx.IsUnique);
    }

    [Fact] public void EnumsStoredAsStrings()
    {
        using var db = Ctx();
        var p = db.Model.FindEntityType(typeof(Commission))!.FindProperty(nameof(Commission.Status))!;
        Assert.Equal(typeof(string), p.GetProviderClrType());
    }

    [Fact] public void SettingsDefaultsMatchAgreedDefaults()
    {
        var s = new AffiliateStoreSettings();
        Assert.Equal(20m, s.DefaultPercent);
        Assert.Equal(CommissionScope.Lifetime, s.DefaultScope);
        Assert.Equal(30, s.HoldDays);
        Assert.True(s.LightningEnabled);
        Assert.True(s.OnChainEnabled);
        Assert.False(s.AutoApprovePayouts);
        Assert.Equal(PayoutFrequency.Monthly, s.PayoutFrequency);
        Assert.Null(s.BaseCurrencyOverride);
        Assert.Equal(0, s.MinPayoutSats);
    }

    [Fact] public void MigrationsAreDiscovered()
    {
        using var db = Ctx();
        Assert.Contains(db.Database.GetMigrations(), m => m.EndsWith("_InitialCreate"));
        Assert.False(db.Database.HasPendingModelChanges(), "Model has changes not captured in a migration");
    }
}

/// <summary>Runs against the dev regtest Postgres (127.0.0.1:39372). Creates and drops a throwaway database.</summary>
[Trait("Category", "Integration")]
public class MigrationIntegrationTests
{
    const string Server = "Host=127.0.0.1;Port=39372;User ID=postgres";

    [Fact]
    public async Task MigrateFreshDatabaseAndEnforceConstraints()
    {
        var dbName = "maxiaff_test_" + Guid.NewGuid().ToString("N")[..8];
        await using (var admin = new NpgsqlConnection(Server + ";Database=postgres"))
        {
            try { await admin.OpenAsync(); }
            catch (Exception) { Assert.Skip("dev Postgres not running (cd dev && docker compose up -d)"); }
            await using var c = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
            await c.ExecuteNonQueryAsync();
        }
        try
        {
            var opts = new DbContextOptionsBuilder<MaxiAffiliatesDbContext>()
                .UseNpgsql($"{Server};Database={dbName}",
                    o => o.MigrationsHistoryTable(MaxiAffiliatesDbContext.MigrationTable)).Options;
            await using var db = new MaxiAffiliatesDbContext(opts);
            await db.Database.MigrateAsync();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            var now = DateTimeOffset.UtcNow;
            var aff = new Affiliate { StoreId = "s1", Email = "a@x.test", Code = "ALICE", PayoutMethod = PayoutMethodKind.Lightning, PayoutDestination = "alice@ln.test", CreatedAt = now, UpdatedAt = now };
            var refl = new Referral { StoreId = "s1", Affiliate = aff, CustomerKey = "cust1", Source = ReferralSource.Link, AttributedAt = now };
            db.Add(new Commission { StoreId = "s1", Affiliate = aff, Referral = refl, InvoiceId = "inv1", BaseAmount = 10, BaseCurrency = "USD", CommissionAmount = 2, AmountSats = 2000, Rate = 100000, CreatedAt = now, PayableAt = now.AddDays(30) });
            db.Add(new AffiliateStoreSettings { StoreId = "s1", UpdatedAt = now });
            await db.SaveChangesAsync();

            // one commission per invoice
            db.Add(new Commission { StoreId = "s1", AffiliateId = aff.Id, ReferralId = refl.Id, InvoiceId = "inv1", BaseCurrency = "USD", CreatedAt = now, PayableAt = now });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            // first-click binding: one referral per customer
            db.Add(new Referral { StoreId = "s1", AffiliateId = aff.Id, CustomerKey = "cust1", Source = ReferralSource.QueryParam, AttributedAt = now });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            var status = await db.Commissions.Select(c => c.Status).SingleAsync();
            Assert.Equal(CommissionStatus.Pending, status);
            var tables = await db.Database.SqlQueryRaw<string>(
                "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'BTCPayServer.Plugins.MaxiAffiliates' ORDER BY 1").ToListAsync();
            Assert.Equal(new[] { "AccessTokens", "Affiliates", "CallbackDeliveries", "CallbackSettings", "CommissionAdjustments", "CommissionRules", "Commissions", "PayoutBatches", "PayoutCycles", "PendingCredits", "Referrals", "StoreSettings" }, tables);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(Server + ";Database=postgres");
            await admin.OpenAsync();
            await using var c = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", admin);
            await c.ExecuteNonQueryAsync();
        }
    }
}
