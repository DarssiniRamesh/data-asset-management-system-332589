using System.ComponentModel.DataAnnotations;

namespace DataAssetBackend.Infrastructure.Api;

/// <summary>
/// Exception used to represent request/model validation failures in a consistent, API-friendly way.
/// </summary>
public sealed class RequestValidationException : ValidationException
{
    /// <summary>
    /// Creates a new validation exception with a set of field-level errors (ValidationProblem compatible).
    /// </summary>
    public RequestValidationException(IDictionary<string, string[]> errors)
        : base("Request validation failed.")
    {
        Errors = new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A map of field name -> list of validation messages.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
