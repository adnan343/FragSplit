\set ON_ERROR_STOP on
BEGIN;

LOCK TABLE "Perfumes", "Movements", "Lines", "SavedCartLines" IN SHARE ROW EXCLUSIVE MODE;
CREATE TEMP TABLE demos ON COMMIT DROP AS
SELECT p."Id"
FROM "Perfumes" p
JOIN "Movements" m ON m."PerfumeId" = p."Id"
WHERE p."Slug" IN ('amber-dusk', 'bergamot-veil', 'cedar-archive', 'petal-hour',
                  'saffron-smoke', 'green-reverie')
  AND p."Tags" = 'Demo collection'
  AND m."Actor" = 'seed' AND m."Reason" = 'Demo opening stock'
  AND m."Key" = ('seed:' || p."Id");

WITH removed AS (
    DELETE FROM "SavedCartLines" WHERE "PerfumeId" IN (SELECT "Id" FROM demos)
    RETURNING "AccountId"
)
UPDATE "Accounts" SET "CartVersion" = gen_random_uuid()
WHERE "Id" IN (SELECT "AccountId" FROM removed);

-- Keep any product still referenced by an order or a later ledger movement.
CREATE TEMP TABLE removable ON COMMIT DROP AS
SELECT d."Id" FROM demos d
WHERE NOT EXISTS (SELECT 1 FROM "Lines" l WHERE l."PerfumeId" = d."Id")
  AND NOT EXISTS (
    SELECT 1 FROM "Movements" m WHERE m."PerfumeId" = d."Id"
      AND (m."OrderId" IS NOT NULL OR m."Key" <> ('seed:' || d."Id"))
  )
  AND EXISTS (
    SELECT 1 FROM "Perfumes" p WHERE p."Id" = d."Id" AND p."ReservedMl" = 0
  );

ALTER TABLE "Movements" DISABLE TRIGGER immutable_stock_ledger;
DELETE FROM "Movements" WHERE "PerfumeId" IN (SELECT "Id" FROM removable);
ALTER TABLE "Movements" ENABLE TRIGGER immutable_stock_ledger;
DELETE FROM "Perfumes" WHERE "Id" IN (SELECT "Id" FROM removable);
UPDATE "Perfumes" SET "Active" = false, "Featured" = false
WHERE "Id" IN (SELECT "Id" FROM demos) AND ("Active" OR "Featured");

SELECT (SELECT count(*) FROM removable) AS deleted_demo_listings,
       ((SELECT count(*) FROM demos) - (SELECT count(*) FROM removable)) AS archived_demo_listings;
COMMIT;
