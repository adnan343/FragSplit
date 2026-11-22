# Delivery plan
The requirements document governs features and acceptance. Build a single ASP.NET Core application and a separately deployable Next.js storefront over PostgreSQL.
1. Foundation – establish the schema, seed data, migration, auth, configuration, Compose, CI and frontend shell. Check API startup and the web build.
2. Catalog – implement discovery, product editing, image storage and stock adjustments. Check catalog sorting, authorization and validation.
3. Checkout – persist variant carts, quote from the database, lock inventory and create snapshot orders. Test conflicts, retry, COD collection and cancellation.
4. Payments – implement hosted sessions, server verification, expiry and reconciliation. Exercise retries against a test-only adapter and record merchant sandbox evidence separately.
5. Analytics and polish – aggregate realized sales in Dhaka time, finish discovery, metadata, responsive controls and accessibility. Check metrics and browser flows.
6. Hardening – run migration, build, lint, xUnit and Playwright checks. Document operations, remaining blockers and the launch checklist.
