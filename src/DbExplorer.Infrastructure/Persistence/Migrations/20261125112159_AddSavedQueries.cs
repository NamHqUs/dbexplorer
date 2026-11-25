using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbExplorer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedQueries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Folder = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SqlText = table.Column<string>(type: "TEXT", maxLength: 100000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedQueries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavedQueries_OwnerUserId_ConnectionId",
                table: "SavedQueries",
                columns: new[] { "OwnerUserId", "ConnectionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavedQueries");
        }
    }
}
