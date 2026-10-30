using CertCoach.Domain.Certifications;
using CertCoach.Domain.Planning;
using CertCoach.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CertCoach.Application.Abstractions;

/// <summary>
/// The persistence port used by application services. EF Core's DbSet already is a repository and
/// unit of work, so the application layer depends on it directly instead of wrapping it again.
/// </summary>
public interface ICertCoachDbContext
{
    DbSet<Certification> Certifications { get; }

    DbSet<StudyPlan> StudyPlans { get; }

    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
