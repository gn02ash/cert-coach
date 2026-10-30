using CertCoach.Domain.Certifications;
using CertCoach.Domain.Planning;
using CertCoach.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CertCoach.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.DisplayName).HasMaxLength(200);
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.TimeZoneId).HasMaxLength(64);

        builder.OwnsMany(u => u.Logins, login =>
        {
            login.ToTable("user_logins");
            login.WithOwner().HasForeignKey("UserId");
            login.Property<int>("Id");
            login.HasKey("Id");
            login.Property(l => l.Provider).HasMaxLength(32);
            login.Property(l => l.Subject).HasMaxLength(256);
            login.HasIndex(l => new { l.Provider, l.Subject }).IsUnique();
        });
        builder.Navigation(u => u.Logins).HasField("_logins").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Owner foreign keys live here so the owned aggregates stay unaware of the User type.
        builder.HasMany<Certification>().WithOne().HasForeignKey(c => c.OwnerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<StudyPlan>().WithOne().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
