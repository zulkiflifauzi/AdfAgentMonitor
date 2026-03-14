using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AdfAgentMonitor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PipelineRunStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PipelineRunId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PipelineName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FactoryName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DiagnosisCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DiagnosisSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RemediationPlan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RemediationRisk = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineRunStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRunStates_FactoryName_Status",
                table: "PipelineRunStates",
                columns: new[] { "FactoryName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRunStates_Status",
                table: "PipelineRunStates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_PipelineRunStates_PipelineRunId",
                table: "PipelineRunStates",
                column: "PipelineRunId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PipelineRunStates");
        }
    }
}
