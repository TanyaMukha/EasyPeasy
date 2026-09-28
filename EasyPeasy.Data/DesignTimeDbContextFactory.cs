
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EasyPeasy.Data;

/// <summary>
/// Factory used by the EF Core CLI tools (<c>dotnet ef migrations add</c>, <c>dotnet ef database update</c>,
/// etc.) to construct an <see cref="EasyPeasyDbContext"/> at design time, when the app's own DI
/// container (which supplies runtime connection strings) isn't running. Not used at app runtime —
/// the app builds its own <see cref="DbContextOptions{TContext}"/> separately (see
/// <c>EasyPeasy.App</c>'s startup wiring).
/// </summary>
public class EasyPeasyDbContextFactory : IDesignTimeDbContextFactory<EasyPeasyDbContext>
{
    /// <summary>
    /// Builds a context pointed at a fixed local SQLite file, relative to the <c>EasyPeasy.Data</c>
    /// project folder (where <c>dotnet ef</c> commands are run from) — not the app's real, per-device
    /// database path. Only used to generate/apply migrations during development.
    /// </summary>
    public EasyPeasyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EasyPeasyDbContext>();

        optionsBuilder.UseSqlite("Data Source=../mukhalab.easypeasy.db");

        return new EasyPeasyDbContext(optionsBuilder.Options);
    }
}