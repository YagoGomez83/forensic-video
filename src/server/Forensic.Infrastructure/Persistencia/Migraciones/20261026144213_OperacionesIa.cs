using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forensic.Infrastructure.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class OperacionesIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModeloId",
                table: "OperacionesForenses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModeloSha256",
                table: "OperacionesForenses",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModeloVersion",
                table: "OperacionesForenses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // A mano: una operación de IA es «sólo investigación» y lleva su modelo completo; una
            // determinista no lleva modelo. Ni escribiendo a mano una operación de IA figura admisible.
            migrationBuilder.Sql("""
                ALTER TABLE "OperacionesForenses" ADD CONSTRAINT "CK_OperacionesForenses_ModeloSegunVia" CHECK (
                    ("Via" = 'SoloInvestigacion'
                        AND "ModeloId" IS NOT NULL AND "ModeloVersion" IS NOT NULL AND "ModeloSha256" IS NOT NULL)
                    OR ("Via" = 'AdmisibleEnJuicio'
                        AND "ModeloId" IS NULL AND "ModeloVersion" IS NULL AND "ModeloSha256" IS NULL));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "OperacionesForenses" DROP CONSTRAINT "CK_OperacionesForenses_ModeloSegunVia";""");
            migrationBuilder.DropColumn(
                name: "ModeloId",
                table: "OperacionesForenses");

            migrationBuilder.DropColumn(
                name: "ModeloSha256",
                table: "OperacionesForenses");

            migrationBuilder.DropColumn(
                name: "ModeloVersion",
                table: "OperacionesForenses");
        }
    }
}
