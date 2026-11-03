using System.Security.Cryptography;
using System.Text;

public static class MatchOutcomeAttestation
{
    public const int MinimumSecretBytes = 32;

    public static bool IsConfigured(string? secret) =>
        !string.IsNullOrEmpty(secret) && Encoding.UTF8.GetByteCount(secret) >= MinimumSecretBytes;

    public static string Sign(string secret, MatchCompleteRequest request)
    {
        if (!IsConfigured(secret)) throw new ArgumentException("Match attestation secret must contain at least 32 UTF-8 bytes.", nameof(secret));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Payload(request))).ToLowerInvariant();
    }

    public static bool Verify(string? secret, MatchCompleteRequest request, string? signature)
    {
        if (!IsConfigured(secret) || string.IsNullOrEmpty(signature) || signature.Length != 64) return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature); }
        catch (FormatException) { return false; }

        var expected = Convert.FromHexString(Sign(secret!, request));
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static byte[] Payload(MatchCompleteRequest request) => Encoding.UTF8.GetBytes(
        $"{request.PlayerId}\n{request.MatchId}\n{request.Gems}\n{request.Eliminations}");
}
