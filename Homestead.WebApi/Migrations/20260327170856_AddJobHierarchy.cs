using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddJobHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompletedChildCount",
                schema: "processing",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FailedChildCount",
                schema: "processing",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentJobId",
                schema: "processing",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalChildCount",
                schema: "processing",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_jobs_ParentJobId",
                schema: "processing",
                table: "jobs",
                column: "ParentJobId");

            migrationBuilder.CreateIndex(
                name: "IX_jobs_ParentJobId_Status",
                schema: "processing",
                table: "jobs",
                columns: new[] { "ParentJobId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_jobs_jobs_ParentJobId",
                schema: "processing",
                table: "jobs",
                column: "ParentJobId",
                principalSchema: "processing",
                principalTable: "jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_jobs_jobs_ParentJobId",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_jobs_ParentJobId",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "IX_jobs_ParentJobId_Status",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "CompletedChildCount",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "FailedChildCount",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "ParentJobId",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "TotalChildCount",
                schema: "processing",
                table: "jobs");
        }
    }
}
