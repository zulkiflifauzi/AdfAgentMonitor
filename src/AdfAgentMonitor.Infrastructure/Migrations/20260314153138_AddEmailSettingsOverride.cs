using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdfAgentMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailSettingsOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailSettingsOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    SmtpHost = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SmtpPort = table.Column<int>(type: "int", nullable: true),
                    UseSsl = table.Column<bool>(type: "bit", nullable: true),
                    Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Password = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FromName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DashboardBaseUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSettingsOverrides", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailSettingsOverrides");
        }
    }
}
