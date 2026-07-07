using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helm.Cpq.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_ref",
                schema: "cpq",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    company_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_ref", x => x.customer_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_ref",
                schema: "cpq");
        }
    }
}
