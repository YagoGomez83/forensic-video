using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ForensicVideo.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Casos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroExpediente = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AbiertoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AbiertoPor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AnalistasAsignados = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Casos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AsientosDeCustodia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Secuencia = table.Column<long>(type: "bigint", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EvidenciaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detalle = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HashAnterior = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AsientosDeCustodia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AsientosDeCustodia_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Evidencias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false),
                    NombreOriginal = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Via = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PadreId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperacionOrigenId = table.Column<Guid>(type: "uuid", nullable: true),
                    IngestadaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngestadaPor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evidencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Evidencias_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Evidencias_Evidencias_PadreId",
                        column: x => x.PadreId,
                        principalTable: "Evidencias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AsientosDeCustodia_CasoId_Secuencia",
                table: "AsientosDeCustodia",
                columns: new[] { "CasoId", "Secuencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Casos_NumeroExpediente",
                table: "Casos",
                column: "NumeroExpediente",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Evidencias_CasoId",
                table: "Evidencias",
                column: "CasoId");

            migrationBuilder.CreateIndex(
                name: "IX_Evidencias_Hash",
                table: "Evidencias",
                column: "Hash");

            migrationBuilder.CreateIndex(
                name: "IX_Evidencias_PadreId",
                table: "Evidencias",
                column: "PadreId");

            // Custodia append-only y evidencias inmutables también a nivel de base: la aplicación
            // no puede corregir ni borrar un asiento aunque un bug lo intente. Un superusuario
            // podría desactivar los triggers; eso lo detecta la verificación de la hash-chain.
            migrationBuilder.Sql("""
                CREATE FUNCTION rechazar_modificacion_forense() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'La tabla % es append-only: % no permitido', TG_TABLE_NAME, TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER "AsientosDeCustodia_append_only"
                    BEFORE UPDATE OR DELETE ON "AsientosDeCustodia"
                    FOR EACH ROW EXECUTE FUNCTION rechazar_modificacion_forense();
                CREATE TRIGGER "AsientosDeCustodia_sin_truncate"
                    BEFORE TRUNCATE ON "AsientosDeCustodia"
                    FOR EACH STATEMENT EXECUTE FUNCTION rechazar_modificacion_forense();

                CREATE TRIGGER "Evidencias_inmutables"
                    BEFORE UPDATE OR DELETE ON "Evidencias"
                    FOR EACH ROW EXECUTE FUNCTION rechazar_modificacion_forense();
                CREATE TRIGGER "Evidencias_sin_truncate"
                    BEFORE TRUNCATE ON "Evidencias"
                    FOR EACH STATEMENT EXECUTE FUNCTION rechazar_modificacion_forense();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AsientosDeCustodia");

            migrationBuilder.DropTable(
                name: "Evidencias");

            migrationBuilder.DropTable(
                name: "Casos");

            migrationBuilder.Sql("DROP FUNCTION rechazar_modificacion_forense();");
        }
    }
}
