using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forensic.Infrastructure.Identidad.Migraciones
{
    /// <summary>
    /// Encadena la auditoría de accesos por hash (forensic-video/accesos/v1, el formato de
    /// <c>CadenaDeAccesos</c> en el dominio). El encadenado lo hace un trigger BEFORE INSERT, así ninguna
    /// escritura (ni un INSERT a mano) se lo saltea; la aplicación lo verifica con su propia implementación.
    /// Los eventos que ya existían se encadenan acá, en el orden en que se insertaron.
    /// </summary>
    public partial class CadenaDeAccesos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Secuencia", schema: "identidad", table: "AuditoriaDeAccesos", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "HashAnterior", schema: "identidad", table: "AuditoriaDeAccesos", type: "character(64)",
                fixedLength: true, maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "Hash", schema: "identidad", table: "AuditoriaDeAccesos", type: "character(64)",
                fixedLength: true, maxLength: 64, nullable: true);

            // A mano: el formato canónico, idéntico al de CadenaDeAccesos.CalcularHash. Cada campo es
            // «nombre:longitud en bytes UTF-8:valor\n»; un nulo, «nombre:-1:\n».
            migrationBuilder.Sql("""
                CREATE FUNCTION identidad.campo_canonico(nombre text, valor text) RETURNS text
                    LANGUAGE sql IMMUTABLE AS $$
                    SELECT nombre || ':' ||
                        CASE WHEN valor IS NULL THEN '-1:' ELSE octet_length(convert_to(valor, 'UTF8'))::text || ':' || valor END ||
                        E'\n'
                    $$;

                CREATE FUNCTION identidad.hash_de_acceso(
                    secuencia bigint, tipo text, usuario text, actor text, detalle text, origen text, instante timestamptz,
                    anterior text) RETURNS character(64)
                    LANGUAGE sql IMMUTABLE AS $$
                    SELECT encode(sha256(convert_to(
                        identidad.campo_canonico('formato', 'forensic-video/accesos/v1') ||
                        identidad.campo_canonico('secuencia', secuencia::text) ||
                        identidad.campo_canonico('tipo', tipo) ||
                        identidad.campo_canonico('usuario', usuario) ||
                        identidad.campo_canonico('actor', actor) ||
                        identidad.campo_canonico('detalle', detalle) ||
                        identidad.campo_canonico('origen', origen) ||
                        identidad.campo_canonico('timestamp', to_char(instante AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')) ||
                        identidad.campo_canonico('anterior', anterior),
                        'UTF8')), 'hex')
                    $$;

                -- Los eventos que ya estaban, encadenados en el orden en que se insertaron.
                ALTER TABLE identidad."AuditoriaDeAccesos" DISABLE TRIGGER "AuditoriaDeAccesos_append_only";
                DO $$
                DECLARE
                    r record;
                    secuencia bigint := 0;
                    anterior text := repeat('0', 64);
                    actual text;
                BEGIN
                    FOR r IN SELECT * FROM identidad."AuditoriaDeAccesos" ORDER BY "Id" LOOP
                        secuencia := secuencia + 1;
                        actual := identidad.hash_de_acceso(
                            secuencia, r."Tipo", r."Usuario", r."Actor", r."Detalle", r."Origen", r."Timestamp", anterior);
                        UPDATE identidad."AuditoriaDeAccesos"
                            SET "Secuencia" = secuencia, "HashAnterior" = anterior, "Hash" = actual
                            WHERE "Id" = r."Id";
                        anterior := actual;
                    END LOOP;
                END
                $$;
                ALTER TABLE identidad."AuditoriaDeAccesos" ENABLE TRIGGER "AuditoriaDeAccesos_append_only";

                -- Cada insert toma un lock de la cadena hasta el commit: dos logins concurrentes no pueden
                -- leer la misma cabeza. En PL/pgSQL cada consulta toma su propia foto, así que la cabeza que
                -- se lee después del lock incluye lo que commiteó quien lo tenía.
                CREATE FUNCTION identidad.encadenar_acceso() RETURNS trigger
                    LANGUAGE plpgsql AS $$
                DECLARE
                    ultima record;
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtext('identidad.AuditoriaDeAccesos'));
                    SELECT "Secuencia", "Hash" INTO ultima
                        FROM identidad."AuditoriaDeAccesos" ORDER BY "Secuencia" DESC LIMIT 1;
                    NEW."Secuencia" := COALESCE(ultima."Secuencia", 0) + 1;
                    NEW."HashAnterior" := COALESCE(ultima."Hash", repeat('0', 64));
                    NEW."Hash" := identidad.hash_de_acceso(
                        NEW."Secuencia", NEW."Tipo", NEW."Usuario", NEW."Actor", NEW."Detalle", NEW."Origen", NEW."Timestamp",
                        NEW."HashAnterior");
                    RETURN NEW;
                END
                $$;

                CREATE TRIGGER "AuditoriaDeAccesos_encadenar"
                    BEFORE INSERT ON identidad."AuditoriaDeAccesos"
                    FOR EACH ROW EXECUTE FUNCTION identidad.encadenar_acceso();
                """);

            migrationBuilder.AlterColumn<long>(
                name: "Secuencia", schema: "identidad", table: "AuditoriaDeAccesos", type: "bigint", nullable: false,
                oldClrType: typeof(long), oldType: "bigint", oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "HashAnterior", schema: "identidad", table: "AuditoriaDeAccesos", type: "character(64)",
                fixedLength: true, maxLength: 64, nullable: false, oldClrType: typeof(string), oldType: "character(64)",
                oldFixedLength: true, oldMaxLength: 64, oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "Hash", schema: "identidad", table: "AuditoriaDeAccesos", type: "character(64)",
                fixedLength: true, maxLength: 64, nullable: false, oldClrType: typeof(string), oldType: "character(64)",
                oldFixedLength: true, oldMaxLength: 64, oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditoriaDeAccesos_Secuencia",
                schema: "identidad",
                table: "AuditoriaDeAccesos",
                column: "Secuencia",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "AuditoriaDeAccesos_encadenar" ON identidad."AuditoriaDeAccesos";
                DROP FUNCTION identidad.encadenar_acceso();
                DROP FUNCTION identidad.hash_de_acceso(bigint, text, text, text, text, text, timestamptz, text);
                DROP FUNCTION identidad.campo_canonico(text, text);
                """);

            migrationBuilder.DropIndex(
                name: "IX_AuditoriaDeAccesos_Secuencia",
                schema: "identidad",
                table: "AuditoriaDeAccesos");

            migrationBuilder.DropColumn(name: "Hash", schema: "identidad", table: "AuditoriaDeAccesos");
            migrationBuilder.DropColumn(name: "HashAnterior", schema: "identidad", table: "AuditoriaDeAccesos");
            migrationBuilder.DropColumn(name: "Secuencia", schema: "identidad", table: "AuditoriaDeAccesos");
        }
    }
}
