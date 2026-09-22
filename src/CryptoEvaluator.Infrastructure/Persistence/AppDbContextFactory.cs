using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CryptoEvaluator.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by EF Core CLI / Package Manager Console tools
/// (add-migration, update-database, etc.).
/// It is never used at runtime — the real DbContext is registered via
/// DependencyInjection.cs with the connection string from IConfiguration.
///
/// Connection string resolution order (design-time only):
///   1. DESIGN_TIME_CONNECTION_STRING environment variable
///   2. ConnectionStrings__DefaultConnection environment variable
///   3. Hard-coded local-dev default
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string LocalDevDefault =
        "Host=localhost;Database=crypto_evaluator;Username=postgres;Password=thaihoan";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DESIGN_TIME_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? LocalDevDefault;

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
