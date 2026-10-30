using CertCoach.Domain.Certifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CertCoach.Infrastructure.Persistence.Configurations;

internal sealed class CertificationConfiguration : IEntityTypeConfiguration<Certification>
{
    public void Configure(EntityTypeBuilder<Certification> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.ExamCode).HasMaxLength(32);
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.Vendor).HasMaxLength(100);
        builder.Property(c => c.ReplacedByExamCode).HasMaxLength(32);
        builder.Property(c => c.OutlineUrl).HasMaxLength(2048);
        builder.HasIndex(c => new { c.OwnerId, c.ExamCode }).IsUnique();
        builder.Ignore(c => c.ObjectiveCount);

        builder.HasMany(c => c.Domains)
            .WithOne()
            .HasForeignKey(d => d.CertificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Domains).HasField("_domains").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ExamDomainConfiguration : IEntityTypeConfiguration<ExamDomain>
{
    public void Configure(EntityTypeBuilder<ExamDomain> builder)
    {
        builder.ToTable("exam_domains");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Name).HasMaxLength(300);
        builder.Ignore(d => d.Weight);
        builder.Ignore(d => d.Objectives);
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_exam_domains_weight_range",
            "weight_min_percent >= 0 AND weight_max_percent <= 100 AND weight_min_percent <= weight_max_percent"));

        builder.HasMany(d => d.SkillAreas)
            .WithOne()
            .HasForeignKey(s => s.DomainId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(d => d.SkillAreas).HasField("_skillAreas").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SkillAreaConfiguration : IEntityTypeConfiguration<SkillArea>
{
    public void Configure(EntityTypeBuilder<SkillArea> builder)
    {
        builder.ToTable("skill_areas");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(300);

        builder.HasMany(s => s.Objectives)
            .WithOne()
            .HasForeignKey(o => o.SkillAreaId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Objectives).HasField("_objectives").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ObjectiveConfiguration : IEntityTypeConfiguration<Objective>
{
    public void Configure(EntityTypeBuilder<Objective> builder)
    {
        builder.ToTable("objectives");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Text).HasMaxLength(1000);
    }
}
