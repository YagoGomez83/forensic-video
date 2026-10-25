using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Forensic.Infrastructure.Identidad.Migraciones
{
    /// <inheritdoc />
    public partial class Identidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identidad");

            migrationBuilder.CreateTable(
                name: "AuditoriaDeAccesos",
                schema: "identidad",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Usuario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Detalle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Origen = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditoriaDeAccesos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                schema: "identidad",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Rol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HashDeContrasena = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    DebeCambiarContrasena = table.Column<bool>(type: "boolean", nullable: false),
                    IntentosFallidos = table.Column<int>(type: "integer", nullable: false),
                    BloqueadoHasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreadoPor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriaDeAccesos_Usuario_Timestamp",
                schema: "identidad",
                table: "AuditoriaDeAccesos",
                columns: new[] { "Usuario", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Nombre",
                schema: "identidad",
                table: "Usuarios",
                column: "Nombre",
                unique: true);

            // A mano: la auditoría de accesos es append-only, igual que la custodia, pero con su
            // propia función para que identidad no dependa de las migraciones de custodia.
            migrationBuilder.Sql("""
                CREATE FUNCTION identidad.rechazar_modificacion_auditoria() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'La tabla % es append-only: % no permitido', TG_TABLE_NAME, TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER "AuditoriaDeAccesos_append_only"
                    BEFORE UPDATE OR DELETE ON identidad."AuditoriaDeAccesos"
                    FOR EACH ROW EXECUTE FUNCTION identidad.rechazar_modificacion_auditoria();
                CREATE TRIGGER "AuditoriaDeAccesos_sin_truncate"
                    BEFORE TRUNCATE ON identidad."AuditoriaDeAccesos"
                    FOR EACH STATEMENT EXECUTE FUNCTION identidad.rechazar_modificacion_auditoria();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "AuditoriaDeAccesos_sin_truncate" ON identidad."AuditoriaDeAccesos";
                DROP TRIGGER "AuditoriaDeAccesos_append_only" ON identidad."AuditoriaDeAccesos";
                DROP FUNCTION identidad.rechazar_modificacion_auditoria();
                """);

            migrationBuilder.DropTable(
                name: "AuditoriaDeAccesos",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "Usuarios",
                schema: "identidad");
        }
    }
}
