using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientsDemo.Migrations
{
    /// <inheritdoc />
    public partial class ClientNomComplet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Généré après le renommage de la propriété Client.Nom.
            migrationBuilder.DropColumn(
                name: "Nom",
                table: "Clients");

            migrationBuilder.AddColumn<string>(
                name: "NomComplet",
                table: "Clients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NomComplet",
                table: "Clients");

            migrationBuilder.AddColumn<string>(
                name: "Nom",
                table: "Clients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
