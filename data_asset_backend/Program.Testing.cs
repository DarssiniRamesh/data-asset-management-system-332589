/// <summary>
/// Test hook required by Microsoft.AspNetCore.Mvc.Testing.
/// </summary>
/// <remarks>
/// Minimal API projects using top-level statements generate an internal Program class.
/// Declaring it as partial allows the test project to reference it via WebApplicationFactory.
/// </remarks>
public partial class Program;
