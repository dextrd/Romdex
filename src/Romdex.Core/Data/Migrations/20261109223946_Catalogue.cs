using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Romdex.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Catalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LibraryFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Container = table.Column<string>(type: "TEXT", nullable: false),
                    HashedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Systems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    DatVersion = table.Column<string>(type: "TEXT", nullable: false),
                    DatDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PluginInstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PluginSystemId = table.Column<string>(type: "TEXT", nullable: false),
                    BiosForId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Systems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Systems_PluginInstances_PluginInstanceId",
                        column: x => x.PluginInstanceId,
                        principalTable: "PluginInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Systems_Systems_BiosForId",
                        column: x => x.BiosForId,
                        principalTable: "Systems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LibraryContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Entry = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Crc32 = table.Column<string>(type: "TEXT", nullable: false),
                    Md5 = table.Column<string>(type: "TEXT", nullable: false),
                    Sha1 = table.Column<string>(type: "TEXT", nullable: false),
                    HeaderlessSize = table.Column<long>(type: "INTEGER", nullable: true),
                    HeaderlessCrc32 = table.Column<string>(type: "TEXT", nullable: true),
                    HeaderlessMd5 = table.Column<string>(type: "TEXT", nullable: true),
                    HeaderlessSha1 = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryContents_LibraryFiles_LibraryFileId",
                        column: x => x.LibraryFileId,
                        principalTable: "LibraryFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Titles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SystemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    IgnoredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PreferredDumpId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Titles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Titles_Systems_SystemId",
                        column: x => x.SystemId,
                        principalTable: "Systems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Dumps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SystemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TitleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CloneOf = table.Column<string>(type: "TEXT", nullable: true),
                    Serial = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    Release = table.Column<string>(type: "TEXT", nullable: false),
                    Disc = table.Column<string>(type: "TEXT", nullable: true),
                    Regions = table.Column<string>(type: "TEXT", nullable: false),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<string>(type: "TEXT", nullable: false),
                    Kinds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dumps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dumps_Systems_SystemId",
                        column: x => x.SystemId,
                        principalTable: "Systems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Dumps_Titles_TitleId",
                        column: x => x.TitleId,
                        principalTable: "Titles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DumpFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DumpId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Crc32 = table.Column<string>(type: "TEXT", nullable: true),
                    Md5 = table.Column<string>(type: "TEXT", nullable: true),
                    Sha1 = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DumpFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DumpFiles_Dumps_DumpId",
                        column: x => x.DumpId,
                        principalTable: "Dumps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryMatches",
                columns: table => new
                {
                    ContentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DumpFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Headerless = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryMatches", x => new { x.ContentId, x.DumpFileId });
                    table.ForeignKey(
                        name: "FK_LibraryMatches_DumpFiles_DumpFileId",
                        column: x => x.DumpFileId,
                        principalTable: "DumpFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryMatches_LibraryContents_ContentId",
                        column: x => x.ContentId,
                        principalTable: "LibraryContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DumpFiles_Crc32_Size",
                table: "DumpFiles",
                columns: new[] { "Crc32", "Size" });

            migrationBuilder.CreateIndex(
                name: "IX_DumpFiles_DumpId",
                table: "DumpFiles",
                column: "DumpId");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFiles_Md5",
                table: "DumpFiles",
                column: "Md5");

            migrationBuilder.CreateIndex(
                name: "IX_DumpFiles_Sha1",
                table: "DumpFiles",
                column: "Sha1");

            migrationBuilder.CreateIndex(
                name: "IX_Dumps_SystemId_Name",
                table: "Dumps",
                columns: new[] { "SystemId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dumps_TitleId",
                table: "Dumps",
                column: "TitleId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryContents_LibraryFileId",
                table: "LibraryContents",
                column: "LibraryFileId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFiles_Path",
                table: "LibraryFiles",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryMatches_DumpFileId",
                table: "LibraryMatches",
                column: "DumpFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Systems_BiosForId",
                table: "Systems",
                column: "BiosForId");

            migrationBuilder.CreateIndex(
                name: "IX_Systems_Name",
                table: "Systems",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Systems_PluginInstanceId",
                table: "Systems",
                column: "PluginInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Titles_SystemId_Key",
                table: "Titles",
                columns: new[] { "SystemId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryMatches");

            migrationBuilder.DropTable(
                name: "DumpFiles");

            migrationBuilder.DropTable(
                name: "LibraryContents");

            migrationBuilder.DropTable(
                name: "Dumps");

            migrationBuilder.DropTable(
                name: "LibraryFiles");

            migrationBuilder.DropTable(
                name: "Titles");

            migrationBuilder.DropTable(
                name: "Systems");
        }
    }
}
