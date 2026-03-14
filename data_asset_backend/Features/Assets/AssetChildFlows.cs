using DataAssetBackend.Infrastructure.Api;
using Microsoft.Extensions.Logging;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Flows for BRD-evidenced asset-scoped child resources.
/// </summary>
public static class AssetChildFlows
{
    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an asset status log row for the given asset.
    /// </summary>
    public static async Task<AssetStatusLogDto> CreateAssetStatusLogAsync(
        long assetId,
        CreateAssetStatusLogRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.CreateAssetStatusLog starting. asset_id={AssetId}", assetId);

        // No-assumptions validation: ensure the asset exists (otherwise 404 at API layer).
        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            logger.LogInformation("AssetChildFlows.CreateAssetStatusLog asset not found. asset_id={AssetId}", assetId);
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        // BRD §6.2 chronology rule: “Latest status from-date must be after previous to-date”.
        // Evidence-based implementation: compare this row's from-date to the most recent prior row's to-date (when present).
        var previousToDate = await repository.GetLatestStatusToDateAsync(assetId, excludeAssetStatusLogId: null, cancellationToken);
        if (previousToDate.HasValue && request.StatusFromDate <= previousToDate.Value)
        {
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.StatusFromDate)] = new[]
                {
                    $"statusFromDate must be after the previous statusToDate ({previousToDate.Value:yyyy-MM-dd})."
                }
            });
        }

        var created = await repository.CreateAssetStatusLogAsync(assetId, request, cancellationToken);

        logger.LogInformation(
            "AssetChildFlows.CreateAssetStatusLog completed. asset_status_log_id={Id}",
            created.AssetStatusLogId);

        return created;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists asset status log rows for the given asset.
    /// </summary>
    public static async Task<IReadOnlyList<AssetStatusLogDto>> ListAssetStatusLogsAsync(
        long assetId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.ListAssetStatusLogs starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            logger.LogInformation("AssetChildFlows.ListAssetStatusLogs asset not found. asset_id={AssetId}", assetId);
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        var list = await repository.ListAssetStatusLogsAsync(assetId, cancellationToken);
        logger.LogInformation("AssetChildFlows.ListAssetStatusLogs completed. count={Count}", list.Count);
        return list;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets a specific status log row by id under an asset scope (ensures it belongs to the asset).
    /// </summary>
    public static async Task<AssetStatusLogDto?> GetAssetStatusLogByIdAsync(
        long assetId,
        long assetStatusLogId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.GetAssetStatusLogById starting. asset_id={AssetId}, asset_status_log_id={Id}",
            assetId,
            assetStatusLogId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.GetAssetStatusLogByIdAsync(assetId, assetStatusLogId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a specific status log row by id under an asset scope (ensures it belongs to the asset).
    /// </summary>
    public static async Task<AssetStatusLogDto?> UpdateAssetStatusLogAsync(
        long assetId,
        long assetStatusLogId,
        UpdateAssetStatusLogRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateAssetStatusLog starting. asset_id={AssetId}, asset_status_log_id={Id}",
            assetId,
            assetStatusLogId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        // BRD §6.2 chronology rule: “Latest status from-date must be after previous to-date”.
        var previousToDate = await repository.GetLatestStatusToDateAsync(assetId, excludeAssetStatusLogId: assetStatusLogId, cancellationToken);
        if (previousToDate.HasValue && request.StatusFromDate <= previousToDate.Value)
        {
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.StatusFromDate)] = new[]
                {
                    $"statusFromDate must be after the previous statusToDate ({previousToDate.Value:yyyy-MM-dd})."
                }
            });
        }

        return await repository.UpdateAssetStatusLogAsync(assetId, assetStatusLogId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an additional asset id row under an asset.
    /// </summary>
    public static async Task<AdditionalAssetIdDto> CreateAdditionalAssetIdAsync(
        long assetId,
        CreateAdditionalAssetIdRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.CreateAdditionalAssetId starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.CreateAdditionalAssetIdAsync(assetId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists additional asset id rows for an asset.
    /// </summary>
    public static async Task<IReadOnlyList<AdditionalAssetIdDto>> ListAdditionalAssetIdsAsync(
        long assetId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.ListAdditionalAssetIds starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.ListAdditionalAssetIdsAsync(assetId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets an additional asset id by id under an asset scope.
    /// </summary>
    public static async Task<AdditionalAssetIdDto?> GetAdditionalAssetIdByIdAsync(
        long assetId,
        long additionalAssetId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.GetAdditionalAssetIdById starting. asset_id={AssetId}, additional_asset_id={ChildId}",
            assetId,
            additionalAssetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.GetAdditionalAssetIdByIdAsync(assetId, additionalAssetId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates an additional asset id by id under an asset scope.
    /// </summary>
    public static async Task<AdditionalAssetIdDto?> UpdateAdditionalAssetIdAsync(
        long assetId,
        long additionalAssetId,
        UpdateAdditionalAssetIdRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateAdditionalAssetId starting. asset_id={AssetId}, additional_asset_id={ChildId}",
            assetId,
            additionalAssetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.UpdateAdditionalAssetIdAsync(assetId, additionalAssetId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an asset property row under an asset.
    /// </summary>
    public static async Task<AssetPropertyDto> CreateAssetPropertyAsync(
        long assetId,
        CreateAssetPropertyRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.CreateAssetProperty starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.CreateAssetPropertyAsync(assetId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists asset properties for an asset.
    /// </summary>
    public static async Task<IReadOnlyList<AssetPropertyDto>> ListAssetPropertiesAsync(
        long assetId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.ListAssetProperties starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.ListAssetPropertiesAsync(assetId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets an asset property by id under an asset scope.
    /// </summary>
    public static async Task<AssetPropertyDto?> GetAssetPropertyByIdAsync(
        long assetId,
        long assetPropertyId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.GetAssetPropertyById starting. asset_id={AssetId}, asset_property_id={ChildId}",
            assetId,
            assetPropertyId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.GetAssetPropertyByIdAsync(assetId, assetPropertyId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates an asset property by id under an asset scope.
    /// </summary>
    public static async Task<AssetPropertyDto?> UpdateAssetPropertyAsync(
        long assetId,
        long assetPropertyId,
        UpdateAssetPropertyRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateAssetProperty starting. asset_id={AssetId}, asset_property_id={ChildId}",
            assetId,
            assetPropertyId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.UpdateAssetPropertyAsync(assetId, assetPropertyId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a control device mapping row under an asset.
    /// </summary>
    public static async Task<ControlDeviceMappingDto> CreateControlDeviceMappingAsync(
        long assetId,
        CreateControlDeviceMappingRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.CreateControlDeviceMapping starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.CreateControlDeviceMappingAsync(assetId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists control device mapping rows under an asset.
    /// </summary>
    public static async Task<IReadOnlyList<ControlDeviceMappingDto>> ListControlDeviceMappingsAsync(
        long assetId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.ListControlDeviceMappings starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.ListControlDeviceMappingsAsync(assetId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets a control device mapping by id under an asset scope.
    /// </summary>
    public static async Task<ControlDeviceMappingDto?> GetControlDeviceMappingByIdAsync(
        long assetId,
        long controlDeviceMappingId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.GetControlDeviceMappingById starting. asset_id={AssetId}, control_device_mapping_id={ChildId}",
            assetId,
            controlDeviceMappingId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.GetControlDeviceMappingByIdAsync(assetId, controlDeviceMappingId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a control device mapping by id under an asset scope.
    /// </summary>
    public static async Task<ControlDeviceMappingDto?> UpdateControlDeviceMappingAsync(
        long assetId,
        long controlDeviceMappingId,
        UpdateControlDeviceMappingRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateControlDeviceMapping starting. asset_id={AssetId}, control_device_mapping_id={ChildId}",
            assetId,
            controlDeviceMappingId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.UpdateControlDeviceMappingAsync(assetId, controlDeviceMappingId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an input parameter row under an asset.
    /// </summary>
    public static async Task<InputParameterDto> CreateInputParameterAsync(
        long assetId,
        CreateInputParameterRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.CreateInputParameter starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.CreateInputParameterAsync(assetId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists input parameter rows under an asset.
    /// </summary>
    public static async Task<IReadOnlyList<InputParameterDto>> ListInputParametersAsync(
        long assetId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.ListInputParameters starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.ListInputParametersAsync(assetId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets an input parameter by id under an asset scope.
    /// </summary>
    public static async Task<InputParameterDto?> GetInputParameterByIdAsync(
        long assetId,
        long inputParameterId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.GetInputParameterById starting. asset_id={AssetId}, input_parameter_id={ChildId}",
            assetId,
            inputParameterId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.GetInputParameterByIdAsync(assetId, inputParameterId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates an input parameter by id under an asset scope.
    /// </summary>
    public static async Task<InputParameterDto?> UpdateInputParameterAsync(
        long assetId,
        long inputParameterId,
        UpdateInputParameterRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateInputParameter starting. asset_id={AssetId}, input_parameter_id={ChildId}",
            assetId,
            inputParameterId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.UpdateInputParameterAsync(assetId, inputParameterId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a parent input mapping under an asset scope for a child input parameter.
    /// </summary>
    public static async Task<ParentInputMappingDto> CreateParentInputMappingAsync(
        long assetId,
        long childInputParameterId,
        CreateParentInputMappingRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.CreateParentInputMapping starting. asset_id={AssetId}, child_input_parameter_id={ChildId}",
            assetId,
            childInputParameterId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        // No-assumptions validation: since the route is asset scoped, ensure both inputs belong to the asset.
        if (!await repository.InputParameterBelongsToAssetAsync(assetId, childInputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", childInputParameterId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, request.ParentInputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", request.ParentInputParameterId);
        }

        return await repository.CreateParentInputMappingAsync(childInputParameterId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists parent input mappings for a given child input parameter under an asset scope.
    /// </summary>
    public static async Task<IReadOnlyList<ParentInputMappingDto>> ListParentInputMappingsAsync(
        long assetId,
        long childInputParameterId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.ListParentInputMappings starting. asset_id={AssetId}, child_input_parameter_id={ChildId}",
            assetId,
            childInputParameterId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, childInputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", childInputParameterId);
        }

        return await repository.ListParentInputMappingsAsync(childInputParameterId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a parent input mapping row under an asset scope.
    /// </summary>
    public static async Task<ParentInputMappingDto?> UpdateParentInputMappingAsync(
        long assetId,
        long childInputParameterId,
        long parentInputMappingId,
        UpdateParentInputMappingRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateParentInputMapping starting. asset_id={AssetId}, child_input_parameter_id={ChildId}, parent_input_mapping_id={MapId}",
            assetId,
            childInputParameterId,
            parentInputMappingId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, childInputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", childInputParameterId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, request.ParentInputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", request.ParentInputParameterId);
        }

        return await repository.UpdateParentInputMappingAsync(childInputParameterId, parentInputMappingId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a reporting attribute mapping row under an asset.
    /// </summary>
    public static async Task<ReportingAttributeMappingDto> CreateReportingAttributeMappingAsync(
        long assetId,
        CreateReportingAttributeMappingRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.CreateReportingAttributeMapping starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.CreateReportingAttributeMappingAsync(assetId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists reporting attribute mapping rows under an asset.
    /// </summary>
    public static async Task<IReadOnlyList<ReportingAttributeMappingDto>> ListReportingAttributeMappingsAsync(
        long assetId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetChildFlows.ListReportingAttributeMappings starting. asset_id={AssetId}", assetId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.ListReportingAttributeMappingsAsync(assetId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets a reporting attribute mapping by id under an asset scope.
    /// </summary>
    public static async Task<ReportingAttributeMappingDto?> GetReportingAttributeMappingByIdAsync(
        long assetId,
        long reportingAttributeMappingId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.GetReportingAttributeMappingById starting. asset_id={AssetId}, reporting_attribute_mapping_id={ChildId}",
            assetId,
            reportingAttributeMappingId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.GetReportingAttributeMappingByIdAsync(assetId, reportingAttributeMappingId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a reporting attribute mapping by id under an asset scope.
    /// </summary>
    public static async Task<ReportingAttributeMappingDto?> UpdateReportingAttributeMappingAsync(
        long assetId,
        long reportingAttributeMappingId,
        UpdateReportingAttributeMappingRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateReportingAttributeMapping starting. asset_id={AssetId}, reporting_attribute_mapping_id={ChildId}",
            assetId,
            reportingAttributeMappingId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        return await repository.UpdateReportingAttributeMappingAsync(assetId, reportingAttributeMappingId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an EF source mapping under an asset scope for a given input parameter.
    /// </summary>
    public static async Task<EfSourceMappingDto> CreateEfSourceMappingAsync(
        long assetId,
        long inputParameterId,
        CreateEfSourceMappingRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.CreateEfSourceMapping starting. asset_id={AssetId}, input_parameter_id={InputId}",
            assetId,
            inputParameterId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        return await repository.CreateEfSourceMappingAsync(inputParameterId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists EF source mappings under an asset scope for a given input parameter.
    /// </summary>
    public static async Task<IReadOnlyList<EfSourceMappingDto>> ListEfSourceMappingsAsync(
        long assetId,
        long inputParameterId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.ListEfSourceMappings starting. asset_id={AssetId}, input_parameter_id={InputId}",
            assetId,
            inputParameterId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        return await repository.ListEfSourceMappingsAsync(inputParameterId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates an EF source mapping under an asset scope for a given input parameter.
    /// </summary>
    public static async Task<EfSourceMappingDto?> UpdateEfSourceMappingAsync(
        long assetId,
        long inputParameterId,
        long efSourceMappingId,
        UpdateEfSourceMappingRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateEfSourceMapping starting. asset_id={AssetId}, input_parameter_id={InputId}, ef_source_mapping_id={MapId}",
            assetId,
            inputParameterId,
            efSourceMappingId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        return await repository.UpdateEfSourceMappingAsync(inputParameterId, efSourceMappingId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a throughput equation under an asset scope for a given input parameter.
    /// </summary>
    public static async Task<ThroughputEquationDto> CreateThroughputEquationAsync(
        long assetId,
        long inputParameterId,
        CreateThroughputEquationRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.CreateThroughputEquation starting. asset_id={AssetId}, input_parameter_id={InputId}",
            assetId,
            inputParameterId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        return await repository.CreateThroughputEquationAsync(inputParameterId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists throughput equations under an asset scope for a given input parameter.
    /// </summary>
    public static async Task<IReadOnlyList<ThroughputEquationDto>> ListThroughputEquationsAsync(
        long assetId,
        long inputParameterId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.ListThroughputEquations starting. asset_id={AssetId}, input_parameter_id={InputId}",
            assetId,
            inputParameterId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        return await repository.ListThroughputEquationsAsync(inputParameterId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a throughput equation under an asset scope for a given input parameter.
    /// </summary>
    public static async Task<ThroughputEquationDto?> UpdateThroughputEquationAsync(
        long assetId,
        long inputParameterId,
        long throughputEquationId,
        UpdateThroughputEquationRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateThroughputEquation starting. asset_id={AssetId}, input_parameter_id={InputId}, throughput_equation_id={EqId}",
            assetId,
            inputParameterId,
            throughputEquationId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        return await repository.UpdateThroughputEquationAsync(inputParameterId, throughputEquationId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a throughput scalar under an asset scope for a given throughput equation.
    /// </summary>
    public static async Task<ThroughputScalarDto> CreateThroughputScalarAsync(
        long assetId,
        long inputParameterId,
        long throughputEquationId,
        CreateThroughputScalarRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.CreateThroughputScalar starting. asset_id={AssetId}, input_parameter_id={InputId}, throughput_equation_id={EqId}",
            assetId,
            inputParameterId,
            throughputEquationId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        if (!await repository.ThroughputEquationBelongsToInputAsync(inputParameterId, throughputEquationId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("throughput_equation", throughputEquationId);
        }

        return await repository.CreateThroughputScalarAsync(throughputEquationId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists throughput scalars under an asset scope for a given throughput equation.
    /// </summary>
    public static async Task<IReadOnlyList<ThroughputScalarDto>> ListThroughputScalarsAsync(
        long assetId,
        long inputParameterId,
        long throughputEquationId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.ListThroughputScalars starting. asset_id={AssetId}, input_parameter_id={InputId}, throughput_equation_id={EqId}",
            assetId,
            inputParameterId,
            throughputEquationId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        if (!await repository.ThroughputEquationBelongsToInputAsync(inputParameterId, throughputEquationId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("throughput_equation", throughputEquationId);
        }

        return await repository.ListThroughputScalarsAsync(throughputEquationId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a throughput scalar under an asset scope for a given throughput equation.
    /// </summary>
    public static async Task<ThroughputScalarDto?> UpdateThroughputScalarAsync(
        long assetId,
        long inputParameterId,
        long throughputEquationId,
        long throughputScalarId,
        UpdateThroughputScalarRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateThroughputScalar starting. asset_id={AssetId}, input_parameter_id={InputId}, throughput_equation_id={EqId}, throughput_scalar_id={ScalarId}",
            assetId,
            inputParameterId,
            throughputEquationId,
            throughputScalarId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        if (!await repository.ThroughputEquationBelongsToInputAsync(inputParameterId, throughputEquationId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("throughput_equation", throughputEquationId);
        }

        return await repository.UpdateThroughputScalarAsync(throughputEquationId, throughputScalarId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a data input value under an asset scope for a given input parameter.
    /// </summary>
    public static async Task<DataInputValueDto> CreateDataInputValueAsync(
        long assetId,
        long inputParameterId,
        CreateDataInputValueRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.CreateDataInputValue starting. asset_id={AssetId}, input_parameter_id={InputId}",
            assetId,
            inputParameterId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        return await repository.CreateDataInputValueAsync(inputParameterId, request, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists data input values under an asset scope for a given input parameter.
    /// </summary>
    public static async Task<IReadOnlyList<DataInputValueDto>> ListDataInputValuesAsync(
        long assetId,
        long inputParameterId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.ListDataInputValues starting. asset_id={AssetId}, input_parameter_id={InputId}",
            assetId,
            inputParameterId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        return await repository.ListDataInputValuesAsync(inputParameterId, cancellationToken);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a data input value under an asset scope for a given input parameter.
    /// </summary>
    public static async Task<DataInputValueDto?> UpdateDataInputValueAsync(
        long assetId,
        long inputParameterId,
        long dataInputValueId,
        UpdateDataInputValueRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetChildFlows.UpdateDataInputValue starting. asset_id={AssetId}, input_parameter_id={InputId}, data_input_value_id={ValueId}",
            assetId,
            inputParameterId,
            dataInputValueId);

        if (!await repository.AssetExistsAsync(assetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", assetId);
        }

        if (!await repository.InputParameterBelongsToAssetAsync(assetId, inputParameterId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("input_parameter", inputParameterId);
        }

        return await repository.UpdateDataInputValueAsync(inputParameterId, dataInputValueId, request, cancellationToken);
    }
}
