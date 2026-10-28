using Forensic.Application.Identidad;
using Forensic.Domain.Comun;
using Forensic.Domain.Identidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Forensic.Infrastructure.Identidad;

/// <summary>
/// Una fila del log de auditoría de accesos. Append-only (triggers en la migración Identidad) y
/// encadenada por hash: Secuencia, HashAnterior y Hash los calcula la base al insertar (migración
/// CadenaDeAccesos), así ninguna escritura puede saltearse el encadenado.
/// </summary>
internal sealed class RegistroDeAcceso
{
    public long Id { get; set; }
    public TipoDeEventoDeAcceso Tipo { get; set; }
    public string Usuario { get; set; } = null!;
    public string Actor { get; set; } = null!;
    public string? Detalle { get; set; }
    public string? Origen { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public long Secuencia { get; set; }
    public string HashAnterior { get; set; } = null!;
    public string Hash { get; set; } = null!;
}

/// <summary>
/// Bounded context de identidad, en su propio schema y con sus propias migraciones: nada de lo que
/// pasa acá toca la cadena de custodia, y viceversa.
/// </summary>
internal sealed class ContextoDeIdentidad(DbContextOptions<ContextoDeIdentidad> opciones) : DbContext(opciones)
{
    public const string Schema = "identidad";

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RegistroDeAcceso> AuditoriaDeAccesos => Set<RegistroDeAcceso>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Usuario>(b =>
        {
            b.ToTable("Usuarios");
            b.HasKey(u => u.Id);
            b.Property(u => u.Nombre).HasMaxLength(100).IsRequired();
            b.HasIndex(u => u.Nombre).IsUnique();
            b.Property(u => u.Rol).HasConversion<string>().HasMaxLength(20);
            b.Property(u => u.HashDeContrasena)
                .HasConversion(h => h.Phc, phc => HashDeContrasena.Parse(phc))
                .HasMaxLength(512)
                .IsRequired();
            b.Property(u => u.CreadoPor).HasMaxLength(100).IsRequired();
            // Los intentos de login concurrentes sobre el mismo usuario no se pisan el contador.
            b.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        });

        modelBuilder.Entity<RegistroDeAcceso>(b =>
        {
            b.ToTable("AuditoriaDeAccesos");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).UseIdentityAlwaysColumn();
            b.Property(r => r.Tipo).HasConversion<string>().HasMaxLength(40);
            b.Property(r => r.Usuario).HasMaxLength(100).IsRequired();
            b.Property(r => r.Actor).HasMaxLength(100).IsRequired();
            b.Property(r => r.Detalle).HasMaxLength(500);
            b.Property(r => r.Origen).HasMaxLength(64);
            b.HasIndex(r => new { r.Usuario, r.Timestamp });
            // Los pone el trigger de encadenado: EF no los manda y los lee del RETURNING.
            b.Property(r => r.Secuencia).ValueGeneratedOnAdd()
                .Metadata.SetValueGenerationStrategy(Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.None);
            b.Property(r => r.HashAnterior).HasMaxLength(HashSha256.LongitudHex).IsFixedLength().ValueGeneratedOnAdd();
            b.Property(r => r.Hash).HasMaxLength(HashSha256.LongitudHex).IsFixedLength().ValueGeneratedOnAdd();
            b.HasIndex(r => r.Secuencia).IsUnique();
        });
    }
}

/// <summary>Sólo para <c>dotnet ef migrations</c>: genera el modelo sin conectarse a ninguna base.</summary>
internal sealed class FabricaDeDisenoDeIdentidad : IDesignTimeDbContextFactory<ContextoDeIdentidad>
{
    public ContextoDeIdentidad CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ContextoDeIdentidad>()
            .UseNpgsql("Host=localhost", o => o.MigrationsHistoryTable("__EFMigrationsHistory", ContextoDeIdentidad.Schema))
            .Options);
}
