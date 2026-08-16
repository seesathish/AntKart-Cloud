# Seed data — product catalogue

`products.csv` is a **committed, deterministic** dataset of **3,000 products** used to populate the
AK.Products catalogue (Azure Cosmos DB, MongoDB API). It is plain data — not a secret — so it is
safe to commit.

## Quick start

Run both commands from the **repository root**:

```bash
# 1. (Optional) Regenerate the dataset — it is already committed.
dotnet run --project AK.Tools/AK.Tools.ProductsSeedGenerator
#    -> writes AK.Seed-Data/products.csv (3,000 rows)

# 2. Load the dataset into Cosmos (idempotent; needs an az-login / managed identity with
#    Key Vault Secrets User + Cosmos access). Safe to run repeatedly.
dotnet run --project AK.Tools/AK.Tools.ProductsSeedLoader
#    -> "Upserted 3000 products (total)."
```

The generator is **deterministic** (regenerating yields byte-identical output) and the loader is
**idempotent** (re-running converges to exactly 3,000 products, never duplicates). Both are detailed
below.

## What's in it

3,000 products, evenly spread across the three top categories — **1,000 Men / 1,000 Women /
1,000 Kids** — with 10 realistic subcategories each (apparel, footwear, and accessories, e.g.
Shirts, Sneakers, Watches / Dresses, Heels, Handbags / Frocks, Sandals, School Bags). Every row has
a realistic name, brand, description, price, sizes, colours, material and stock, and a **unique
SKU** of the form `{CAT}-{SUBCAT}-{NNN}` (e.g. `MEN-SHIR-001`).

Columns (header row): `Sku, Name, Description, Brand, CategoryName, SubCategoryName, Price,
Currency, StockQuantity, Sizes, Colors, Material`. The list fields (`Sizes`, `Colors`) are
**pipe-delimited** (`S|M|L`); fields containing commas (some descriptions) are CSV-quoted. There is
**no id column** — the loader derives each document id deterministically from the SKU.

## Regenerate (deterministic)

The generator uses a fixed RNG seed and a fixed iteration order, so regenerating always produces
byte-identical output:

```bash
dotnet run --project AK.Tools/AK.Tools.ProductsSeedGenerator
# writes AK.Seed-Data/products.csv (3,000 rows)
```

## Load into Cosmos (idempotent, secret-less)

The loader upserts each row into the `products` collection. It is **idempotent**: the document id
is derived from the stable SKU, so re-running converges to exactly 3,000 products and never creates
duplicates. It is **secret-less** — it reuses the Products service's configuration foundation and
reads the Cosmos connection string from **Key Vault** via `DefaultAzureCredential` (no secret in the
tool). Run it with an identity that has Key Vault Secrets User + Cosmos access:

```bash
dotnet run --project AK.Tools/AK.Tools.ProductsSeedLoader
# Upserted 3000 products (total).
```

See [docs/guides/cloud-migration-guide.md](../docs/guides/cloud-migration-guide.md) (Step 7) for the
full design.
