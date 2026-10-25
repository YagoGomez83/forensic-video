using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forensic.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Operaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperacionesForenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CasoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenciaEntradaId = table.Column<Guid>(type: "uuid", nullable: false),
                    HashEntrada = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Entorno = table.Column<string>(type: "text", nullable: false),
                    Parametros = table.Column<string>(type: "text", nullable: false),
                    Via = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    HashSalida = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    TamanoSalidaBytes = table.Column<long>(type: "bigint", nullable: false),
                    HashPixelesSalida = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RegistradaEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacionesForenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperacionesForenses_Casos_CasoId",
                        column: x => x.CasoId,
                        principalTable: "Casos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperacionesForenses_Evidencias_EvidenciaEntradaId",
                        column: x => x.EvidenciaEntradaId,
                        principalTable: "Evidencias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Evidencias_OperacionOrigenId",
                table: "Evidencias",
                column: "OperacionOrigenId");

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesForenses_CasoId",
                table: "OperacionesForenses",
                column: "CasoId");

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesForenses_EvidenciaEntradaId",
                table: "OperacionesForenses",
                column: "EvidenciaEntradaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Evidencias_OperacionesForenses_OperacionOrigenId",
                table: "Evidencias",
                column: "OperacionOrigenId",
                principalTable: "OperacionesForenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        
            // A mano: una operación registrada es inmutable, como la evidencia y la cadena.
            migrationBuilder.Sql("""
                CREATE TRIGGER "OperacionesForenses_inmutables"
                    BEFORE UPDATE OR DELETE ON "OperacionesForenses"
                    FOR EACH ROW EXECUTE FUNCTION rechazar_modificacion_forense();
                CREATE TRIGGER "OperacionesForenses_sin_truncate"
                    BEFORE TRUNCATE ON "OperacionesForenses"
                    FOR EACH STATEMENT EXECUTE FUNCTION rechazar_modificacion_forense();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "OperacionesForenses_sin_truncate" ON "OperacionesForenses";
                DROP TRIGGER "OperacionesForenses_inmutables" ON "OperacionesForenses";
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Evidencias_OperacionesForenses_OperacionOrigenId",
                table: "Evidencias");

            migrationBuilder.DropTable(
                name: "OperacionesForenses");

            migrationBuilder.DropIndex(
                name: "IX_Evidencias_OperacionOrigenId",
                table: "Evidencias");
        }
    }
}
