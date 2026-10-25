using Forensic.Domain.Casos;
using Forensic.Domain.Comun;
using Forensic.Domain.Custodia;
using Forensic.Domain.Evidencias;
using Forensic.Domain.Operaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Forensic.Infrastructure.Persistencia;

public sealed class ContextoForense(DbContextOptions<ContextoForense> options) : DbContext(options)
{
    public DbSet<Caso> Casos => Set<Caso>();
    public DbSet<Evidencia> Evidencias => Set<Evidencia>();
    public DbSet<AsientoDeCustodia> AsientosDeCustodia => Set<AsientoDeCustodia>();
    public DbSet<OperacionForense> OperacionesForenses => Set<OperacionForense>();

    private static readonly ValueConverter<HashSha256, string> ConversorHash =
        new(h => h.Hex, s => HashSha256.Parse(s));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Caso>(b =>
        {
            b.ToTable("Casos");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).ValueGeneratedNever();
            b.Property(c => c.NumeroExpediente).HasMaxLength(100).IsRequired();
            b.HasIndex(c => c.NumeroExpediente).IsUnique();
            b.Property(c => c.Descripcion).HasMaxLength(2000).IsRequired();
            b.Property(c => c.Estado).HasConversion<string>().HasMaxLength(20);
            b.Property(c => c.AbiertoPor).HasMaxLength(100).IsRequired();
            b.Property<List<string>>("_analistasAsignados")
                .HasColumnName("AnalistasAsignados")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Ignore(c => c.AnalistasAsignados);
        });

        modelBuilder.Entity<Evidencia>(b =>
        {
            b.ToTable("Evidencias");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.HasOne<Caso>().WithMany().HasForeignKey(e => e.CasoId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Evidencia>().WithMany().HasForeignKey(e => e.PadreId).OnDelete(DeleteBehavior.Restrict);
            b.Property(e => e.Hash).HasConversion(ConversorHash).HasMaxLength(HashSha256.LongitudHex).IsFixedLength();
            b.HasIndex(e => e.Hash);
            b.Property(e => e.NombreOriginal).HasMaxLength(Evidencia.LongitudMaximaNombre).IsRequired();
            b.Property(e => e.Via).HasConversion<string>().HasMaxLength(30);
            b.Property(e => e.IngestadaPor).HasMaxLength(100).IsRequired();
            b.HasOne<OperacionForense>().WithMany().HasForeignKey(e => e.OperacionOrigenId).OnDelete(DeleteBehavior.Restrict);
            b.Ignore(e => e.EsOriginal);
        });

        modelBuilder.Entity<OperacionForense>(b =>
        {
            b.ToTable("OperacionesForenses");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.HasOne<Caso>().WithMany().HasForeignKey(o => o.CasoId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Evidencia>().WithMany().HasForeignKey(o => o.EvidenciaEntradaId).OnDelete(DeleteBehavior.Restrict);
            b.Property(o => o.HashEntrada).HasConversion(ConversorHash).HasMaxLength(HashSha256.LongitudHex).IsFixedLength();
            b.Property(o => o.Tipo).HasMaxLength(100).IsRequired();
            // Texto, no jsonb: jsonb reordena claves y quita espacios, y el string exacto es lo que se reproduce.
            b.Property(o => o.Parametros).HasColumnType("text").IsRequired();
            b.Property(o => o.Entorno).HasColumnType("text").IsRequired();
            b.Property(o => o.Via).HasConversion<string>().HasMaxLength(30);
            b.Property(o => o.HashSalida).HasConversion(ConversorHash).HasMaxLength(HashSha256.LongitudHex).IsFixedLength();
            b.Property(o => o.HashPixelesSalida).HasMaxLength(HashSha256.LongitudHex);
            b.Property(o => o.Actor).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<AsientoDeCustodia>(b =>
        {
            b.ToTable("AsientosDeCustodia");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.HasOne<Caso>().WithMany().HasForeignKey(a => a.CasoId).OnDelete(DeleteBehavior.Restrict);
            // Dos asientos que compiten por la misma posición: uno falla. Así se serializa la cadena.
            b.HasIndex(a => new { a.CasoId, a.Secuencia }).IsUnique();
            b.Property(a => a.Tipo).HasConversion<string>().HasMaxLength(40);
            b.Property(a => a.Detalle).HasColumnType("text").IsRequired();
            b.Property(a => a.Actor).HasMaxLength(100).IsRequired();
            b.Property(a => a.HashAnterior).HasConversion(ConversorHash).HasMaxLength(HashSha256.LongitudHex).IsFixedLength();
            b.Property(a => a.Hash).HasConversion(ConversorHash).HasMaxLength(HashSha256.LongitudHex).IsFixedLength();
        });
    }
}
