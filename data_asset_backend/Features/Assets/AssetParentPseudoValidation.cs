using DataAssetBackend.Infrastructure.Api;
using Microsoft.Extensions.Logging;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Reusable validation utilities for the Asset "Parent Pseudo Asset" relationship.
/// </summary>
public static class AssetParentPseudoValidation
{
    // PUBLIC_INTERFACE
    /// <summary>
    /// Validates semantics for <c>requiresParentPseudo</c> and <c>parentPseudoAssetId</c>.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Inputs:
    ///   - <paramref name="assetIdBeingUpdated"/>: null for create; asset id for update (used for self-reference check)
    ///   - <paramref name="requiresParentPseudo"/>: the client intent flag
    ///   - <paramref name="parentPseudoAssetId"/>: optional FK to another asset
    /// - Validation rules:
    ///   1) If requiresParentPseudo == false, parentPseudoAssetId must be null (avoid stale/accidental linkage).
    ///   2) If requiresParentPseudo == true, parentPseudoAssetId must be non-null and must reference an existing, non-deleted asset.
    ///   3) On update, parentPseudoAssetId must not equal the current asset id (no self-parenting).
    /// - Output: completes successfully or throws <see cref="RequestValidationException"/> (mapped to HTTP 400 with field errors).
    /// - Side effects: none (reads DB only when parentPseudoAssetId is provided).
    /// </remarks>
    public static async Task ValidateAndThrowAsync(
        long? assetIdBeingUpdated,
        bool requiresParentPseudo,
        long? parentPseudoAssetId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        // Keep the contract deterministic and avoid silent interpretation of "false but id provided".
        if (!requiresParentPseudo && parentPseudoAssetId is not null)
        {
            errors[nameof(CreateAssetRequest.ParentPseudoAssetId)] =
                new[] { "parentPseudoAssetId must be null when requiresParentPseudo is false." };
        }

        if (requiresParentPseudo)
        {
            if (parentPseudoAssetId is null)
            {
                errors[nameof(CreateAssetRequest.ParentPseudoAssetId)] =
                    new[] { "parentPseudoAssetId is required when requiresParentPseudo is true." };
            }
            else
            {
                // Update-specific: disallow self-reference.
                if (assetIdBeingUpdated.HasValue && parentPseudoAssetId.Value == assetIdBeingUpdated.Value)
                {
                    errors[nameof(CreateAssetRequest.ParentPseudoAssetId)] =
                        new[] { "parentPseudoAssetId must not refer to the asset being updated." };
                }
                else
                {
                    var exists = await repository.AssetExistsAsync(parentPseudoAssetId.Value, cancellationToken);
                    if (!exists)
                    {
                        errors[nameof(CreateAssetRequest.ParentPseudoAssetId)] =
                            new[] { $"parentPseudoAssetId must reference an existing, non-deleted asset (id={parentPseudoAssetId.Value})." };
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            logger.LogWarning(
                "AssetParentPseudoValidation failed. asset_id={AssetId}, requires_parent_pseudo={RequiresParentPseudo}, parent_pseudo_asset_id={ParentPseudoAssetId}",
                assetIdBeingUpdated,
                requiresParentPseudo,
                parentPseudoAssetId);

            throw new RequestValidationException(errors);
        }
    }
}
