using BTCPayServer.Plugins.MaxiAffiliates.Attribution;
using BTCPayServer.Plugins.MaxiAffiliates.Controllers;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Plugins.MaxiAffiliates.Services;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Npgsql;

namespace BTCPayServer.Plugins.MaxiAffiliates.Tests;

class FixedClock(DateTimeOffset now) : TimeProvider { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }

public class ReferralCookieTests
{
    static readonly DateTimeOffset T = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    [Fact] public void RoundTrip()
    {
        var (c, s) = ReferralCookie.Parse(ReferralCookie.Format("ABCD2345", T));
        Assert.Equal("ABCD2345", c); Assert.Equal(T, s);
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("NODOT")] [InlineData("ABC.x")] [InlineData(".123")] [InlineData("A B.123")] [InlineData("<script>.1")]
    public void Malformed(string? v) => Assert.Null(ReferralCookie.Parse(v).code);
    [Fact] public void CookieNameIsPerStore() => Assert.Equal("maxiaff_ref_S1", ReferralCookie.Name("S1"));
    [Theory] [InlineData("https://x.example/a?b=1", true)] [InlineData("http://127.0.0.1:8099/", true)] [InlineData("javascript:alert(1)", false)] [InlineData("/relative", false)] [InlineData(null, false)]
    public void LandingValidation(string? url, bool ok) => Assert.Equal(ok, UIReferralController.TryGetLanding(url, out _));
}

public class ReferralPickTests
{
    static readonly DateTimeOffset T = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    readonly FixedClock _clock = new(T);
    ReferralService Svc() => new(new AffiliateService(null!, _clock), new FirstClickAttributionStrategy(), _clock);

    [Fact] public void ExplicitRefUsedWhenNoCookie() => Assert.Equal("BOB", Svc().PickCode(null, "bob"));
    [Fact] public void CookieBeatsExplicitRefWithin30Days() =>
        Assert.Equal("ALICE", Svc().PickCode(ReferralCookie.Format("ALICE", T.AddDays(-10)), "BOB"));
    [Fact] public void ExpiredCookieLosesToExplicitRef() =>
        Assert.Equal("BOB", Svc().PickCode(ReferralCookie.Format("ALICE", T.AddDays(-31)), "BOB"));
    [Fact] public void GarbageRefIgnored() => Assert.Null(Svc().PickCode(null, "<x>"));

    [Fact] public void FirstClickCookieNotOverwritten() =>
        Assert.Null(Svc().CookieAfterClick(ReferralCookie.Format("ALICE", T.AddDays(-1)), "BOB"));
    [Fact] public void NewCookieWhenNoneOrExpired()
    {
        Assert.Equal(ReferralCookie.Format("BOB", T), Svc().CookieAfterClick(null, "BOB"));
        Assert.Equal(ReferralCookie.Format("BOB", T), Svc().CookieAfterClick(ReferralCookie.Format("ALICE", T.AddDays(-40)), "BOB"));
    }
}

[Trait("Category", "Integration")]
public class ReferralResolveIntegrationTests : IAsyncLifetime
{
    const string Server = "Host=127.0.0.1;Port=39372;User ID=postgres";
    readonly string _db = "maxiaff_m3_" + Guid.NewGuid().ToString("N")[..8];
    AffiliateService _aff = null!; ReferralService _ref = null!; bool _ok;
    class Factory(DbContextOptions<MaxiAffiliatesDbContext> o) : IDbContextFactory<MaxiAffiliatesDbContext> { public MaxiAffiliatesDbContext CreateDbContext() => new(o); }

    public async ValueTask InitializeAsync()
    {
        try { await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync(); await new NpgsqlCommand($"CREATE DATABASE \"{_db}\"", a).ExecuteNonQueryAsync(); _ok = true; }
        catch (Exception) { return; }
        var o = new DbContextOptionsBuilder<MaxiAffiliatesDbContext>().UseNpgsql($"{Server};Database={_db}").Options;
        await using (var c = new MaxiAffiliatesDbContext(o)) await c.Database.MigrateAsync();
        _aff = new AffiliateService(new Factory(o), TimeProvider.System);
        _ref = new ReferralService(_aff, new FirstClickAttributionStrategy(), TimeProvider.System);
    }
    public async ValueTask DisposeAsync()
    {
        if (!_ok) return; NpgsqlConnection.ClearAllPools();
        await using var a = new NpgsqlConnection(Server + ";Database=postgres"); await a.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_db}\" WITH (FORCE)", a).ExecuteNonQueryAsync();
    }
    async Task<Affiliate> Active(string store, string email)
    {
        var a = (await _aff.Signup(new SignupRequest(store, email, null, PayoutMethodKind.Lightning, "x@ln.example"), Network.RegTest)).Affiliate!;
        await _aff.Apply(store, a.Id, AffiliateAction.Approve); return a;
    }

    [Fact]
    public async Task ResolveOutcomes()
    {
        if (!_ok) Assert.Skip("dev Postgres not running");
        var alice = await Active("s1", "alice@example.com");
        var bob = await Active("s1", "bob@example.com");
        var cookieAlice = ReferralCookie.Format(alice.Code, DateTimeOffset.UtcNow.AddDays(-2));

        var r = await _ref.Resolve("s1", cookieAlice, bob.Code, "buyer@example.com");
        Assert.Equal(RefOutcome.Attributed, r.Outcome); Assert.Equal(alice.Id, r.Affiliate!.Id); // first click wins

        r = await _ref.Resolve("s1", null, bob.Code.ToLowerInvariant(), "BOB@example.com");
        Assert.Equal(RefOutcome.SelfReferral, r.Outcome); Assert.Null(r.Affiliate);

        await _aff.Apply("s1", bob.Id, AffiliateAction.Ban);
        Assert.Equal(RefOutcome.UnknownOrInactive, (await _ref.Resolve("s1", null, bob.Code, null)).Outcome);
        Assert.Equal(RefOutcome.UnknownOrInactive, (await _ref.Resolve("other", null, alice.Code, null)).Outcome); // store-scoped
        Assert.Equal(RefOutcome.None, (await _ref.Resolve("s1", null, null, null)).Outcome);
    }
}
