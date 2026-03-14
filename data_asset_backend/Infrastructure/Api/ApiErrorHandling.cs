using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataAssetBackend.Infrastructure.Api;

/// <summary>
/// Centralized API error handling utilities.
/// </summary>
/// <remarks>
/// Contract:
/// - Inputs:
///   - An <see cref="Exception"/> thrown by flows/repositories or framework code.
///   - The current <see cref="HttpContext"/> (for trace id / request path).
/// - Outputs:
///   - An <see cref="IResult"/> representing RFC7807 ProblemDetails (or plain status where appropriate).
/// - Error mapping (unified across endpoints):
///   - <see cref="DataAssetBackend.Features.Assets.AssetRepository.EntityNotFoundException"/> -> 404 Not Found
///   - <see cref="DataAssetBackend.Features.Assets.AssetCopyLineageFlows.MissingFiltersException"/> -> 400 Bad Request
///   - <see cref="InvalidOperationException"/> when DB is not configured -> 503 Service Unavailable
///   - <see cref="PostgresException"/> integrity/constraint issues -> 409 Conflict
///   - Otherwise -> 500 Internal Server Error
/// - Observability:
///   - Uses the request trace id in ProblemDetails.extensions["traceId"].
///   - Logs exceptions at appropriate levels.
/// </remarks>
public static class ApiErrorHandling
{
    /// <summary>
    /// Registers global exception handling middleware that converts uncaught exceptions into consistent ProblemDetails responses.
    /// </summary>
    public static void UseUnifiedExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler(handlerApp =>
        {
            handlerApp.Run(async ctx =>
            {
                var feature = ctx.Features.Get<IExceptionHandlerFeature>();
                var ex = feature?.Error;

                // If there's no exception, do nothing special.
                if (ex is null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    return;
                }

                var loggerFactory = ctx.RequestServices.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger("UnifiedApiExceptionHandler");

                var result = ToResult(ex, ctx, logger);
                await result.ExecuteAsync(ctx);
            });
        });
    }

    /// <summary>
    /// Converts an exception into an HTTP result with a consistent ProblemDetails body.
    /// </summary>
    public static IResult ToResult(Exception ex, HttpContext httpContext, ILogger logger)
    {
        // Special-case: return ValidationProblem with field errors for request validation failures.
        if (ex is RequestValidationException rve)
        {
            logger.LogWarning(ex, "API validation failure. path={Path}", httpContext.Request.Path);

            return Results.ValidationProblem(
                new Dictionary<string, string[]>(rve.Errors, StringComparer.OrdinalIgnoreCase),
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation failed",
                instance: httpContext.Request.Path);
        }

        var (status, title, detail) = MapException(ex);

        // Log level is part of the contract: expected/handled failures are warnings; unexpected are errors.
        if (status >= 500)
        {
            logger.LogError(ex, "Unhandled API exception mapped to {Status}. path={Path}", status, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(ex, "API exception mapped to {Status}. path={Path}", status, httpContext.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.com/{status}"
        };

        // Ensure callers can correlate server logs without leaking internals.
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        return Results.Problem(problem);
    }

    private static (int Status, string Title, string Detail) MapException(Exception ex)
    {
        // Validation failures (tab-level / cross-field)
        // Note: We return a 400 ValidationProblem result upstream; this mapping remains for title/detail fallback.
        if (ex is RequestValidationException rve)
        {
            return (
                StatusCodes.Status400BadRequest,
                "Validation failed",
                rve.Message
            );
        }

        if (ex is ValidationException ve)
        {
            return (
                StatusCodes.Status400BadRequest,
                "Validation failed",
                ve.Message
            );
        }

        // Not found (domain-level existence validation)
        if (ex is DataAssetBackend.Features.Assets.AssetRepository.EntityNotFoundException notFound)
        {
            return (
                StatusCodes.Status404NotFound,
                "Not found",
                notFound.Message
            );
        }

        // Domain-level uniqueness conflicts
        if (ex is DataAssetBackend.Features.Assets.AssetFlows.DuplicatePermitEuIdException)
        {
            return (
                StatusCodes.Status409Conflict,
                "Duplicate Permit EU ID",
                ex.Message
            );
        }

        // Bad request (query contract violation)
        if (ex is DataAssetBackend.Features.Assets.AssetCopyLineageFlows.MissingFiltersException)
        {
            return (
                StatusCodes.Status400BadRequest,
                "Bad request",
                ex.Message
            );
        }

        // DB not configured (thrown by NpgsqlConnectionFactory when no conn string can be resolved)
        // We intentionally match by message prefix so existing code paths do not require a new custom exception type.
        // This keeps the contract stable while still unifying the HTTP mapping.
        if (ex is InvalidOperationException ioe &&
            ioe.Message.StartsWith("Database is not configured", StringComparison.OrdinalIgnoreCase))
        {
            return (
                StatusCodes.Status503ServiceUnavailable,
                "Database not configured",
                ioe.Message
            );
        }

        // Postgres constraint errors (FK/unique/etc). Conservative mapping:
        // - SQLSTATE class 23 (integrity constraint violation) -> 409
        if (ex is PostgresException pg)
        {
            if (!string.IsNullOrWhiteSpace(pg.SqlState) && pg.SqlState.StartsWith("23", StringComparison.Ordinal))
            {
                return (
                    StatusCodes.Status409Conflict,
                    "Database constraint error",
                    pg.MessageText
                );
            }

            // Other Postgres errors are treated as internal server errors (don't leak too much detail).
            return (
                StatusCodes.Status500InternalServerError,
                "Database error",
                "A database error occurred."
            );
        }

        // Default: internal error
        return (
            StatusCodes.Status500InternalServerError,
            "Internal server error",
            "An unexpected error occurred."
        );
    }
}
