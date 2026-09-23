using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRetriesAndLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                schema: "processing",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                schema: "processing",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                schema: "processing",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RunAfter",
                schema: "processing",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true);

            // The startup orphan sweep is gone; an expired lease is what makes a crashed job claimable now.
            // A childless row left InProgress by a process that died before this upgrade has no lease at all,
            // and a NULL lease is never reclaimed — so hand every such orphan an already-expired one. This is
            // the sweep's last run, done through the new path. Migrations complete before any hosted service
            // starts, so nothing is running that this could wrongly reclaim. TotalChildCount = 0 is the same
            // load-bearing guard the sweep had: a parent with children is waiting on them, not orphaned.
            migrationBuilder.Sql(
                """
                UPDATE processing.jobs
                SET "LockedUntil" = now()
                WHERE "Status" = 'InProgress' AND "TotalChildCount" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attempts",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                schema: "processing",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "RunAfter",
                schema: "processing",
                table: "jobs");
        }
    }
}
