using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ModelEvaluator.ApiComparison.Core.Infrastructure;

public static class Hashing
{
    public static string Sha256(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash);
    }

    public static string Sha256(object value) =>
        Sha256(JsonSerializer.Serialize(value, JsonDefaults.Read));

    public static string Short(string value, int length = 12) =>
        value[..Math.Min(length, value.Length)];
}
