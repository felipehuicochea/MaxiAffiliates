using BTCPayServer.Plugins.MaxiAffiliates.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.MaxiAffiliates.Data.Migrations
{
    [DbContext(typeof(MaxiAffiliatesDbContext))]
    [Migration("20261019163732_AddLandingUrl")]
    public partial class AddLandingUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LandingUrl",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "StoreSettings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }
    }
}
