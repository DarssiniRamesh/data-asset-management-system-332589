using System.Runtime.CompilerServices;

// Allow the test project to use internal helpers like DatabaseUrlParser without widening public API surface.
[assembly: InternalsVisibleTo("DataAssetBackend.Tests")]
