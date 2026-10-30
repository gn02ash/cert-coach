using CertCoach.Application.Abstractions;
using CertCoach.Domain.Certifications;
using CertCoach.Domain.Planning;
using CertCoach.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Infrastructure.Persistence;

/// <param name="options">EF Core options.</param>
/// <param name="currentUser">
/// When present, owner-scoped entities are filtered to this user as a second line of defense
/// behind the explicit checks in the application services. Tooling and tests pass none.
/// </param>
public sealed class CertCoachDbContext(DbContextOptions<CertCoachDbContext> options, ICurrentUser? currentUser = null)
    : DbContext(options), ICertCoachDbContext
{
    public DbSet<Certification> Certifications => Set<Certification>();

    public DbSet<StudyPlan> StudyPlans => Set<StudyPlan>();

    public DbSet<User> Users => Set<User>();

    private Guid? OwnerFilter => currentUser?.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Enabled from the first migration so Phase 2 embeddings need no extension change.
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CertCoachDbContext).Assembly);

        modelBuilder.Entity<Certification>().HasQueryFilter(c => OwnerFilter == null || c.OwnerId == OwnerFilter);
        modelBuilder.Entity<StudyPlan>().HasQueryFilter(p => OwnerFilter == null || p.OwnerId == OwnerFilter);
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
