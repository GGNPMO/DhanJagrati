using System.Text.Json;
using DhanJagrati.Core.Loans;
using DhanJagrati.Data;
using DhanJagrati.Data.Masters;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Api.Infrastructure;

/// <summary>Maps known exceptions to RFC 7807 problem responses; everything else becomes a generic 500.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            ValidationFailedException v => new ValidationProblemDetails(
                v.Errors.ToDictionary(e => JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Please correct the highlighted fields."
            },
            DomainException d => new ProblemDetails { Status = d.StatusCode, Title = d.Message },
            EmiCalculationException e => new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = e.Message },
            BadHttpRequestException b => new ProblemDetails { Status = b.StatusCode, Title = "The request could not be read." },
            DatabaseUnavailableException or SqlException { Number: -2 } => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "The database is not reachable. Try again shortly."
            },
            _ => new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "An unexpected error occurred." }
        };

        if (problem.Status >= 500)
            logger.LogError(exception, "Request failed on {Method} {Path}", http.Request.Method, http.Request.Path);

        http.Response.StatusCode = problem.Status ?? 500;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }
}

/// <summary>
/// Same-site cookies already block cross-site posts; requiring a custom header on unsafe
/// methods adds defence in depth (HTML forms cannot set custom headers).
/// </summary>
public sealed class ClientHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-DJ-Client";

    public Task InvokeAsync(HttpContext http)
    {
        if (http.Request.Path.StartsWithSegments("/api")
            && !HttpMethods.IsGet(http.Request.Method)
            && !HttpMethods.IsHead(http.Request.Method)
            && !HttpMethods.IsOptions(http.Request.Method)
            && !http.Request.Headers.ContainsKey(HeaderName))
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return http.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 400,
                Title = $"Missing {HeaderName} header."
            });
        }
        return next(http);
    }
}
