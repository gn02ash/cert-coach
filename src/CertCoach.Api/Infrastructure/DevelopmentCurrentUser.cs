using CertCoach.Application.Abstractions;

namespace CertCoach.Api.Infrastructure;

/// <summary>
/// A fixed local user until sign-in is added. Configured under <c>CertCoach:DevUser</c>.
/// </summary>
internal sealed class DevelopmentCurrentUser(IConfiguration configuration) : ICurrentUser
{
    public static readonly Guid DefaultUserId = new("00000000-0000-0000-0000-000000000001");

    public Guid UserId { get; } = configuration.GetValue<Guid?>("CertCoach:DevUser:Id") ?? DefaultUserId;

    public TimeZoneInfo TimeZone { get; } =
        TimeZoneInfo.FindSystemTimeZoneById(configuration.GetValue<string>("CertCoach:DevUser:TimeZone") ?? "UTC");
}
