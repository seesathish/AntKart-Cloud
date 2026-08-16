using System.Security.Cryptography;
using System.Text;

namespace AK.Tools.DiscountSeedLoader;

// A STABLE integer hash of a SKU, used to deterministically pick which products get a discount and
// which variant they get (Rule A). MD5 (used purely as a fast, stable hash — not for security) is
// chosen because string.GetHashCode() is RANDOMISED per process in .NET, so it would give different
// results on every run. This implementation is repeatable across runs, machines, and platforms.
public static class SkuHash
{
    public static uint Of(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(sku.Trim().ToUpperInvariant()));
        return BitConverter.ToUInt32(bytes, 0);
    }
}
