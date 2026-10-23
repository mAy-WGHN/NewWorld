using System.Security.Cryptography;
using System.Text;

namespace Workforce.Core.Absences;

/// <summary>A GUID derived from a text: same input, same ID (idempotency keys that survive a retry).</summary>
public static class DeterministicGuid
{
    public static Guid From(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
}
