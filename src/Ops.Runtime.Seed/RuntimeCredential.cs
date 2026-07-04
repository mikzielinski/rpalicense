namespace Ops.Runtime.Seed;

internal static partial class RuntimeCredential
{
    private static ReadOnlySpan<byte> Mask =>
        System.Text.Encoding.UTF8.GetBytes("OpsRuntimeSeed2026");

    internal static string? TryRead()
    {
        if (Payload.Length == 0)
        {
            return null;
        }

        var mask = Mask;
        var chars = new char[Payload.Length];
        for (var i = 0; i < Payload.Length; i++)
        {
            chars[i] = (char)(Payload[i] ^ mask[i % mask.Length]);
        }

        var value = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
