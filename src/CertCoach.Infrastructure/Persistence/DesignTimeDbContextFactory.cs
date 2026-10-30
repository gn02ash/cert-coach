using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CertCoach.Infrastructure.Persistence;

/// <summary>Lets the EF Core CLI build the model without starting the API or reaching a database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CertCoachDbContext>
{
    public CertCoachDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CertCoach")
            ?? "Host=localhost;Database=certcoach;Username=certcoach;Password=design-time-only";

        var options = new DbContextOptionsBuilder<CertCoachDbContext>();
        options.UseCertCoachDatabase(connectionString);
        return new CertCoachDbContext(options.Options);
    }
}
