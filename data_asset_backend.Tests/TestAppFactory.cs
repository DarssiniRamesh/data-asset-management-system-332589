using Microsoft.AspNetCore.Mvc.Testing;

namespace DataAssetBackend.Tests;

/// <summary>
/// Creates an in-memory test host for the DataAssetBackend minimal API.
/// </summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    // Keep default behavior: the API should start even when DB isn't configured.
    // Tests intentionally avoid assuming database availability.
}
