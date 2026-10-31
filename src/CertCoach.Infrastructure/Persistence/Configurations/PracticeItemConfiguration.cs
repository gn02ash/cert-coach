using CertCoach.Domain.Certifications;
using CertCoach.Domain.Practice;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CertCoach.Infrastructure.Persistence.Configurations;

internal sealed class PracticeItemConfiguration : IEntityTypeConfiguration<PracticeItem>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<PracticeItem> builder)
    {
        builder.ToTable("practice_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Prompt).HasMaxLength(PracticeItem.MaxPromptLength);
        builder.Property(i => i.Answer).HasMaxLength(PracticeItem.MaxAnswerLength);
        builder.Property(i => i.Fingerprint).HasMaxLength(200);
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Origin).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(i => i.IsNew);

        // Read and written with the item, never queried on their own.
        builder.PrimitiveCollection(i => i.Options).HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.PrimitiveCollection(i => i.CorrectOptions).HasField("_correctOptions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(i => i.Sources)
            .HasField("_sources")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<ItemSource>>(v, Json) ?? new List<ItemSource>(),
                new ValueComparer<IReadOnlyList<ItemSource>>(
                    (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.SequenceEqual(b)),
                    v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s)),
                    v => v.ToList()));

        // The review queue: an owner's due items, and the items per objective for generation and readiness.
        builder.HasIndex(i => new { i.OwnerId, i.Status, i.DueOn });
        builder.HasIndex(i => new { i.CertificationId, i.ObjectiveId });

        builder.HasOne<Certification>()
            .WithMany()
            .HasForeignKey(i => i.CertificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Objective>()
            .WithMany()
            .HasForeignKey(i => i.ObjectiveId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class GenerationJobConfiguration : IEntityTypeConfiguration<GenerationJob>
{
    public void Configure(EntityTypeBuilder<GenerationJob> builder)
    {
        builder.ToTable("generation_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.Error).HasMaxLength(500);
        builder.PrimitiveCollection(j => j.ObjectiveIds).HasField("_objectiveIds").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(j => j.IsActive);
        builder.Ignore(j => j.NextObjective);

        // The worker's claim query, and the "one run at a time per certification" check.
        builder.HasIndex(j => new { j.Status, j.CreatedAt });
        builder.HasIndex(j => new { j.CertificationId, j.Status });

        builder.HasOne<Certification>()
            .WithMany()
            .HasForeignKey(j => j.CertificationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
