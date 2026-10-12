using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BusinessCentralBlazor.Infrastructure.Persistence;

public sealed class ErpDbContextFactory : IDesignTimeDbContextFactory<ErpDbContext>
{
    public ErpDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ErpDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__ErpDatabase to a SQL Server connection string before using EF Core tools.");
        }

        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ErpDbContext(options);
    }
}
