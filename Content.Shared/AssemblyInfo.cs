using System.Runtime.CompilerServices;

// Same shape as Content.Server/AssemblyInfo.cs and Content.Client/AssemblyInfo.cs. First needed
// here by SolreignFxCueV1's test-only internal constructor overload (lets SchemaVersion vary —
// see its doc comment), but this opens ALL of Content.Shared's `internal` members to these two
// assemblies, not just that one constructor (grk adversarial review, low-severity scope note).
// That blast radius is the established repo-wide convention, not a deviation.
[assembly: InternalsVisibleTo("Content.Tests")]
[assembly: InternalsVisibleTo("Content.IntegrationTests")]
