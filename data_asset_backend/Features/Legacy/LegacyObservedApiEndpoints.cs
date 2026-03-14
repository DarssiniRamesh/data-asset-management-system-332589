using System.ComponentModel.DataAnnotations;
using DataAssetBackend.Features.Assets;
using DataAssetBackend.Infrastructure.Api;
using Microsoft.AspNetCore.Mvc;

namespace DataAssetBackend.Features.Legacy;

/// <summary>
/// Central registration point for BRD §9 legacy/observed endpoints.
///
/// These endpoints exist for backward compatibility with legacy client integrations that still
/// call the BRD-observed routes (e.g., /api/siteassets/*, /api/inputefsourcemapping/*, /api/calculatedthroughputequationsetup/*).
///
/// Design goals:
/// - One canonical mapping function so shims are not scattered across Program.cs.
/// - Delegate to existing flows/repos (no duplicated business logic).
/// - Appear in OpenAPI (no ExcludeFromDescription).
/// - Strong contracts: typed request bodies and predictable status codes.
/// </summary>
public static class LegacyObservedApiEndpoints
{
    /// <summary>
    /// Registers BRD §9 legacy/observed compatibility endpoints onto the provided route builder.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Inputs: ASP.NET Core endpoint route builder.
    /// - Outputs: endpoints registered into the runtime pipeline.
    /// - Errors: delegates to existing flows; repository exceptions are handled by existing API-level try/catch blocks
    ///   or by the unified exception middleware in Program.cs.
    /// - Side effects: none beyond endpoint registration.
    /// </remarks>
    // PUBLIC_INTERFACE
    public static void MapLegacyObservedApiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .WithTags("LegacyCompatibility")
            .WithSummary("Legacy/Observed API compatibility endpoints (BRD §9)")
            .WithDescription(
                "Compatibility routes for BRD §9 'API Inventory (Observed)' endpoints. " +
                "These routes delegate to the canonical /api/assets/** flows and related child-resource flows.");

