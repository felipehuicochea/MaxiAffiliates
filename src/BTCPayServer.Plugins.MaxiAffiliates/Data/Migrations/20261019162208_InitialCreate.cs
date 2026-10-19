using System;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.MaxiAffiliates.Data.Migrations
{
    [DbContext(typeof(MaxiAffiliatesDbContext))]
    [Migration("20261019162208_InitialCreate")]
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.Plugins.MaxiAffiliates");

            migrationBuilder.CreateTable(
                name: "Affiliates",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayoutMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayoutDestination = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Affiliates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommissionRules",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    PlanId = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FirstN = table.Column<int>(type: "integer", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StoreSettings",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    DefaultScope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HoldDays = table.Column<int>(type: "integer", nullable: false),
                    LightningEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    OnChainEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoApprovePayouts = table.Column<bool>(type: "boolean", nullable: false),
                    AutoApproveAffiliates = table.Column<bool>(type: "boolean", nullable: false),
                    MinPayoutSats = table.Column<long>(type: "bigint", nullable: false),
                    PayoutFrequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayoutDay = table.Column<int>(type: "integer", nullable: false),
                    BaseCurrencyOverride = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettings", x => x.StoreId);
                });

            migrationBuilder.CreateTable(
                name: "PayoutBatches",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    AffiliateId = table.Column<string>(type: "text", nullable: true),
                    CycleStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CycleEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AmountSats = table.Column<long>(type: "bigint", nullable: false),
                    PayoutMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Destination = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PullPaymentId = table.Column<string>(type: "text", nullable: true),
                    PayoutId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayoutBatches_Affiliates_AffiliateId",
                        column: x => x.AffiliateId,
                        principalSchema: "BTCPayServer.Plugins.MaxiAffiliates",
                        principalTable: "Affiliates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Referrals",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    AffiliateId = table.Column<string>(type: "text", nullable: true),
                    CustomerKey = table.Column<string>(type: "text", nullable: true),
                    SubscriberId = table.Column<string>(type: "text", nullable: true),
                    FirstInvoiceId = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttributedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreditedPayments = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Referrals_Affiliates_AffiliateId",
                        column: x => x.AffiliateId,
                        principalSchema: "BTCPayServer.Plugins.MaxiAffiliates",
                        principalTable: "Affiliates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Commissions",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    AffiliateId = table.Column<string>(type: "text", nullable: true),
                    ReferralId = table.Column<string>(type: "text", nullable: true),
                    InvoiceId = table.Column<string>(type: "text", nullable: true),
                    RuleId = table.Column<string>(type: "text", nullable: true),
                    PlanId = table.Column<string>(type: "text", nullable: true),
                    BaseAmount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    BaseCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    AmountSats = table.Column<long>(type: "bigint", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversedReason = table.Column<string>(type: "text", nullable: true),
                    PayoutBatchId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Commissions_Affiliates_AffiliateId",
                        column: x => x.AffiliateId,
                        principalSchema: "BTCPayServer.Plugins.MaxiAffiliates",
                        principalTable: "Affiliates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Commissions_PayoutBatches_PayoutBatchId",
                        column: x => x.PayoutBatchId,
                        principalSchema: "BTCPayServer.Plugins.MaxiAffiliates",
                        principalTable: "PayoutBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Commissions_Referrals_ReferralId",
                        column: x => x.ReferralId,
                        principalSchema: "BTCPayServer.Plugins.MaxiAffiliates",
                        principalTable: "Referrals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Affiliates_StoreId_Code",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Affiliates",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Affiliates_StoreId_Email",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Affiliates",
                columns: new[] { "StoreId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommissionRules_StoreId_PlanId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "CommissionRules",
                columns: new[] { "StoreId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_AffiliateId_Status",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Commissions",
                columns: new[] { "AffiliateId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_PayoutBatchId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Commissions",
                column: "PayoutBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_ReferralId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Commissions",
                column: "ReferralId");

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_StoreId_InvoiceId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Commissions",
                columns: new[] { "StoreId", "InvoiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_StoreId_Status_PayableAt",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Commissions",
                columns: new[] { "StoreId", "Status", "PayableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayoutBatches_AffiliateId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                column: "AffiliateId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutBatches_StoreId_State",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                columns: new[] { "StoreId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_AffiliateId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Referrals",
                column: "AffiliateId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_StoreId_CustomerKey",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Referrals",
                columns: new[] { "StoreId", "CustomerKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_StoreId_SubscriberId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Referrals",
                columns: new[] { "StoreId", "SubscriberId" });
        }
    }
}
