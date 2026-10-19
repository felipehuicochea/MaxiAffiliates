using BTCPayServer.Abstractions.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.MaxiAffiliates.Data;

[PluginDatabase(MigrationTable)]
public class MaxiAffiliatesDbContext(DbContextOptions<MaxiAffiliatesDbContext> options)
    : BasePluginDbContext<MaxiAffiliatesDbContext>(options)
{
    public const string Schema = "BTCPayServer.Plugins.MaxiAffiliates";
    public const string MigrationTable = "MaxiAffiliates_Migrations";

    public MaxiAffiliatesDbContext() : this(new DbContextOptions<MaxiAffiliatesDbContext>()) { }

    public DbSet<Affiliate> Affiliates => Set<Affiliate>();
    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<Commission> Commissions => Set<Commission>();
    public DbSet<PayoutBatch> PayoutBatches => Set<PayoutBatch>();
    public DbSet<AffiliateStoreSettings> StoreSettings => Set<AffiliateStoreSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(Schema);

        b.Entity<Affiliate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.PayoutMethod).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Code).HasMaxLength(32);
            e.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.Email }).IsUnique();
        });
        b.Entity<CommissionRule>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Scope).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Value).HasPrecision(28, 8);
            e.HasIndex(x => new { x.StoreId, x.PlanId });
        });
        b.Entity<Referral>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Affiliate).WithMany(a => a.Referrals).HasForeignKey(x => x.AffiliateId).OnDelete(DeleteBehavior.Restrict);
            // First-click: a customer is bound to exactly one affiliate per store.
            e.HasIndex(x => new { x.StoreId, x.CustomerKey }).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.SubscriberId });
        });
        b.Entity<Commission>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.BaseAmount).HasPrecision(28, 8);
            e.Property(x => x.CommissionAmount).HasPrecision(28, 8);
            e.Property(x => x.Rate).HasPrecision(28, 8);
            e.Property(x => x.BaseCurrency).HasMaxLength(10);
            e.HasOne(x => x.Affiliate).WithMany(a => a.Commissions).HasForeignKey(x => x.AffiliateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Referral).WithMany(r => r.Commissions).HasForeignKey(x => x.ReferralId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PayoutBatch).WithMany(p => p.Commissions).HasForeignKey(x => x.PayoutBatchId).OnDelete(DeleteBehavior.SetNull);
            // Fraud rule: one commission per invoice.
            e.HasIndex(x => new { x.StoreId, x.InvoiceId }).IsUnique();
            e.HasIndex(x => new { x.StoreId, x.Status, x.PayableAt });
            e.HasIndex(x => new { x.AffiliateId, x.Status });
        });
        b.Entity<PayoutBatch>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.PayoutMethod).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Affiliate).WithMany().HasForeignKey(x => x.AffiliateId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.StoreId, x.State });
        });
        b.Entity<AffiliateStoreSettings>(e =>
        {
            e.HasKey(x => x.StoreId);
            e.Property(x => x.DefaultScope).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.PayoutFrequency).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.DefaultPercent).HasPrecision(9, 4);
            e.Property(x => x.BaseCurrencyOverride).HasMaxLength(10);
        });
    }
}
