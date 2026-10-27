using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forensic.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class AnalisisIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalisisIa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    HashEntrada = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Parametros = table.Column<string>(type: "text", nullable: false),
                    Entorno = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RegistradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModeloId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModeloSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ModeloVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalisisIa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalisisIa_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnalisisIa_Evidencias_EvidenciaId",
                        column: x => x.EvidenciaId,
                        principalTable: "Evidencias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HallazgosIa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnalisisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false),
                    Clase = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TextoLeido = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Confianza = table.Column<decimal>(type: "numeric", nullable: false),
                    DatoCrudo = table.Column<string>(type: "text", nullable: false),
                    RegionAlto = table.Column<int>(type: "integer", nullable: false),
                    RegionAncho = table.Column<int>(type: "integer", nullable: false),
                    RegionX = table.Column<int>(type: "integer", nullable: false),
                    RegionY = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HallazgosIa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HallazgosIa_AnalisisIa_AnalisisId",
                        column: x => x.AnalisisId,
                        principalTable: "AnalisisIa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalisisIa_CasoId_EvidenciaId",
                table: "AnalisisIa",
                columns: new[] { "CasoId", "EvidenciaId" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalisisIa_EvidenciaId",
                table: "AnalisisIa",
                column: "EvidenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_HallazgosIa_AnalisisId_Orden",
                table: "HallazgosIa",
                columns: new[] { "AnalisisId", "Orden" },
                unique: true);

            // A mano. La vía no está en el modelo (es una constante del dominio): existe en la base para
            // quien consulte por SQL, y ni escribiendo a mano puede figurar algo de IA como admisible.
            // Los análisis y sus hallazgos son inmutables, como la evidencia y la cadena.
            migrationBuilder.Sql("""
                ALTER TABLE "AnalisisIa"
                    ADD COLUMN "Via" character varying(30) NOT NULL DEFAULT 'SoloInvestigacion'
                    CONSTRAINT "CK_AnalisisIa_SoloInvestigacion" CHECK ("Via" = 'SoloInvestigacion');
                ALTER TABLE "HallazgosIa"
                    ADD COLUMN "Via" character varying(30) NOT NULL DEFAULT 'SoloInvestigacion'
                    CONSTRAINT "CK_HallazgosIa_SoloInvestigacion" CHECK ("Via" = 'SoloInvestigacion'),
                    ADD CONSTRAINT "CK_HallazgosIa_Confianza" CHECK ("Confianza" >= 0 AND "Confianza" <= 1),
                    ADD CONSTRAINT "CK_HallazgosIa_ClaseOTexto" CHECK (("Clase" IS NULL) <> ("TextoLeido" IS NULL));

                CREATE TRIGGER "AnalisisIa_inmutables"
                    BEFORE UPDATE OR DELETE ON "AnalisisIa"
                    FOR EACH ROW EXECUTE FUNCTION rechazar_modificacion_forense();
                CREATE TRIGGER "AnalisisIa_sin_truncate"
                    BEFORE TRUNCATE ON "AnalisisIa"
                    FOR EACH STATEMENT EXECUTE FUNCTION rechazar_modificacion_forense();
                CREATE TRIGGER "HallazgosIa_inmutables"
                    BEFORE UPDATE OR DELETE ON "HallazgosIa"
                    FOR EACH ROW EXECUTE FUNCTION rechazar_modificacion_forense();
                CREATE TRIGGER "HallazgosIa_sin_truncate"
                    BEFORE TRUNCATE ON "HallazgosIa"
                    FOR EACH STATEMENT EXECUTE FUNCTION rechazar_modificacion_forense();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "HallazgosIa_sin_truncate" ON "HallazgosIa";
                DROP TRIGGER "HallazgosIa_inmutables" ON "HallazgosIa";
                DROP TRIGGER "AnalisisIa_sin_truncate" ON "AnalisisIa";
                DROP TRIGGER "AnalisisIa_inmutables" ON "AnalisisIa";
                """);
            migrationBuilder.DropTable(
                name: "HallazgosIa");

            migrationBuilder.DropTable(
                name: "AnalisisIa");
        }
    }
}
