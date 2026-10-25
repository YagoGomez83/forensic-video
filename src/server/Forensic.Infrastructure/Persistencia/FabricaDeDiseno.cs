using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Forensic.Infrastructure.Persistencia;

/// <summary>Sólo para <c>dotnet ef migrations</c>: genera el modelo sin conectarse a ninguna base.</summary>
internal sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<ContextoForense>
{
    public ContextoForense CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ContextoForense>().UseNpgsql("Host=localhost").Options);
}
