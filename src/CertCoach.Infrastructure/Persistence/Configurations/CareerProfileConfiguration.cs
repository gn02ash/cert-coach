using CertCoach.Domain.Careers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CertCoach.Infrastructure.Persistence.Configurations;

internal sealed class CareerProfileConfiguration : IEntityTypeConfiguration<CareerProfile>
{
    public void Configure(EntityTypeBuilder<CareerProfile> builder)
    {
        builder.ToTable("career_profiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.TargetRole).HasMaxLength(CareerProfile.MaxEntryLength);
        builder.PrimitiveCollection(p => p.Skills).HasField("_skills").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.PrimitiveCollection(p => p.Roles).HasField("_roles").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.PrimitiveCollection(p => p.HeldCertifications).HasField("_heldCertifications").UsePropertyAccessMode(PropertyAccessMode.Field);

        // One profile per learner.
        builder.HasIndex(p => p.OwnerId).IsUnique();
    }
}
