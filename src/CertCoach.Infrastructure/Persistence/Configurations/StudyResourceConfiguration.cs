using CertCoach.Domain.Certifications;
using CertCoach.Domain.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CertCoach.Infrastructure.Persistence.Configurations;

internal sealed class StudyResourceConfiguration : IEntityTypeConfiguration<StudyResource>
{
    public void Configure(EntityTypeBuilder<StudyResource> builder)
    {
        builder.ToTable("study_resources");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Title).HasMaxLength(StudyResource.MaxTitleLength);
        builder.Property(r => r.Url).HasMaxLength(StudyResource.MaxUrlLength);
        builder.Property(r => r.Source).HasMaxLength(StudyResource.MaxSourceLength);
        builder.Property(r => r.Summary).HasMaxLength(StudyResource.MaxSummaryLength);
        builder.HasIndex(r => new { r.CertificationId, r.ObjectiveId });

        builder.HasOne<Certification>()
            .WithMany()
            .HasForeignKey(r => r.CertificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Objective>()
            .WithMany()
            .HasForeignKey(r => r.ObjectiveId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
