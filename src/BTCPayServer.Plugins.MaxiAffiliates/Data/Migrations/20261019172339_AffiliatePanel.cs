using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.MaxiAffiliates.Data.Migrations
{
    [DbContext(typeof(MaxiAffiliatesDbContext))]
    [Migration("20261019172339_AffiliatePanel")]
    public partial class AffiliatePanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LinkClicks",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Affiliates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PayoutDestinationUpdatedAt",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "Affiliates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccessTokens",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    AffiliateId = table.Column<string>(type: "text", nullable: true),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessTokens_Affiliates_AffiliateId",
                        column: x => x.AffiliateId,
                        principalSchema: "BTCPayServer.Plugins.MaxiAffiliates",
                        principalTable: "Affiliates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_AffiliateId_Kind_CreatedAt",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "AccessTokens",
                columns: new[] { "AffiliateId", "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_TokenHash",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "AccessTokens",
                column: "TokenHash",
                unique: true);
        }
    }
}
