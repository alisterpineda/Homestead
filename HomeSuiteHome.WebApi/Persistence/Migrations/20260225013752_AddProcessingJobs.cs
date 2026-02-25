using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeSuiteHome.WebApi.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "processing");

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "processing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_jobs_EntityId",
                schema: "processing",
                table: "jobs",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_Status_Priority",
                schema: "processing",
                table: "jobs",
                columns: new[] { "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_jobs_Type",
                schema: "processing",
                table: "jobs",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jobs",
                schema: "processing");
        }
    }
}
