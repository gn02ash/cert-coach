using CertCoach.Domain.Certifications;
using CertCoach.Domain.Documents;
using CertCoach.Domain.Planning;
using CertCoach.Domain.Practice;
using CertCoach.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

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

    DbSet<Document> Documents { get; }

    DbSet<DocumentChunk> DocumentChunks { get; }

    DbSet<PracticeItem> PracticeItems { get; }

    DbSet<GenerationJob> GenerationJobs { get; }

    DbSet<ReviewLog> ReviewLogs { get; }

    /// <summary>For transactions and set-based updates that span more than one save.</summary>
    DatabaseFacade Database { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
