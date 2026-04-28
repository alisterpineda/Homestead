using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ScheduleId",
                schema: "processing",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "schedules",
                schema: "processing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    JobType = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CronExpression = table.Column<string>(type: "text", nullable: false),
                    TimeZone = table.Column<string>(type: "text", nullable: false, defaultValue: "Etc/UTC"),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastJobId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_schedules_jobs_LastJobId",
                        column: x => x.LastJobId,
                        principalSchema: "processing",
                        principalTable: "jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_jobs_ScheduleId",
                schema: "processing",
                table: "jobs",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_schedules_CreatedByUserId",
                schema: "processing",
                table: "schedules",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_schedules_IsEnabled_NextRunAt",
                schema: "processing",
                table: "schedules",
                columns: new[] { "IsEnabled", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_schedules_JobType",
                schema: "processing",
                table: "schedules",
                column: "JobType");

            migrationBuilder.CreateIndex(
                name: "IX_schedules_LastJobId",
                schema: "processing",
                table: "schedules",
                column: "LastJobId");

            migrationBuilder.AddForeignKey(
                name: "FK_jobs_schedules_ScheduleId",
                schema: "processing",
                table: "jobs",
                column: "ScheduleId",
                principalSchema: "processing",
                principalTable: "schedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_jobs_schedules_ScheduleId",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropTable(
                name: "schedules",
                schema: "processing");

            migrationBuilder.DropIndex(
                name: "IX_jobs_ScheduleId",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "ScheduleId",
                schema: "processing",
                table: "jobs");
        }
    }
}
