using Microsoft.Extensions.Logging;

namespace DataAssetBackend.Features.Section4;

/// <summary>
/// Flows for BRD §4 modules.
/// </summary>
public static class Section4Flows
{
    // PUBLIC_INTERFACE
    /// <summary>
    /// List Site Profiles with optional siteId filter.
    /// </summary>
    public static Task<IReadOnlyList<SiteProfileDto>> ListSiteProfilesAsync(
        string? siteId,
        int? limit,
        Section4Repository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var normalizedLimit = Section4Repository.NormalizeLimit(limit);
        logger.LogInformation("Section4Flows.ListSiteProfiles starting. site_id={SiteId}, limit={Limit}", siteId, normalizedLimit);
        return repository.ListSiteProfilesAsync(siteId, normalizedLimit, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// List WWTS process streams with optional siteId filter.
    /// </summary>
    public static Task<IReadOnlyList<WwtsProcessStreamDto>> ListWwtsProcessStreamsAsync(
        string? siteId,
        int? limit,
        Section4Repository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var normalizedLimit = Section4Repository.NormalizeLimit(limit);
        logger.LogInformation("Section4Flows.ListWwtsProcessStreams starting. site_id={SiteId}, limit={Limit}", siteId, normalizedLimit);
        return repository.ListWwtsProcessStreamsAsync(siteId, normalizedLimit, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// List Chemical Raw Materials with optional siteId filter.
    /// </summary>
    public static Task<IReadOnlyList<ChemicalRawMaterialDto>> ListChemicalRawMaterialsAsync(
        string? siteId,
        int? limit,
        Section4Repository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var normalizedLimit = Section4Repository.NormalizeLimit(limit);
        logger.LogInformation("Section4Flows.ListChemicalRawMaterials starting. site_id={SiteId}, limit={Limit}", siteId, normalizedLimit);
        return repository.ListChemicalRawMaterialsAsync(siteId, normalizedLimit, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// List Chemical SDS rows with optional siteId filter.
    /// </summary>
    public static Task<IReadOnlyList<ChemicalSdsDto>> ListChemicalSdsAsync(
        string? siteId,
        int? limit,
        Section4Repository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var normalizedLimit = Section4Repository.NormalizeLimit(limit);
        logger.LogInformation("Section4Flows.ListChemicalSds starting. site_id={SiteId}, limit={Limit}", siteId, normalizedLimit);
        return repository.ListChemicalSdsAsync(siteId, normalizedLimit, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// List Lab Data Configurations with optional siteId filter.
    /// </summary>
    public static Task<IReadOnlyList<LabDataConfigurationDto>> ListLabDataConfigurationsAsync(
        string? siteId,
        int? limit,
        Section4Repository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var normalizedLimit = Section4Repository.NormalizeLimit(limit);
        logger.LogInformation("Section4Flows.ListLabDataConfigurations starting. site_id={SiteId}, limit={Limit}", siteId, normalizedLimit);
        return repository.ListLabDataConfigurationsAsync(siteId, normalizedLimit, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// List Water Process Configurations with optional siteId filter.
    /// </summary>
    public static Task<IReadOnlyList<WaterProcessConfigurationDto>> ListWaterProcessConfigurationsAsync(
        string? siteId,
        int? limit,
        Section4Repository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var normalizedLimit = Section4Repository.NormalizeLimit(limit);
        logger.LogInformation("Section4Flows.ListWaterProcessConfigurations starting. site_id={SiteId}, limit={Limit}", siteId, normalizedLimit);
        return repository.ListWaterProcessConfigurationsAsync(siteId, normalizedLimit, cancellationToken);
    }
}
