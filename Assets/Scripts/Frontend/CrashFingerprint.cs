using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace BumperTanks.Frontend
{
    /// <summary>Converts an exception type and stack frame signature into a non-reversible upload identifier.</summary>
    public static class CrashFingerprint
    {
        public static string Create(string condition, string stackTrace)
        {
            var firstLineEnd = condition != null ? condition.IndexOfAny(new[] { '\r', '\n' }) : -1;
            var firstLine = firstLineEnd >= 0 ? condition.Substring(0, firstLineEnd) : condition ?? string.Empty;
            var colon = firstLine.IndexOf(':');
            var typeCandidate = (colon >= 0 ? firstLine.Substring(0, colon) : firstLine).Trim();
            if (colon < 0 && typeCandidate.IndexOfAny(new[] { ' ', '\t' }) >= 0)
                typeCandidate = typeCandidate.Substring(0, typeCandidate.IndexOfAny(new[] { ' ', '\t' }));
            var exceptionType = typeCandidate.EndsWith("Exception", StringComparison.Ordinal)
                ? typeCandidate
                : "UnknownException";

            var signature = new StringBuilder(exceptionType);
            if (!string.IsNullOrEmpty(stackTrace))
            {
                foreach (var rawLine in stackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var frame = rawLine.Trim();
                    var sourcePath = frame.IndexOf(" in ", StringComparison.Ordinal);
                    if (sourcePath >= 0) frame = frame.Substring(0, sourcePath);
                    var lineMarker = frame.LastIndexOf(":line ", StringComparison.Ordinal);
                    if (lineMarker >= 0) frame = frame.Substring(0, lineMarker);
                    if (frame.Length > 0) signature.Append('\n').Append(frame);
                }
            }

            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()))).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
