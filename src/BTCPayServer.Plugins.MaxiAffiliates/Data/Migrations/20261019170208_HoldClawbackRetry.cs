using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.MaxiAffiliates.Data.Migrations
{
    [DbContext(typeof(MaxiAffiliatesDbContext))]
    [Migration("20261019170208_HoldClawbackRetry")]
    public partial class HoldClawbackRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SaleAmount",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Commissions",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "SaleCurrency",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Commissions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommissionAdjustments",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    AffiliateId = table.Column<string>(type: "text", nullable: true),
                    CommissionId = table.Column<string>(type: "text", nullable: true),
                    AmountSats = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayoutBatchId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommissionAdjustments_Commissions_CommissionId",
                        column: x => x.CommissionId,
                        principalSchema: "BTCPayServer.Plugins.MaxiAffiliates",
                        principalTable: "Commissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PendingCredits",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    InvoiceId = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GaveUp = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingCredits", x => new { x.StoreId, x.InvoiceId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAdjustments_AffiliateId_PayoutBatchId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "CommissionAdjustments",
                columns: new[] { "AffiliateId", "PayoutBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAdjustments_CommissionId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "CommissionAdjustments",
                column: "CommissionId");

            migrationBuilder.CreateIndex(
                name: "IX_CommissionAdjustments_StoreId_SourceRef",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "CommissionAdjustments",
                columns: new[] { "StoreId", "SourceRef" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingCredits_GaveUp_NextAttemptAt",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PendingCredits",
                columns: new[] { "GaveUp", "NextAttemptAt" });
        }
    }
}
