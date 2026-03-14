using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataAssetBackend.Features.Masters;

/// <summary>
/// Flows for BRD master/reference data (BRD §6.14).
/// </summary>
public static class MasterFlows
{
    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a UOM master row.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Inputs: <see cref="CreateUomMasterRequest"/> (includes BRD §6.11 audit fields)
    /// - Output: created <see cref="UomMasterDto"/>
    /// - Errors:
    ///   - <see cref="InvalidOperationException"/> when DB is not configured
    ///   - <see cref="PostgresException"/> for constraint issues
    /// - Side effects: INSERT into <c>uom_master</c>
    /// </remarks>
    public static async Task<UomMasterDto> CreateUomAsync(
        CreateUomMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.CreateUom starting. uom_key={UomKey}", request.UomKey);
        var created = await repository.CreateUomAsync(request, cancellationToken);
        logger.LogInformation("MasterFlows.CreateUom completed. uom_id={UomId}", created.UomId);
        return created;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a UOM master row.
    /// </summary>
    public static async Task<UomMasterDto?> UpdateUomAsync(
        long uomId,
        UpdateUomMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.UpdateUom starting. uom_id={UomId}", uomId);
        var updated = await repository.UpdateUomAsync(uomId, request, cancellationToken);
        logger.LogInformation("MasterFlows.UpdateUom completed. uom_id={UomId}, found={Found}", uomId, updated is not null);
        return updated;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Queries UOM masters.
    /// </summary>
    public static async Task<IReadOnlyList<UomMasterDto>> QueryUomsAsync(
        QueryMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.QueryUoms starting. active_only={ActiveOnly}, limit={Limit}", request.ActiveOnly, request.Limit);
        var list = await repository.QueryUomsAsync(request, cancellationToken);
        logger.LogInformation("MasterFlows.QueryUoms completed. count={Count}", list.Count);
        return list;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a reporting program master row.
    /// </summary>
    public static async Task<ReportingProgramMasterDto> CreateReportingProgramAsync(
        CreateReportingProgramMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.CreateReportingProgram starting. program_key={ProgramKey}", request.ProgramKey);
        var created = await repository.CreateReportingProgramAsync(request, cancellationToken);
        logger.LogInformation("MasterFlows.CreateReportingProgram completed. reporting_program_id={Id}", created.ReportingProgramId);
        return created;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a reporting program master row.
    /// </summary>
    public static async Task<ReportingProgramMasterDto?> UpdateReportingProgramAsync(
        long reportingProgramId,
        UpdateReportingProgramMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.UpdateReportingProgram starting. reporting_program_id={Id}", reportingProgramId);
        var updated = await repository.UpdateReportingProgramAsync(reportingProgramId, request, cancellationToken);
        logger.LogInformation("MasterFlows.UpdateReportingProgram completed. reporting_program_id={Id}, found={Found}", reportingProgramId, updated is not null);
        return updated;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Queries reporting program masters.
    /// </summary>
    public static async Task<IReadOnlyList<ReportingProgramMasterDto>> QueryReportingProgramsAsync(
        QueryMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.QueryReportingPrograms starting. active_only={ActiveOnly}, limit={Limit}", request.ActiveOnly, request.Limit);
        var list = await repository.QueryReportingProgramsAsync(request, cancellationToken);
        logger.LogInformation("MasterFlows.QueryReportingPrograms completed. count={Count}", list.Count);
        return list;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a control device master row.
    /// </summary>
    public static async Task<ControlDeviceMasterDto> CreateControlDeviceAsync(
        CreateControlDeviceMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.CreateControlDevice starting. site_id={SiteId}, device_key={DeviceKey}", request.SiteId, request.DeviceKey);
        var created = await repository.CreateControlDeviceAsync(request, cancellationToken);
        logger.LogInformation("MasterFlows.CreateControlDevice completed. control_device_id={Id}", created.ControlDeviceId);
        return created;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a control device master row.
    /// </summary>
    public static async Task<ControlDeviceMasterDto?> UpdateControlDeviceAsync(
        long controlDeviceId,
        UpdateControlDeviceMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.UpdateControlDevice starting. control_device_id={Id}", controlDeviceId);
        var updated = await repository.UpdateControlDeviceAsync(controlDeviceId, request, cancellationToken);
        logger.LogInformation("MasterFlows.UpdateControlDevice completed. control_device_id={Id}, found={Found}", controlDeviceId, updated is not null);
        return updated;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Queries control device masters.
    /// </summary>
    public static async Task<IReadOnlyList<ControlDeviceMasterDto>> QueryControlDevicesAsync(
        string? siteId,
        QueryMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.QueryControlDevices starting. site_id={SiteId}, active_only={ActiveOnly}, limit={Limit}", siteId, request.ActiveOnly, request.Limit);
        var list = await repository.QueryControlDevicesAsync(siteId, request, cancellationToken);
        logger.LogInformation("MasterFlows.QueryControlDevices completed. count={Count}", list.Count);
        return list;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an equation master row.
    /// </summary>
    public static async Task<EquationMasterDto> CreateEquationAsync(
        CreateEquationMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.CreateEquation starting. equation_key={Key}, version={Version}", request.EquationKey, request.VersionLabel);
        var created = await repository.CreateEquationAsync(request, cancellationToken);
        logger.LogInformation("MasterFlows.CreateEquation completed. equation_master_id={Id}", created.EquationMasterId);
        return created;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates an equation master row.
    /// </summary>
    public static async Task<EquationMasterDto?> UpdateEquationAsync(
        long equationMasterId,
        UpdateEquationMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.UpdateEquation starting. equation_master_id={Id}", equationMasterId);
        var updated = await repository.UpdateEquationAsync(equationMasterId, request, cancellationToken);
        logger.LogInformation("MasterFlows.UpdateEquation completed. equation_master_id={Id}, found={Found}", equationMasterId, updated is not null);
        return updated;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Queries equation masters.
    /// </summary>
    public static async Task<IReadOnlyList<EquationMasterDto>> QueryEquationsAsync(
        QueryMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.QueryEquations starting. limit={Limit}", request.Limit);
        var list = await repository.QueryEquationsAsync(request, cancellationToken);
        logger.LogInformation("MasterFlows.QueryEquations completed. count={Count}", list.Count);
        return list;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a status code master row.
    /// </summary>
    public static async Task<StatusCodeMasterDto> CreateStatusCodeAsync(
        CreateStatusCodeMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.CreateStatusCode starting. status_code={StatusCode}", request.StatusCode);
        var created = await repository.CreateStatusCodeAsync(request, cancellationToken);
        logger.LogInformation("MasterFlows.CreateStatusCode completed. status_code_id={Id}", created.StatusCodeId);
        return created;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a status code master row.
    /// </summary>
    public static async Task<StatusCodeMasterDto?> UpdateStatusCodeAsync(
        long statusCodeId,
        UpdateStatusCodeMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.UpdateStatusCode starting. status_code_id={Id}", statusCodeId);
        var updated = await repository.UpdateStatusCodeAsync(statusCodeId, request, cancellationToken);
        logger.LogInformation("MasterFlows.UpdateStatusCode completed. status_code_id={Id}, found={Found}", statusCodeId, updated is not null);
        return updated;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Queries status code masters.
    /// </summary>
    public static async Task<IReadOnlyList<StatusCodeMasterDto>> QueryStatusCodesAsync(
        QueryMasterRequest request,
        MasterRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("MasterFlows.QueryStatusCodes starting. active_only={ActiveOnly}, limit={Limit}", request.ActiveOnly, request.Limit);
        var list = await repository.QueryStatusCodesAsync(request, cancellationToken);
        logger.LogInformation("MasterFlows.QueryStatusCodes completed. count={Count}", list.Count);
        return list;
    }
}
