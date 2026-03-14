using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdfAgentMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentActivityLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AgentName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PipelineRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PipelineName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ResultMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentActivityLogs_AgentName_Timestamp",
                table: "AgentActivityLogs",
                columns: new[] { "AgentName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentActivityLogs_PipelineRunId",
                table: "AgentActivityLogs",
                column: "PipelineRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentActivityLogs_Timestamp",
                table: "AgentActivityLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentActivityLogs");
        }
    }
}
