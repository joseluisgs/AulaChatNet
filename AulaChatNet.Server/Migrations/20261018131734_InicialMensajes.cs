using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaChatNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class InicialMensajes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Mensajes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Canal = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Usuario = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Texto = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mensajes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Mensajes_Canal_Timestamp",
                table: "Mensajes",
                columns: new[] { "Canal", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Mensajes_Timestamp",
                table: "Mensajes",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Mensajes");
        }
    }
}
