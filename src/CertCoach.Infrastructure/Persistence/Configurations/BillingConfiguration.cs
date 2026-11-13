using CertCoach.Domain.Billing;
using CertCoach.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CertCoach.Infrastructure.Persistence.Configurations;

internal sealed class BillingAccountConfiguration : IEntityTypeConfiguration<BillingAccount>
{
    public void Configure(EntityTypeBuilder<BillingAccount> builder)
    {
        builder.ToTable("billing_accounts");
        builder.HasKey(a => a.UserId);
        builder.HasOne<User>().WithOne().HasForeignKey<BillingAccount>(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AiUsageConfiguration : IEntityTypeConfiguration<AiUsage>
{
    public void Configure(EntityTypeBuilder<AiUsage> builder)
    {
        builder.ToTable("ai_usage");
        builder.HasKey(u => new { u.UserId, u.Month });
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
