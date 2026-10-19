using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.MaxiAffiliates.Data.Migrations
{
    /// <inheritdoc />
    public partial class SellerCallbacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CallbackDeliveries",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    EventId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RedeliveryOfId = table.Column<string>(type: "text", nullable: true),
                    InvoiceId = table.Column<string>(type: "text", nullable: true),
                    Facts = table.Column<string>(type: "text", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastHttpStatus = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallbackDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CallbackSettings",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SecretProtected = table.Column<string>(type: "text", nullable: true),
                    SecretCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallbackSettings", x => x.StoreId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallbackDeliveries_Status_NextAttemptAt",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "CallbackDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CallbackDeliveries_StoreId_CreatedAt",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "CallbackDeliveries",
                columns: new[] { "StoreId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CallbackDeliveries_StoreId_EventId",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "CallbackDeliveries",
                columns: new[] { "StoreId", "EventId" },
                unique: true,
                filter: "\"RedeliveryOfId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallbackDeliveries",
                schema: "BTCPayServer.Plugins.MaxiAffiliates");

            migrationBuilder.DropTable(
                name: "CallbackSettings",
                schema: "BTCPayServer.Plugins.MaxiAffiliates");
        }
    }
}
