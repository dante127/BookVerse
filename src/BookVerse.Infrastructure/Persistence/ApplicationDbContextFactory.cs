using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookVerse.Infrastructure.Persistence;

/// <summary>
/// Used by `dotnet ef` at design time so schema tooling does not boot the API host
/// (which validates secrets and touches the database at startup).
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost,1433;Database=BookVerseDb;User Id=sa;Password=design-time-only;TrustServerCertificate=True;",
            sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
