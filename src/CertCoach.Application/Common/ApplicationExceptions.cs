namespace CertCoach.Application.Common;

/// <summary>The resource does not exist or belongs to another user. Mapped to HTTP 404.</summary>
public sealed class NotFoundException(string resource) : Exception($"{resource} was not found.");

/// <summary>The request conflicts with the current state. Mapped to HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>The learner has used this month's AI allowance. Mapped to HTTP 402.</summary>
public sealed class AiAllowanceExceededException(string message) : Exception(message);
