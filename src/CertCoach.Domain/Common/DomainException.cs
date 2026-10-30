namespace CertCoach.Domain.Common;

/// <summary>Raised when an operation would violate a domain rule. The API maps it to HTTP 400/409.</summary>
public sealed class DomainException(string message) : Exception(message);
