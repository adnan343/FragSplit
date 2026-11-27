# Implementation and verification results

## VialMuse COD release, 27 November 2026

| Check | Result |
| --- | --- |
| xUnit | 3 unit and 30 PostgreSQL integration tests passed. Coverage includes concurrent COD hold limits, expiry, guest receipt authorization, authenticator replay and administrator session revocation. |
| Chromium | 15 tests passed against the production Compose images. Desktop and Pixel 7 emulation each verified that dropping an accepted checkout response and editing its notes leaves one order and one reservation. |
| Ingress | 144 interleaved quotes succeeded across two shoppers on the staging ingress. Saturating one shopper's quote budget left the other shopper and checkout recovery available. Changing client-supplied headers from an untrusted peer kept a single IP budget. |
| Backup rehearsal | The existing database, uploaded media and data protection keys restored to an isolated stack. The migration kept all 11 orders and five accounts. The API connected as its restricted PostgreSQL role, and the existing administrator signed in with MFA. |
| Email | Checkout confirmation and guest receipt recovery passed with Mailpit. The configured external SMTP server accepted TLS authentication and the sender address. Delivery to an external inbox remains open. |
| Build | Both production images built. TypeScript, ESLint and Prettier passed. The production JavaScript and NuGet dependency audits reported zero known vulnerabilities. |
| Production | The Cloudflare hostname redirected HTTP to HTTPS and passed certificate verification. The live browser checks covered 36 route, viewport and theme combinations, the COD quote, closed gateway routes, administrator MFA and secure sign-out. The storefront had zero seed listings after cleanup. |

Physical-device and screen-reader checks remain open. SSLCommerz's callback, refund and merchant-sandbox checks apply when online payments return. [Payment scope](payments.md)

## Historical verification, 21 November 2026

The following tables record verification of the initial build. Browser tests used separate services and seeded data.

| Phase | Delivered | Verification at the phase boundary |
| --- | --- | --- |
| 1. Foundation | Next.js, .NET 10, EF model and migration, PostgreSQL, seed/bootstrap, configuration and Compose | API compiled, Next production build passed, two xUnit foundation tests passed. |
| 2. Catalog | Searchable catalog, variants, galleries, protected listing editor, media storage, stock adjustments | Four PostgreSQL integration cases passed for the three categories and anonymous authorization. Catalog web build passed. |
| 3. Cart and COD | Persistent variant cart, quote, guest checkout, private confirmation, fulfillment and collection | Seven integration cases passed, including concurrent checkout, order replay and exact ml consumption. Next build passed. |
| 4. Payments | SSLCommerz adapter, browser and IPN handlers, session reuse, expiry, review and refund recording | Five unit and twelve integration cases passed at this phase, with a test-only payment gateway. Actual merchant sandbox acceptance is pending. |
| 5. Reporting and polish | Dhaka reporting, daily trends, category/size/product breakdowns, settings, SEO and responsive controls | Eighteen integration cases passed, including dated refunds, role restrictions and stock recovery. Lint and web build passed. |
| 6. Hardening | Append-only DB ledger trigger, reporting indexes, interrupted checkout recovery, CI, operations guides and manual checklist | Final results below. |

## Final checks

| Check | Result |
| --- | --- |
| xUnit unit tests | 9 passed. Includes hosted checkout form fields, both provider hosts, validation response parsing and multiple-attempt reconciliation using an in-process HTTP handler. |
| xUnit PostgreSQL integration tests | 26 passed against a new PostgreSQL 17 database through the documented test script. |
| Chromium browser tests | 10 passed against production Compose images, covering desktop and Pixel 7 emulation. |
| Production build, TypeScript, ESLint and Prettier | Passed. Both production container images built successfully. |
| Migrations and seed | Applied to fresh databases. Repeated startup preserved data. OpenAPI returned the versioned checkout and admin routes. |
| Recovery drill | Restored a custom-format PostgreSQL backup to a second database. Both had 2 orders, 10 movement entries, 1,150 ml on hand and 22 ml reserved. |
| Public surface | Health, product page, sitemap, robots, storefront and admin sign-in returned HTTP 200. Checked 39 client JavaScript chunks, public JSON and local Compose logs for the generated QA database and admin passwords. Neither value appeared. |

## Acceptance checklist

Use a disposable store for the following checks.

