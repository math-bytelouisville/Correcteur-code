using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientsDemo.Migrations
{
    /// <inheritdoc />
    public partial class FaxVersDateInscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Le fax n'est plus utilisé ; la date d'inscription est ajoutée.
            migrationBuilder.DropColumn(
                name: "Fax",
                table: "Clients");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateInscription",
                table: "Clients",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateInscription",
                table: "Clients");

            migrationBuilder.AddColumn<string>(
                name: "Fax",
                table: "Clients",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
