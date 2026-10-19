using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.MaxiAffiliates.Data.Migrations
{
    [DbContext(typeof(MaxiAffiliatesDbContext))]
    [Migration("20261019171328_PayoutCycles")]
    public partial class PayoutCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AdjustmentSats",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "CommissionCount",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CycleId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Error",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GrossSats",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "PayoutCycles",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    CycleKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Manual = table.Column<bool>(type: "boolean", nullable: false),
                    AutoApproved = table.Column<bool>(type: "boolean", nullable: false),
                    MinPayoutSats = table.Column<long>(type: "bigint", nullable: false),
                    BatchCount = table.Column<int>(type: "integer", nullable: false),
                    TotalSats = table.Column<long>(type: "bigint", nullable: false),
                    CarriedOverCount = table.Column<int>(type: "integer", nullable: false),
                    CarriedOverSats = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutCycles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayoutBatches_CycleId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutBatches_PayoutId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutCycles_StoreId_CycleKey",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutCycles",
                columns: new[] { "StoreId", "CycleKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PayoutBatches_PayoutCycles_CycleId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "PayoutBatches",
                column: "CycleId",
                principalSchema: "BTCPayServer.Plugins.MaxiAffiliates",
                principalTable: "PayoutCycles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
