using CertCoach.Application.Common;
using CertCoach.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CertCoach.Api.Infrastructure;

/// <summary>Maps domain and application errors to RFC 9457 problem details.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            DomainException => (StatusCodes.Status400BadRequest, "The request breaks a rule."),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found."),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict."),
            _ => (0, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = exception.Message },
        });
    }
}
