using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Content.Server._Solreign.PublicExport;

internal static class PublicExportSigner
{
    private const int MinimumKeyLength = 32;

    internal static PublicExportSignedResult Sign(PublicEnvelopeV1 envelope, ReadOnlySpan<byte> key)
    {
        var canonicalBody = PublicExportCanonicalization.Canonicalize(envelope);
        if (key.Length < MinimumKeyLength)
            throw new PublicExportSigningException(PublicExportSigningCode.InvalidKeyMaterial);

        var hashBytes = SHA256.HashData(canonicalBody.AsSpan());
        var bodyHash = Convert.ToHexStringLower(hashBytes);
        var signingInput = string.Join('\n',
            "SOLREIGN-V1",
            PublicExportCanonicalization.FormatUtc(envelope.PublishedAt),
            envelope.PublisherId,
            envelope.StreamId,
            envelope.Sequence.ToString(CultureInfo.InvariantCulture),
            bodyHash);
        var signatureBytes = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signingInput));
        var signature = Convert.ToBase64String(signatureBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return new PublicExportSignedResult(canonicalBody, bodyHash, signingInput, signature);
    }
}
