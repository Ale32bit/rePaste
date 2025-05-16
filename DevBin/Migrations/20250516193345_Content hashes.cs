using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevBin.Migrations
{
    /// <inheritdoc />
    public partial class Contenthashes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "HashId",
                table: "Pastes",
                type: "BINARY(32)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PasteContentHashId",
                table: "Pastes",
                type: "BINARY(32)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Contents",
                columns: table => new
                {
                    HashId = table.Column<byte[]>(type: "BINARY(32)", nullable: false),
                    Content = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contents", x => x.HashId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Pastes_PasteContentHashId",
                table: "Pastes",
                column: "PasteContentHashId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pastes_Contents_PasteContentHashId",
                table: "Pastes",
                column: "PasteContentHashId",
                principalTable: "Contents",
                principalColumn: "HashId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pastes_Contents_PasteContentHashId",
                table: "Pastes");

            migrationBuilder.DropTable(
                name: "Contents");

            migrationBuilder.DropIndex(
                name: "IX_Pastes_PasteContentHashId",
                table: "Pastes");

            migrationBuilder.DropColumn(
                name: "HashId",
                table: "Pastes");

            migrationBuilder.DropColumn(
                name: "PasteContentHashId",
                table: "Pastes");
        }
    }
}
