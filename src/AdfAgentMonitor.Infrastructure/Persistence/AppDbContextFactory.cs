using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdfAgentMonitor.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used exclusively by EF Core tooling (dotnet ef migrations add / update).
/// Not registered in the application DI container.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Prefer an explicit env var so CI can inject a real connection string.
        // Falls back to LocalDB for local developer use.
        var connectionString =
            Environment.GetEnvironmentVariable("ADFMONITOR_CONNECTIONSTRING")
            ?? "Server=(localdb)\\mssqllocaldb;Database=AdfAgentMonitor;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
