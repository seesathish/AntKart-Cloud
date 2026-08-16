using System.Security.Cryptography;
using System.Text;

namespace AK.Tools.ProductsSeedLoader;

// Derives a STABLE document id from the product's SKU.
//
// WHY: the Product's `Id` maps to Mongo's `_id`, and the Cosmos collection is sharded on
// `{ "_id": "hashed" }`. By deriving `_id` deterministically from the immutable SKU, the same
// SKU always maps to the same document, so the loader's upsert is an IDEMPOTENT, single-partition
// point write — re-running never creates duplicates.
//
// MD5 is used purely as a fast, stable hash (NOT for security): 16 bytes -> 32 lowercase hex
// chars, the same shape as StringEntity's default Guid.ToString("N") id.
public static class DeterministicId
{
    public static string FromSku(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(sku.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
