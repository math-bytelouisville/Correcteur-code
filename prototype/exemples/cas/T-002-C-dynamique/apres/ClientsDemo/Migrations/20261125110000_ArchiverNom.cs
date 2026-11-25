using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClientsDemo.Migrations
{
    public partial class ArchiverNom : Migration
    {
        private static readonly string Source = "dbo.Clients";
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("INSERT INTO dbo.ClientsArchive (ClientId, Nom) SELECT Id, Nom FROM " + Source);

            migrationBuilder.DropColumn(
                name: "Nom",
                table: "Clients");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
