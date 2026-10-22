using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaChatNet.Server.Migrations
{
    /// <inheritdoc />
    public partial class EditadoMensaje : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Editado",
                table: "Mensajes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Editado",
                table: "Mensajes");
        }
    }
}
