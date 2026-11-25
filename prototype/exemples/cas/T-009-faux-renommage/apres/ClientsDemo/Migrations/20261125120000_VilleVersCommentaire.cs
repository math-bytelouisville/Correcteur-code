using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientsDemo.Migrations
{
    /// <inheritdoc />
    public partial class VilleVersCommentaire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La ville n'est plus saisie ; un commentaire libre est ajouté.
            migrationBuilder.DropColumn(
                name: "Ville",
                table: "Clients");

            migrationBuilder.AddColumn<string>(
                name: "Commentaire",
                table: "Clients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Commentaire",
                table: "Clients");

            migrationBuilder.AddColumn<string>(
                name: "Ville",
                table: "Clients",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
