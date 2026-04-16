using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Homestead.WebApi.Migrations
{
    /// <inheritdoc />
    public partial class DropAspNetIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preflight: stash the computed role on a temp column of identity.app_users
            // BEFORE the identity tables are dropped. It survives the table rename to
            // public.users below, and is copied into the real Role column at the end.
            migrationBuilder.Sql(@"
                ALTER TABLE identity.app_users
                    ADD COLUMN ""ComputedRole"" character varying(32);

                UPDATE identity.app_users u
                SET ""ComputedRole"" = COALESCE((
                    SELECT r.""Name""
                    FROM identity.user_roles ur
                    JOIN identity.roles r ON r.""Id"" = ur.""RoleId""
                    WHERE ur.""UserId"" = u.""Id""
                      AND r.""Name"" IN ('Admin','Contributor','Viewer','Guest')
                    ORDER BY CASE r.""Name""
                        WHEN 'Admin' THEN 1
                        WHEN 'Contributor' THEN 2
                        WHEN 'Viewer' THEN 3
                        WHEN 'Guest' THEN 4
                    END
                    LIMIT 1
                ), 'Contributor');
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_document_comments_app_users_CreatedByUserId",
                schema: "documents",
                table: "document_comments");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_app_users_CreatedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_app_users_DeletedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_app_users_OwnerUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_app_users_UpdatedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_group_members_app_users_UserId",
                schema: "auth",
                table: "group_members");

            migrationBuilder.DropForeignKey(
                name: "FK_groups_app_users_CreatedByUserId",
                schema: "auth",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "FK_groups_app_users_UpdatedByUserId",
                schema: "auth",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "FK_parties_app_users_CreatedByUserId",
                schema: "documents",
                table: "parties");

            migrationBuilder.DropForeignKey(
                name: "FK_parties_app_users_UpdatedByUserId",
                schema: "documents",
                table: "parties");

            migrationBuilder.DropForeignKey(
                name: "FK_tags_app_users_CreatedByUserId",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropForeignKey(
                name: "FK_tags_app_users_UpdatedByUserId",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropForeignKey(
                name: "FK_text_extraction_engines_app_users_CreatedByUserId",
                schema: "settings",
                table: "text_extraction_engines");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "identity");

            migrationBuilder.DropPrimaryKey(
                name: "PK_app_users",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "AccessFailedCount",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "EmailConfirmed",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "LockoutEnabled",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "LockoutEnd",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "NormalizedUserName",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "PhoneNumberConfirmed",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnabled",
                schema: "identity",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "UserName",
                schema: "identity",
                table: "app_users");

            migrationBuilder.RenameTable(
                name: "app_users",
                schema: "identity",
                newName: "users",
                newSchema: "public");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            // Copy the preflight-computed role values into the real column, then drop the temp column.
            migrationBuilder.Sql(@"
                UPDATE users SET ""Role"" = ""ComputedRole"";
                ALTER TABLE users DROP COLUMN ""ComputedRole"";
            ");

            migrationBuilder.AddPrimaryKey(
                name: "PK_users",
                table: "users",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_document_comments_users_CreatedByUserId",
                schema: "documents",
                table: "document_comments",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_users_CreatedByUserId",
                schema: "documents",
                table: "documents",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_users_DeletedByUserId",
                schema: "documents",
                table: "documents",
                column: "DeletedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_users_OwnerUserId",
                schema: "documents",
                table: "documents",
                column: "OwnerUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_users_UpdatedByUserId",
                schema: "documents",
                table: "documents",
                column: "UpdatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_group_members_users_UserId",
                schema: "auth",
                table: "group_members",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_groups_users_CreatedByUserId",
                schema: "auth",
                table: "groups",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_groups_users_UpdatedByUserId",
                schema: "auth",
                table: "groups",
                column: "UpdatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_parties_users_CreatedByUserId",
                schema: "documents",
                table: "parties",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_parties_users_UpdatedByUserId",
                schema: "documents",
                table: "parties",
                column: "UpdatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_tags_users_CreatedByUserId",
                schema: "documents",
                table: "tags",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_tags_users_UpdatedByUserId",
                schema: "documents",
                table: "tags",
                column: "UpdatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_text_extraction_engines_users_CreatedByUserId",
                schema: "settings",
                table: "text_extraction_engines",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // All identity.* tables have been dropped; the schema is now empty.
            migrationBuilder.Sql(@"DROP SCHEMA identity;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "DropAspNetIdentity is irreversible: role/login/claim rows are discarded, not archived.");
#pragma warning disable CS0162 // unreachable on purpose — kept for documentation of the original shape.
            migrationBuilder.DropForeignKey(
                name: "FK_document_comments_users_CreatedByUserId",
                schema: "documents",
                table: "document_comments");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_users_CreatedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_users_DeletedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_users_OwnerUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_documents_users_UpdatedByUserId",
                schema: "documents",
                table: "documents");

            migrationBuilder.DropForeignKey(
                name: "FK_group_members_users_UserId",
                schema: "auth",
                table: "group_members");

            migrationBuilder.DropForeignKey(
                name: "FK_groups_users_CreatedByUserId",
                schema: "auth",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "FK_groups_users_UpdatedByUserId",
                schema: "auth",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "FK_parties_users_CreatedByUserId",
                schema: "documents",
                table: "parties");

            migrationBuilder.DropForeignKey(
                name: "FK_parties_users_UpdatedByUserId",
                schema: "documents",
                table: "parties");

            migrationBuilder.DropForeignKey(
                name: "FK_tags_users_CreatedByUserId",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropForeignKey(
                name: "FK_tags_users_UpdatedByUserId",
                schema: "documents",
                table: "tags");

            migrationBuilder.DropForeignKey(
                name: "FK_text_extraction_engines_users_CreatedByUserId",
                schema: "settings",
                table: "text_extraction_engines");

            migrationBuilder.DropPrimaryKey(
                name: "PK_users",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_Email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "users");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.RenameTable(
                name: "users",
                newName: "app_users",
                newSchema: "identity");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "identity",
                table: "app_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                schema: "identity",
                table: "app_users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                schema: "identity",
                table: "app_users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmed",
                schema: "identity",
                table: "app_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LockoutEnabled",
                schema: "identity",
                table: "app_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutEnd",
                schema: "identity",
                table: "app_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                schema: "identity",
                table: "app_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUserName",
                schema: "identity",
                table: "app_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                schema: "identity",
                table: "app_users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                schema: "identity",
                table: "app_users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhoneNumberConfirmed",
                schema: "identity",
                table: "app_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                schema: "identity",
                table: "app_users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                schema: "identity",
                table: "app_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                schema: "identity",
                table: "app_users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_app_users",
                schema: "identity",
                table: "app_users",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_claims_app_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "identity",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_logins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_user_logins_app_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_user_tokens_app_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_claims_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false, defaultValue: "Local")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_user_roles_app_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "roles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("2dfd3ea9-d87c-4772-ac15-58d639c67fe1"), "e5a3a534-6cb0-4e8f-b7a1-3f2c6e9d04a7", "Guest", "GUEST" },
                    { new Guid("36dd9cfb-c763-4e7a-b6ad-fb2894387100"), "b026694e-34ae-4a7e-b94e-1b0566351474", "Admin", "ADMIN" },
                    { new Guid("470b20ff-ebd9-4704-9412-5d4f7268e0b4"), "a1e03693-1592-4c68-a12c-51e4e6e1bf6e", "Contributor", "CONTRIBUTOR" },
                    { new Guid("6cb8bf9d-1af1-442a-b077-d337e83dcae3"), "dc457fa2-09f0-4c65-a7b5-963327cb41a4", "Viewer", "VIEWER" }
                });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity",
                table: "app_users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identity",
                table: "app_users",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_claims_RoleId",
                schema: "identity",
                table: "role_claims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identity",
                table: "roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_claims_UserId",
                schema: "identity",
                table: "user_claims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_logins_UserId",
                schema: "identity",
                table: "user_logins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_RoleId",
                schema: "identity",
                table: "user_roles",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_document_comments_app_users_CreatedByUserId",
                schema: "documents",
                table: "document_comments",
                column: "CreatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_app_users_CreatedByUserId",
                schema: "documents",
                table: "documents",
                column: "CreatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_app_users_DeletedByUserId",
                schema: "documents",
                table: "documents",
                column: "DeletedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_app_users_OwnerUserId",
                schema: "documents",
                table: "documents",
                column: "OwnerUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_documents_app_users_UpdatedByUserId",
                schema: "documents",
                table: "documents",
                column: "UpdatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_group_members_app_users_UserId",
                schema: "auth",
                table: "group_members",
                column: "UserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_groups_app_users_CreatedByUserId",
                schema: "auth",
                table: "groups",
                column: "CreatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_groups_app_users_UpdatedByUserId",
                schema: "auth",
                table: "groups",
                column: "UpdatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_parties_app_users_CreatedByUserId",
                schema: "documents",
                table: "parties",
                column: "CreatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_parties_app_users_UpdatedByUserId",
                schema: "documents",
                table: "parties",
                column: "UpdatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_tags_app_users_CreatedByUserId",
                schema: "documents",
                table: "tags",
                column: "CreatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_tags_app_users_UpdatedByUserId",
                schema: "documents",
                table: "tags",
                column: "UpdatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_text_extraction_engines_app_users_CreatedByUserId",
                schema: "settings",
                table: "text_extraction_engines",
                column: "CreatedByUserId",
                principalSchema: "identity",
                principalTable: "app_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
#pragma warning restore CS0162
        }
    }
}
