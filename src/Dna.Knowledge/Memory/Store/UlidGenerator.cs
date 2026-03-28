using System.Security.Cryptography;

namespace Dna.Memory.Store;

/// <summary>
/// ULID 生成器 — 时间有序、全局唯一。
/// 格式：10 字符时间戳（48bit, ms 精度）+ 16 字符随机（80bit）= 26 字符 Crockford Base32。
/// 比 GUID 的优势：可排序、更短、天然时间有序。
/// </summary>
internal static class UlidGenerator
{
    private const string CrockfordBase32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static string New()
    {
        var timestamp = (long)(DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
        Span<char> result = stackalloc char[26];

        // 10 字符 时间戳部分 (48 bit, big-endian)
        for (var i = 9; i >= 0; i--)
        {
            result[i] = CrockfordBase32[(int)(timestamp & 0x1F)];
            timestamp >>= 5;
        }

        // 16 字符 随机部分 (80 bit)
        Span<byte> randomBytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(randomBytes);

        var bitBuffer = 0UL;
        var bitsInBuffer = 0;
        var charIndex = 10;

        foreach (var b in randomBytes)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5 && charIndex < 26)
            {
                bitsInBuffer -= 5;
                result[charIndex++] = CrockfordBase32[(int)((bitBuffer >> bitsInBuffer) & 0x1F)];
            }
        }

        return new string(result);
    }
}
