using CertCoach.Domain.Certifications;
using CertCoach.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CertCoach.Infrastructure.Persistence.Configurations;

internal sealed class StudyPlanConfiguration : IEntityTypeConfiguration<StudyPlan>
{
    public void Configure(EntityTypeBuilder<StudyPlan> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.HoursPerWeek).HasPrecision(4, 1);
        builder.Ignore(p => p.Settings);
        builder.HasIndex(p => new { p.OwnerId, p.Status });

        builder.HasOne<Certification>()
            .WithMany()
            .HasForeignKey(p => p.CertificationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ComplexProperty(p => p.Shortfall, shortfall =>
        {
            shortfall.Property(s => s.SuggestedHoursPerWeek).HasPrecision(4, 1);
        });

        builder.HasMany(p => p.Sessions)
            .WithOne()
            .HasForeignKey(s => s.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Sessions).HasField("_sessions").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class StudySessionConfiguration : IEntityTypeConfiguration<StudySession>
{
    public void Configure(EntityTypeBuilder<StudySession> builder)
    {
        builder.ToTable("study_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => new { s.PlanId, s.ScheduledDate });

        // Stored as a uuid[] column: objective IDs are only read together with their session,
        // and a plan is regenerated, not patched, when its certification outline changes.
        builder.PrimitiveCollection(s => s.ObjectiveIds);
    }
}
