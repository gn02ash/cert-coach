namespace CertCoach.Api.Infrastructure;

internal static partial class StartupLog
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Local dev login is on: anyone who can reach this server can sign in without a password. "
            + "Keep it on your own machine, or set AUTH_DEV_LOGIN=false and configure a sign-in provider.")]
    public static partial void DevLoginEnabled(ILogger logger);
}
