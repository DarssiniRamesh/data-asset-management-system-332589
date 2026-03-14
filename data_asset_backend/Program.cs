using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(config =>
{
    config.Title = "Data Asset Backend API";
    config.Version = "1.0.0";
    config.Description = "Backend API for data asset management (POC). Includes validation utilities for asset configuration workflows.";
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowCredentials()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Use CORS
app.UseCors("AllowAll");

// Configure OpenAPI/Swagger
app.UseOpenApi();
app.UseSwaggerUi(config =>
{
    config.Path = "/docs";
});

// Health check endpoint
app.MapGet("/", () => new { message = "Healthy" })
   .WithName("Health")
   .WithTags("Health");

// Validity check endpoint (lightweight utility for validating client-provided values)
//
// Route: POST /api/validity-check
// Body:  { "value": "..." }
// 200:   { "isValid": true, "reason": null }
app.MapPost("/api/validity-check", (ValidityCheckRequest request) =>
    {
        // Baseline checks (safe and generic)
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return Results.Ok(new ValidityCheckResponse { IsValid = false, Reason = "Value must not be empty." });
        }

        // Data-quality: avoid unintended whitespace
        if (!string.Equals(request.Value, request.Value.Trim(), StringComparison.Ordinal))
        {
            return Results.Ok(new ValidityCheckResponse { IsValid = false, Reason = "Value must not have leading or trailing whitespace." });
        }

        // Block control characters
        if (request.Value.Any(char.IsControl))
        {
            return Results.Ok(new ValidityCheckResponse { IsValid = false, Reason = "Value contains invalid control characters." });
        }

        return Results.Ok(new ValidityCheckResponse { IsValid = true, Reason = null });
    })
    .WithName("ValidityCheck")
    .WithTags("Validation")
    .WithSummary("Validity check utility")
    .WithDescription("Performs a basic validity check on an input value. This endpoint can be extended as asset/tab validation rules are implemented.")
    .Produces<ValidityCheckResponse>(StatusCodes.Status200OK)
    .Accepts<ValidityCheckRequest>("application/json");

app.Run();

/// <summary>
/// Request payload for the validity check endpoint.
/// </summary>
public sealed class ValidityCheckRequest
{
    /// <summary>
    /// The input value to validate.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "value is required")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Response payload for the validity check endpoint.
/// </summary>
public sealed class ValidityCheckResponse
{
    /// <summary>
    /// True when the value is considered valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Optional reason when invalid.
    /// </summary>
    public string? Reason { get; set; }
}
