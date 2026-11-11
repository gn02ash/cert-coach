using CertCoach.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CertCoach.Infrastructure.Persistence.Configurations;

internal sealed class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.ToTable("api_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(ApiToken.MaxNameLength);
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.Property(t => t.Prefix).HasMaxLength(16);

        // Sign-in looks a token up by its hash.
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.OwnerId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
