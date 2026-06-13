using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddNestedAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentAreaId",
                schema: "inventory",
                table: "areas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_areas_ParentAreaId",
                schema: "inventory",
                table: "areas",
                column: "ParentAreaId");

            migrationBuilder.AddForeignKey(
                name: "FK_areas_areas_ParentAreaId",
                schema: "inventory",
                table: "areas",
                column: "ParentAreaId",
                principalSchema: "inventory",
                principalTable: "areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_areas_areas_ParentAreaId",
                schema: "inventory",
                table: "areas");

            migrationBuilder.DropIndex(
                name: "IX_areas_ParentAreaId",
                schema: "inventory",
                table: "areas");

            migrationBuilder.DropColumn(
                name: "ParentAreaId",
                schema: "inventory",
                table: "areas");
        }
    }
}
