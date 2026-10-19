using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.MaxiAffiliates.Data.Migrations
{
    [DbContext(typeof(MaxiAffiliatesDbContext))]
    [Migration("20261019165219_AddDefaultFirstN")]
    public partial class AddDefaultFirstN : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultFirstN",
                schema: "BTCPayServer.Plugins.MaxiAffiliates",
                table: "StoreSettings",
                type: "integer",
                nullable: true);
        }
    }
}
