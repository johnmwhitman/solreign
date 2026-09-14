using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace Content.Tests._Solreign;

[TestFixture]
public sealed class PublicExportFixtureTests
{
    [TestCase("server-snapshot-envelope-v1.schema.json", "0a35f9a9af786b2f4bdb1cf128ee93d85950db1263ad0a165ad7013fabbb67fe")]
    [TestCase("server-snapshot-v1-signing-vector.json", "d8e2b02f1b06420ee61a6646cf5dfbbdef4bfb982ab685d7c3e6647baa564bab")]
    [TestCase("prohibited-public-export-v1.json", "9d58d9e09e007fc81f0c4eeda75e96f27f00d27c4df71ecf8ea92b89c1c90af2")]
    public void EmbeddedFixtureParsesAndMatchesFrozenHash(string suffix, string expectedSha256)
    {
        var bytes = LoadFixture(suffix);

        using var document = JsonDocument.Parse(bytes);
        Assert.That(document.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Is.EqualTo(expectedSha256));
    }

    private static byte[] LoadFixture(string suffix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var matches = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
            .ToArray();
        Assert.That(matches, Has.Length.EqualTo(1), $"Expected exactly one embedded resource ending in {suffix}.");

        using var stream = assembly.GetManifestResourceStream(matches[0])
            ?? throw new InvalidOperationException("The selected embedded resource could not be opened.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
