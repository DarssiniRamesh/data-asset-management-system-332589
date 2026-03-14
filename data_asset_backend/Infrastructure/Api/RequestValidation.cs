using System.ComponentModel.DataAnnotations;

namespace DataAssetBackend.Infrastructure.Api;

/// <summary>
/// Request validation utilities for minimal APIs.
/// </summary>
public static class RequestValidation
{
    // PUBLIC_INTERFACE
    /// <summary>
    /// Validates a request object using DataAnnotations (including <see cref="IValidatableObject"/>).
    /// Throws <see cref="RequestValidationException"/> when invalid.
    /// </summary>
    /// <param name="request">The request object to validate.</param>
    /// <param name="objectNameForErrors">
    /// Optional prefix for errors when validation results do not specify a member name.
    /// When provided, errors are attributed to that name; otherwise, they use "request".
    /// </param>
    /// <exception cref="RequestValidationException">Thrown when validation fails.</exception>
    public static void ValidateAndThrow(object request, string? objectNameForErrors = null)
    {
        var ctx = new ValidationContext(request);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, ctx, results, validateAllProperties: true);

        if (isValid)
        {
            return;
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var vr in results)
        {
            var memberNames = (vr.MemberNames?.Any() ?? false)
                ? vr.MemberNames
                : new[] { objectNameForErrors ?? "request" };

            foreach (var member in memberNames)
            {
                if (!errors.TryGetValue(member, out var list))
                {
                    list = new List<string>();
                    errors[member] = list;
                }

                list.Add(vr.ErrorMessage ?? "Invalid value.");
            }
        }

        throw new RequestValidationException(errors.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
    }
}
