using CertCoach.Application.Abstractions;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Infrastructure.Persistence;

public sealed class CertCoachDbContext(DbContextOptions<CertCoachDbContext> options)
    : DbContext(options), ICertCoachDbContext
{
    public DbSet<Certification> Certifications => Set<Certification>();

    public DbSet<StudyPlan> StudyPlans => Set<StudyPlan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Enabled from the first migration so Phase 2 embeddings need no extension change.
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CertCoachDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored by name so the database stays readable and reordering members is safe.
        configurationBuilder.Properties<CertificationStatus>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<PlanStatus>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<SessionKind>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<SessionStatus>().HaveConversion<string>().HaveMaxLength(16);
    }
}