        MapSiteAssetsObservedEndpoints(group);
        MapInputEfSourceMappingObservedEndpoints(group);
        MapCalculatedThroughputEquationSetupObservedEndpoints(group);
    }

    private static void MapSiteAssetsObservedEndpoints(RouteGroupBuilder group)
    {
        // NOTE: BRD §9 lists:
        // - api/siteassets/managesiteassets
        // - api/siteassets/removesiteasset
        //
        // Because the BRD excerpt does not provide the legacy request shape, we implement
        // a stable compatibility contract that can represent both create and update operations
        // while delegating to the canonical flows.

        group.MapPost("/siteassets/managesiteassets", async (
                ManageSiteAssetsRequest request,
                AssetRepository repository,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("Legacy.ManageSiteAssets");

                RequestValidation.ValidateAndThrow(request, nameof(ManageSiteAssetsRequest));

                if (string.Equals(request.Mode, "create", StringComparison.OrdinalIgnoreCase))
                {
                    if (request.Create is null)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            [nameof(request.Create)] = new[] { "create payload is required when mode is 'create'." }
                        });
                    }

                    var created = await AssetFlows.CreateAssetAsync(
                        new AssetFlows.CreateAssetFlowRequest(request.Create),
                        repository,
                        logger,
                        cancellationToken);

                    // Legacy callers typically don't rely on Location header; we still return 201.
                    return Results.Created($"/api/assets/{created.Asset.AssetId}", created.Asset);
                }

                if (string.Equals(request.Mode, "update", StringComparison.OrdinalIgnoreCase))
                {
                    if (!request.AssetId.HasValue)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            [nameof(request.AssetId)] = new[] { "assetId is required when mode is 'update'." }
                        });
                    }

                    if (request.Update is null)
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            [nameof(request.Update)] = new[] { "update payload is required when mode is 'update'." }
                        });
                    }

                    var updated = await AssetFlows.UpdateAssetAsync(
                        new AssetFlows.UpdateAssetFlowRequest(request.AssetId.Value, request.Update),
                        repository,
                        logger,
                        cancellationToken);

                    return updated.Asset is null ? Results.NotFound() : Results.Ok(updated.Asset);
                }

                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Mode)] = new[] { "mode must be either 'create' or 'update'." }
                });
            })
            .RequireAuthorization("CanWrite")
            .WithName("Legacy_ManageSiteAssets")
            .WithSummary("Legacy: Manage site assets (create or update)")
            .WithDescription(
                "BRD §9 observed endpoint: /api/siteassets/managesiteassets. " +
                "This compatibility endpoint delegates to the canonical asset create/update flows.")
            .Accepts<ManageSiteAssetsRequest>("application/json")
            .Produces<AssetDto>(StatusCodes.Status201Created)
            .Produces<AssetDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/siteassets/removesiteasset", async (
                RemoveSiteAssetRequest request,
                AssetRepository repository,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("Legacy.RemoveSiteAsset");

                RequestValidation.ValidateAndThrow(request, nameof(RemoveSiteAssetRequest));

                var deleted = await AssetFlows.DeleteAssetAsync(
                    new AssetFlows.DeleteAssetFlowRequest(
                        request.AssetId!.Value,
                        new DeleteAssetRequest { ModifiedBy = request.ModifiedBy, CorrelationId = request.CorrelationId }),
                    repository,
                    logger,
                    cancellationToken);

                return deleted.Deleted ? Results.NoContent() : Results.NotFound();
            })
            .RequireAuthorization("AdminOnly")
            .WithName("Legacy_RemoveSiteAsset")
            .WithSummary("Legacy: Remove site asset (soft delete)")
            .WithDescription(
                "BRD §9 observed endpoint: /api/siteassets/removesiteasset. " +
                "Delegates to the canonical asset soft-delete flow.")
            .Accepts<RemoveSiteAssetRequest>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapInputEfSourceMappingObservedEndpoints(RouteGroupBuilder group)
    {
        // NOTE: BRD §9 lists:
        // - api/inputefsourcemapping/...
        //
        // Canonical endpoints are nested under:
        // - /api/assets/{assetId}/input-parameters/{inputParameterId}/ef-source-mappings
        //
        // Legacy compatibility endpoints are provided below and delegate to AssetChildFlows.

        group.MapPost("/inputefsourcemapping/{assetId:long}/{inputParameterId:long}", async (
                long assetId,
                long inputParameterId,
                CreateEfSourceMappingRequest request,
                AssetRepository repository,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("Legacy.CreateInputEfSourceMapping");

                var created = await AssetChildFlows.CreateEfSourceMappingAsync(
                    assetId,
                    inputParameterId,
                    request,
                    repository,
                    logger,
                    cancellationToken);

                return Results.Created(
                    $"/api/assets/{assetId}/input-parameters/{inputParameterId}/ef-source-mappings/{created.EfSourceMappingId}",
                    created);
            })
            .RequireAuthorization("CanWrite")
            .WithName("Legacy_CreateInputEfSourceMapping")
            .WithSummary("Legacy: Create EF source mapping for input parameter")
            .WithDescription(
                "BRD §9 observed endpoint family: /api/inputefsourcemapping/**. " +
                "Creates an EF source mapping row by delegating to the canonical AssetChildFlows.CreateEfSourceMappingAsync.")
            .Accepts<CreateEfSourceMappingRequest>("application/json")
            .Produces<EfSourceMappingDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/inputefsourcemapping/{assetId:long}/{inputParameterId:long}", async (
                long assetId,
                long inputParameterId,
                AssetRepository repository,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("Legacy.ListInputEfSourceMappings");

                var list = await AssetChildFlows.ListEfSourceMappingsAsync(
                    assetId,
                    inputParameterId,
                    repository,
                    logger,
                    cancellationToken);

                return Results.Ok(list);
            })
            .RequireAuthorization("CanRead")
            .WithName("Legacy_ListInputEfSourceMappings")
            .WithSummary("Legacy: List EF source mappings for input parameter")
            .WithDescription(
                "BRD §9 observed endpoint family: /api/inputefsourcemapping/**. " +
                "Lists EF source mapping rows by delegating to the canonical AssetChildFlows.ListEfSourceMappingsAsync.")
            .Produces<IReadOnlyList<EfSourceMappingDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/inputefsourcemapping/{assetId:long}/{inputParameterId:long}/{efSourceMappingId:long}", async (
                long assetId,
                long inputParameterId,
                long efSourceMappingId,
                UpdateEfSourceMappingRequest request,
                AssetRepository repository,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("Legacy.UpdateInputEfSourceMapping");

                var updated = await AssetChildFlows.UpdateEfSourceMappingAsync(
                    assetId,
                    inputParameterId,
                    efSourceMappingId,
                    request,
                    repository,
                    logger,
                    cancellationToken);

                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
            .RequireAuthorization("CanWrite")
            .WithName("Legacy_UpdateInputEfSourceMapping")
            .WithSummary("Legacy: Update EF source mapping for input parameter")
            .WithDescription(
                "BRD §9 observed endpoint family: /api/inputefsourcemapping/**. " +
                "Updates an EF source mapping row by delegating to the canonical AssetChildFlows.UpdateEfSourceMappingAsync.")
            .Accepts<UpdateEfSourceMappingRequest>("application/json")
            .Produces<EfSourceMappingDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapCalculatedThroughputEquationSetupObservedEndpoints(RouteGroupBuilder group)
    {
        // NOTE: BRD §9 lists an observed endpoint like:
        //   api/calculatedthroughputequationsetup/.../generatethroughputforinputparameter
        //
        // The current backend already persists throughput equation configuration as:
        // - throughput_equation rows (created via AssetChildFlows.CreateThroughputEquationAsync)
        //
        // Without the full legacy request contract, we provide a stable compatibility endpoint that:
        // - accepts the canonical CreateThroughputEquationRequest shape
        // - stores the throughput equation row by delegating to AssetChildFlows.CreateThroughputEquationAsync
        // - returns the created ThroughputEquationDto

        group.MapPost(
                "/calculatedthroughputequationsetup/{assetId:long}/{inputParameterId:long}/generatethroughputforinputparameter",
                async (
                    long assetId,
                    long inputParameterId,
                    CreateThroughputEquationRequest request,
                    AssetRepository repository,
                    ILoggerFactory loggerFactory,
                    CancellationToken cancellationToken) =>
                {
                    var logger = loggerFactory.CreateLogger("Legacy.GenerateThroughputForInputParameter");

                    var created = await AssetChildFlows.CreateThroughputEquationAsync(
                        assetId,
                        inputParameterId,
                        request,
                        repository,
                        logger,
                        cancellationToken);

                    return Results.Created(
                        $"/api/assets/{assetId}/input-parameters/{inputParameterId}/throughput-equations/{created.ThroughputEquationId}",
                        created);
                })
            .RequireAuthorization("CanWrite")
            .WithName("Legacy_GenerateThroughputForInputParameter")
            .WithSummary("Legacy: Generate throughput for input parameter")
            .WithDescription(
                "BRD §9 observed endpoint: /api/calculatedthroughputequationsetup/**/generatethroughputforinputparameter. " +
                "This compatibility endpoint delegates to the canonical throughput equation create flow, persisting a throughput equation row.")
            .Accepts<CreateThroughputEquationRequest>("application/json")
            .Produces<ThroughputEquationDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);
    }
}

