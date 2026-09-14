using System;
using System.Security.Cryptography;
using System.Text;

namespace Content.Server._Solreign.SessionTelemetry;

/// <summary>
///     The account hash for the session-telemetry ledger: 16 lowercase hex chars of
///     HMAC-SHA256(pepper, guid "N" form). Pure so the leak/determinism properties are
///     unit-testable without Robust.
/// </summary>
public static class SessionTelemetryHash
{
    /// <summary>
    ///     Stable longitudinal id for one account under one pepper. Throws on a missing pepper
    ///     rather than degrading to an unkeyed digest — an unkeyed hash of a public GUID is
    ///     reversible by table lookup, so there is no safe output for that input.
    /// </summary>
    public static string AcctHash(string pepper, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(pepper))
            throw new ArgumentException("session telemetry pepper must be non-empty", nameof(pepper));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(userId.ToString("N")));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }
}