1. Sign in as the configured admin. Check that anonymous requests return 401 and a signed-in customer gets 403 on admin data. The integration suite also checks rejection of a cross-origin admin write.
2. Create a listing, enter its opening liquid stock in ml, set its category and five independent prices, then upload two images. Verify the opening balance and ledger entry before recording another restock with a reason. The browser suite exercises this on desktop and mobile. Move the second image earlier, update its alt text and check the public gallery.
3. Open each category, search by name or notes, sort by price and open a product. Share a filtered URL or revisit with the browser back button. Sold-out and disabled sizes must be unavailable to purchase.
4. Add two sizes of one perfume, reload, and edit their quantities independently. Review the delivery estimate and the server quote including shipping.
5. Reprice a variant between quote and checkout. Confirm rejection of the stale quote with no reservation. Retry identical submissions concurrently and recover the same receipt using the original private key.
6. Submit competing orders for the last 10 ml. Exactly one can reserve it. Confirm separate COD orders for 1 × 10 ml and 2 × 5 ml. Each consumes 10 ml once. Edit the listing and verify the old purchase snapshot.
7. Exercise duplicate verified IPNs, declines, expiry, unverified success redirects, risky charges and a success arriving after expiry. Confirm one stock decision and review for unfulfillable payments. Complete a real merchant sandbox purchase using the payment runbook.
8. Confirm, process, ship and deliver a COD order. The payment stays unpaid until collection. Cancel before and after confirmation and verify the reason, history and explicit physical stock recovery choice.
9. Inspect full refunds and receipts straddling midnight in Dhaka. Confirm the financial, product, size and category groups match the documented definitions. Uncollected COD contributes no receipts.
10. Use keyboard navigation, a small viewport and reduced motion. Open and dismiss the bag, check form feedback and inspect both the admin and storefront. The automated mobile run covers guest and admin journeys, skip navigation, menu activation, viewport overflow and the reduced-motion preference.
11. Inspect public responses and frontend assets for secrets, then rehearse database, media and key recovery with the actual hosting environment.

## Customer accounts
The account browser journey starts with a guest bag, registers a customer, saves a delivery address, checks out in a second independent browser session and opens the resulting order in the first. Integration tests verify ownership restrictions, stale-cart conflicts, private guest receipt claiming and single-use email verification and reset links, legacy account ownership and session revocation.

The browser suite opens the verification and reset links received over SMTP in a disposable Mailpit inbox. It signs in with the replacement password and checks the customer's order history. Delivery to a public mailbox still requires the store's sender credentials and public HTTPS origin.

## Theme verification

Desktop and mobile browser tests cover the device default, live system preference changes, explicit light and dark choices, persistence after reload, the server-rendered theme and the cart drawer. Both themes also passed overflow checks across seven customer and admin entry points at 320 and 1440 px.

## Storefront redesign verification

The production web image passed all eight desktop and mobile Chromium journeys. Playwright CLI also opened the home, catalog, product, account, cart and guide pages at 320, 390, 768, 1024 and 1440 px. All 30 combinations loaded without horizontal overflow or uncaught page errors. The mobile discovery check followed a collection into the filtered catalog, opened the size guide and verified that Escape returns focus to the menu button.

## Remaining release gates and scope

- **Merchant sandbox payment** requires the merchant's SSLCommerz sandbox credentials and a public HTTPS callback origin. The documented API and simulated provider flows were tested. A real charge, its IPN delivery and the merchant's enabled card/bKash channels still need end-to-end verification.
- **Deployment environment** requires a domain, TLS proxy, secret storage and durable backups. Local volume image uploads were exercised. Verify upload/read and recovery on the chosen S3-compatible provider before using it. The checked-in CI workflow has been exercised through its local commands; a hosted CI run needs a repository remote.
- **Business configuration** requires production branding, contacts, policies, delivery rates, product imagery and starting liquid balances. The default content and shipping charges are for demonstration.
- **Optional scope** includes partial refunds, CSV export, packaging inventory and automated courier integrations. Refund recording supports full refunds after external reimbursement. Customer registration, guest checkout and protected admin accounts are implemented.
- Browser coverage uses Chromium desktop and mobile emulation. Physical-device, screen-reader, WebKit and field Core Web Vitals measurements remain launch QA tasks.