/// <summary>
/// Compatibility request contract for the BRD-observed /api/siteassets/managesiteassets endpoint.
/// </summary>
/// <remarks>
/// The legacy BRD excerpt does not specify payload fields. This request provides a stable compatibility shape
/// that supports both create and update while delegating to the canonical create/update flows.
/// </remarks>
public sealed class ManageSiteAssetsRequest : IValidatableObject
{
    /// <summary>
    /// Operation mode: "create" or "update".
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Mode { get; set; } = "create";

    /// <summary>
    /// Asset ID to update; required when mode is "update".
    /// </summary>
    public long? AssetId { get; set; }

    /// <summary>
    /// Canonical create payload; required when mode is "create".
    /// </summary>
    public CreateAssetRequest? Create { get; set; }

    /// <summary>
    /// Canonical update payload; required when mode is "update".
    /// </summary>
    public UpdateAssetRequest? Update { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.Equals(Mode, "create", StringComparison.OrdinalIgnoreCase))
        {
            if (Create is null)
            {
                yield return new ValidationResult("create payload is required when mode is 'create'.", new[] { nameof(Create) });
            }
        }
        else if (string.Equals(Mode, "update", StringComparison.OrdinalIgnoreCase))
        {
            if (!AssetId.HasValue)
            {
                yield return new ValidationResult("assetId is required when mode is 'update'.", new[] { nameof(AssetId) });
            }

            if (Update is null)
            {
                yield return new ValidationResult("update payload is required when mode is 'update'.", new[] { nameof(Update) });
            }
        }
        else
        {
            yield return new ValidationResult("mode must be either 'create' or 'update'.", new[] { nameof(Mode) });
        }
    }
}

/// <summary>
/// Compatibility request contract for the BRD-observed /api/siteassets/removesiteasset endpoint.
/// </summary>
public sealed class RemoveSiteAssetRequest
{
    /// <summary>
    /// The asset id to soft-delete.
    /// </summary>
    /// <remarks>
    /// Kept nullable so JSON payloads like <c>{ "assetId": null }</c> bind successfully and are surfaced
    /// as a 400 ValidationProblem (via <see cref="RequestValidation.ValidateAndThrow"/>) rather than a 500
    /// due to JSON binding failure.
    /// </remarks>
    [Required]
    public long? AssetId { get; set; }

    /// <summary>
    /// BRD §6.11 audit field.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    /// <summary>
    /// BRD §6.11 trace field.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}
