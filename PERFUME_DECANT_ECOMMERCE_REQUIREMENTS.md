# Perfume Decant E-Commerce — Product Requirements & Codex Build Instructions

> **Purpose:** Build a production-ready, single-vendor perfume decant e-commerce platform for customers in Bangladesh. This document is the primary implementation specification for Codex.
>
> **Instruction to Codex:** Implement the application, not just a mockup. Prioritize a secure, functioning end-to-end purchase flow, accurate milliliter-based inventory, and a polished, responsive storefront. Follow the phased plan and acceptance criteria below. Where unspecified, choose sensible defaults, document assumptions, and avoid blocking on minor design decisions.

## 1. Product vision

Create a premium, fast, accessible, Apple-inspired online perfume decant store. Customers can discover fragrances, choose **3 ml, 5 ml, 10 ml, 15 ml, or 30 ml** decants, add multiple variants to a cart, and check out using **SSLCommerz** (including supported card and bKash payment methods) or **cash on delivery (COD)**. A secure admin panel lets the store owner manage perfume listings, product imagery, stock, orders, and sales analytics.

This is a **single-vendor** store: no seller onboarding, marketplace commissions, or multi-tenant architecture.

### Market and defaults
- Target market: Bangladesh; currency **BDT (৳)**.
- English-first interface, with architecture allowing Bangla localization later.
- Configurable delivery areas, delivery charges, and COD eligibility.
- All prices and totals calculated on the server in BDT; never trust client-supplied prices.
- Brand/store name, logo, support contact, shipping policy, and return policy must be configurable rather than hard-coded.

## 2. Technology stack and repository

- **Frontend:** Next.js (App Router), React, TypeScript, Tailwind CSS, shadcn/ui, Framer Motion (or Motion for React), React Hook Form + Zod, TanStack Query where appropriate.
- **Backend:** ASP.NET Core Web API using a currently supported stable .NET release; C#; Entity Framework Core; FluentValidation or equivalent; OpenAPI/Swagger.
- **Database:** PostgreSQL; EF Core migrations.
- **Authentication:** ASP.NET Core Identity or equivalent secure .NET authentication with role-based authorization; `Admin` and `Customer` roles. Support guest checkout.
- **Payments:** SSLCommerz hosted checkout and server-side verification; COD.
- **Analytics:** Recharts for admin charts, backed by server-generated metrics.
- **Images:** S3-compatible object storage abstraction, with local development option; image resizing/optimization and Next.js image component.
- **Infrastructure:** Docker and Docker Compose for local development; environment-based configuration, health checks, structured logging, and CI.
- **Testing:** xUnit for backend unit/integration tests; Playwright for critical browser flows; frontend component tests where useful.

Suggested monorepo layout:

```text
/
  apps/
    web/                 # Next.js storefront and admin UI
    api/                 # ASP.NET Core Web API
  tests/
    api-unit/
    api-integration/
    e2e/
  infra/
    docker-compose.yml
  docs/
    architecture.md
    api.md
    deployment.md
  .env.example
  README.md
```

Use clean, maintainable modular architecture rather than unnecessary microservices. Keep domain logic and payment integrations isolated behind interfaces.

## 3. Storefront requirements

### 3.1 Homepage
- Refined hero section with store value proposition, premium imagery, and clear “Shop fragrances” CTA.
- Featured fragrances, new arrivals, best sellers, and category discovery sections.
- Categories: **Middle Eastern**, **Designer**, and **Niche**.
- Trust/support content: authentic fragrances, decant sizing explanation, delivery/payment information (only make claims the business can substantiate).
- Subtle entrance and hover animations; no distracting autoplay effects.

### 3.2 Product catalog
- Responsive product grid with image, brand, perfume name, category, starting decant price, availability, and optional notes/concentration.
- Category filters: Middle Eastern, Designer, Niche.
- Search by perfume name, brand, and relevant keywords; case-insensitive; debounce text input; provide clear empty states.
- Sort by newest, price low-to-high, price high-to-low, name A–Z, and best selling. Sort price consistently using the lowest *available* size price.
- Filter by brand, availability, and price range if feasible.
- Query parameters must preserve filter/search/sort state and support shareable URLs.
- Server-side pagination (or cursor pagination) and stable sorting.
- Product slugs and SEO-friendly URLs.

### 3.3 Product details
- Product gallery with optimized images and alt text.
- Name, brand, category, fragrance description, notes (top/heart/base), concentration (EDT/EDP/Extrait/etc.), gender/marketing profile if supplied, and optional size-related guidance.
- **Selectable decant sizes: 3, 5, 10, 15, 30 ml.** Each size has its own configurable price and availability. Do not assume linear pricing.
- Quantity selector, live subtotal, stock/availability messaging, add-to-cart CTA, and related products.
- If insufficient liquid stock remains for a size, prevent ordering that quantity and show a useful message.
- Support inactive size options (e.g., a product can offer only some of the five sizes).
- Do not promise original manufacturer bottles for decants; show accurate decant packaging information.

### 3.4 Cart
- Add, remove, and update quantities by **product + decant size**.
- Cart drawer or mini-cart plus full cart page.
- Persist guest cart in browser storage; optionally sync to account when signed in.
- Revalidate current prices, variant availability, and stock on checkout; show changes clearly.
- Show line items, subtotal, delivery fee estimate, discounts if implemented, and total.
- Empty-cart state and easy continue-shopping link.

### 3.5 Checkout
- Support guest checkout; account registration is optional, not required.
- Save signed-in customers’ delivery details on successful checkout for their next order.
- Collect full name, mobile number, email (required, default to the signed-in account email), full delivery address, city/district, area, postal code if applicable, and delivery notes.
- Bangladesh-friendly phone validation without rejecting valid numbers unnecessarily.
- Delivery method/region and charge calculation from server-configured rules.
- Payment options: **Online via SSLCommerz** and **Cash on Delivery**.
- Explicit order summary, shipping fee, final total, terms/policy acknowledgment, and confirmation step.
- Prevent accidental duplicate submissions; use idempotency keys.
- Order confirmation page with human-readable order number, status, items, payment method, and delivery details.
- Provide a secure guest order lookup using order reference plus appropriate verification (e.g., phone OTP or signed token), not just an enumerable order ID.

### 3.6 Customer account (secondary priority)
- Optional sign-up/sign-in, profile and saved addresses, order history and order details.
- New customer passwords at registration and reset require at least eight characters, including uppercase and lowercase letters, a number, and a special character.
- Password reset and session management if accounts are implemented.

## 4. Admin dashboard

All admin routes require authenticated `Admin` authorization **enforced in the API**, not just hidden in the UI.

### 4.1 Overview and metrics
- KPI cards: gross sales, net sales after refunds (if supported), paid orders, COD orders, pending orders, units/decants sold, and low-stock perfumes.
- Time range selector: today, last 7 days, last 30 days, custom range.
- Revenue and order trends; best-selling fragrances; sales by category; sales by size.
- Clearly distinguish paid online revenue, COD orders awaiting collection, cancelled orders, and refunds; do not count unpaid/pending payments as realized revenue.
- Use consistent reporting timezone (**Asia/Dhaka**) and document metrics definitions.

### 4.2 Product management
- Create, edit, archive/unpublish, and view listings.
- Fields: perfume name, slug, brand, category, description, fragrance notes, concentration, images, tags, active/featured flags, SEO metadata.
- Upload multiple images, set primary image, reorder and remove images.
- Configure prices and enabled state for each decant size (3/5/10/15/30 ml).
- Product form validation, image size/type checks, and safe file storage.
- Avoid destructive hard deletes of products referenced by orders; use archive/soft delete.

### 4.3 Inventory
- **Track perfume liquid inventory in milliliters**, not just the number of bottles or SKUs.
- Store `on_hand_ml`, `reserved_ml`, and `available_ml = on_hand_ml - reserved_ml` per perfume, with optional safety buffer and low-stock threshold.
- Support restock, manual correction, damage/spillage, and stock history with timestamp, admin, reason, and quantity delta.
- Optional packaging inventory (empty atomizers by size) is a future enhancement; design for extension without making it a launch blocker.
- Dashboard inventory view: available ml, estimated possible decants per selected size, low-stock alerts, out-of-stock products.
- Inventory changes must be transactional, concurrency-safe, and auditable.

### 4.4 Order management
- Search/filter orders by reference, date, status, payment method, customer name/phone.
- View customer, shipping address, purchased products/sizes, totals, payment status, and status history.
- Fulfillment states: `Pending`, `Confirmed`, `Processing`, `Shipped`, `Delivered`, `Cancelled`, `Returned` (define valid transitions).
- Payment states independent of fulfillment: `Unpaid`, `Pending`, `Paid`, `Failed`, `Refunded`, `PartiallyRefunded` where supported.
- Admin actions to confirm COD, mark processing/shipped/delivered, add tracking reference, cancel, and record manual COD collection status.
- Require reason and audit entry for cancellation/refund/stock correction.
- CSV export for orders and sales metrics (optional for initial MVP).

### 4.5 Store settings
- Store branding, contact information, delivery rates/rules, free-shipping threshold if desired, COD eligibility, low-stock thresholds, and homepage featured products.
- Never expose payment credentials in admin responses or browser code.

## 5. Inventory and reservation rules (critical)

An order line for `quantity = q` of `size_ml = s` consumes **`q × s` milliliters** from the corresponding perfume. Example: 2 × 10 ml = 20 ml.

Implement a transaction-safe reservation mechanism:
1. On checkout/order creation, atomically validate available liquid and reserve the required ml for each perfume, preventing overselling across simultaneous orders. Use row locks or optimistic concurrency with retries.
2. An unpaid online order holds stock for a configurable short period (e.g., 15 minutes). If payment fails or expires, release the reservation exactly once.
3. After verified online payment, convert the reservation to committed stock consumption exactly once. If payment confirmation arrives after reservation expiry, do **not** silently accept an unfulfillable order; reconcile and flag for admin action/refund.
4. For COD, reserve stock on order submission; commit consumption when the order is confirmed/accepted (choose and document the exact business rule). Release reservations on rejection/cancellation before commitment. For cancellations after commitment, restore sellable stock only if physically recoverable and explicitly approved.
5. Maintain an append-only inventory movement ledger for all reserve, release, consume, restock, and correction operations; make retries idempotent.
6. Ensure `available_ml >= 0` and avoid floating-point arithmetic for stock (use integer milliliters). Use decimal/numeric types for money.
7. Orders preserve snapshots of product name, size, unit price, and other purchase-time details even if listings later change.

## 6. SSLCommerz payment integration (critical)

Implement the provider's **currently documented Bangladesh integration flow**, confirming exact endpoints, field requirements, sandbox/production differences, and supported channels from official SSLCommerz documentation before coding. Do not hard-code undocumented assumptions.

- Configure `SSLCOMMERZ_STORE_ID`, `SSLCOMMERZ_STORE_PASSWORD`, sandbox/production mode, and callback base URL via secure server-side environment variables.
- The backend creates an order and initiates a hosted checkout session with a **server-calculated** amount and unique transaction identifier.
- Redirect customers to the provider-hosted payment page. Supported payment methods (including cards and bKash) depend on the merchant account/provider configuration; do not promise a method unless enabled.
- Implement success, failure, cancel, and **IPN/webhook** endpoints according to official provider guidance.
- **Never mark an order paid solely from a browser redirect or unverified callback.** Validate with the provider's transaction validation API and compare amount, currency, transaction/order identity, and expected state.
- Callbacks can be duplicated, delayed, or arrive out of order: use idempotent handlers, unique constraints, and transaction-safe state changes.
- Keep full card data out of the application. Never store card numbers/CVV.
- Record provider transaction ID, verification result, status, timestamps, and non-sensitive diagnostic metadata.
- Show useful payment pending/failed/cancelled screens and safe retry behavior without double-charging.
- Use HTTPS in production and a reachable callback URL for sandbox testing; document local tunneling if necessary.
- Provide a mock payment adapter **only for local automated tests**, clearly separated from live/sandbox integration.

### COD rules
- COD creates an order with payment method `COD` and payment state `Unpaid`, not `Paid`.
- Allow admin to confirm and fulfill the order; track collection separately on delivery.
- COD availability and shipping fees must follow configurable rules.

## 7. Data model (suggested)

Use EF Core migrations, explicit foreign keys, indices, constraints, and UTC timestamps in persistence.

| Entity | Core fields |
|---|---|
| `Users` | id, email/phone, password hash (Identity-managed), role, created_at |
| `Brands` | id, name, slug |
| `Categories` | id, name, slug; seed Middle Eastern, Designer, Niche |
| `Perfumes` | id, brand_id, category_id, name, slug, description, notes, concentration, active, featured, created_at, updated_at |
| `PerfumeImages` | id, perfume_id, storage_key, alt_text, sort_order, is_primary |
| `DecantVariants` | id, perfume_id, size_ml (restricted to 3/5/10/15/30), price_bdt, enabled |
| `PerfumeInventory` | perfume_id, on_hand_ml, reserved_ml, low_stock_threshold_ml, row_version/concurrency token |
| `InventoryMovements` | id, perfume_id, order_id nullable, movement_type, delta_on_hand_ml, delta_reserved_ml, reason, actor_id, created_at, idempotency_key |
| `Carts` / `CartItems` | optional server-side carts for accounts; perfume_id, variant_id, quantity |
| `Orders` | id, public_reference, customer details, shipping snapshot, subtotal, shipping, discount, total, payment_method, payment_status, fulfillment_status, timestamps, idempotency_key |
| `OrderItems` | order_id, perfume_id nullable, variant_id nullable, product/brand snapshot, size_ml, quantity, unit_price, line_total |
| `StockReservations` | id, order_id, perfume_id, reserved_ml, state, expires_at |
| `PaymentTransactions` | id, order_id, gateway, transaction_id, session_id, amount, currency, state, verification metadata, timestamps |
| `OrderStatusHistory` | order_id, previous/new states, reason, actor_id, timestamp |
| `ShippingRules` | region/zone, charge, COD eligibility, active |
| `StoreSettings` | configurable public branding/policy/shipping settings |
| `AuditLogs` | admin actor, action, entity, identifier, timestamp, safe change summary |

Add uniqueness constraints for product slugs, variant `(perfume_id, size_ml)`, order reference, payment transaction identifiers, and idempotency keys. Add search/filter indices. Use appropriate precision for BDT (e.g., `numeric(12,2)`).

## 8. API surface (illustrative, versioned)

Public:
- `GET /api/v1/categories`
- `GET /api/v1/brands`
- `GET /api/v1/perfumes?search=&category=&brand=&sort=&page=&pageSize=`
- `GET /api/v1/perfumes/{slug}`
- `POST /api/v1/checkout/quote` — server-validated totals and availability
- `POST /api/v1/orders` — idempotent order creation
- `POST /api/v1/payments/sslcommerz/initiate`
- `POST /api/v1/payments/sslcommerz/ipn`
- Provider-specific success/fail/cancel callback endpoints as documented
- `GET /api/v1/orders/{reference}/status` — authenticated or signed/verified guest access

Admin (all require `Admin` role):
- `GET/POST /api/v1/admin/perfumes`
- `GET/PATCH /api/v1/admin/perfumes/{id}`
- `POST /api/v1/admin/perfumes/{id}/images`
- `GET/POST /api/v1/admin/inventory/{perfumeId}/movements`
- `GET /api/v1/admin/inventory`
- `GET /api/v1/admin/orders`
- `GET /api/v1/admin/orders/{id}`
- `PATCH /api/v1/admin/orders/{id}/status`
- `GET /api/v1/admin/analytics/overview?from=&to=`
- `GET /api/v1/admin/analytics/sales?from=&to=`
- `GET/PATCH /api/v1/admin/settings`

Exact API routes may evolve; keep OpenAPI and frontend client synchronized. Use structured validation errors and appropriate HTTP status codes.

## 9. Visual design and UX

**Design direction:** Apple-inspired clarity and restraint, not a copy of Apple's branding, assets, or website. Premium perfume editorial photography, generous whitespace, crisp typography, subtle depth, high-quality product imagery, and quiet motion.

- Palette: warm off-white/white backgrounds, charcoal text, soft gray borders, one understated accent (e.g., muted bronze). Provide a cohesive dark mode only if it does not delay MVP.
- Typography: modern sans-serif such as Geist or Inter; strong hierarchy, comfortable reading sizes.
- Layout: mobile-first, responsive, polished desktop navigation and mobile menu.
- Cards: clean photography, restrained radius/shadows, readable prices, clear decant size options.
- Motion: 150–300 ms hover/transition effects, subtle fade/slide reveals, cart drawer animation, skeleton loaders; honor `prefers-reduced-motion`.
- Usability: sticky header, prominent search, visible cart count, straightforward checkout, clear feedback/toasts, actionable validation and error states.
- Accessibility: semantic HTML, keyboard navigation, visible focus rings, sufficient contrast, accessible dialogs/forms, labeled inputs, alt text, and WCAG 2.2 AA as a target.
- Do not overuse glassmorphism, excessive blur, parallax, huge animations, or layout shifts.
- Product photography should use actual uploaded assets. Seed/demo assets must be clearly marked placeholders; do not fabricate product photos or authenticity claims.

## 10. Security, privacy, and reliability

- Validate and authorize every sensitive API operation; apply least privilege.
- Secure session/cookie design, CSRF protection where cookie authentication is used, CORS restricted to approved origins, rate limiting for login/checkout/search, and secure headers.
- Passwords hashed by ASP.NET Identity; no plaintext secrets; production admin credentials provisioned securely (never ship a default password).
- Validate image MIME type, size, and actual file contents; restrict uploads; avoid executable file serving.
- Prevent SQL injection via EF Core parameterization; sanitize or safely render user-supplied rich text.
- Protect customer PII; redact sensitive fields in logs; document retention and access controls.
- Use structured logs, exception handling, correlation IDs, health checks, and payment/inventory audit trails.
- Use HTTPS, secure secret management, database backups, migration/rollback instructions, and deployment-ready configuration.
- Implement clear failure recovery for payment initiation, webhook verification, stock conflicts, and expired reservations.
- Never trust cart totals, discount values, stock, roles, or payment state submitted by the browser.

## 11. Performance and SEO

- Use Next.js server rendering/static optimization where appropriate; ensure product/category pages are crawlable.
- Per-product metadata, canonical URLs, sitemap, robots.txt, Open Graph tags, and relevant structured product data without misleading stock/price claims.
- Optimize images, lazy-load below-the-fold assets, avoid oversized JS bundles, and paginate listings.
- Target strong Core Web Vitals, especially on midrange mobile devices and slower connections.

## 12. Testing and acceptance criteria

Automate critical scenarios and provide a manual test checklist:

1. Admin logs in; non-admin cannot access admin APIs.
2. Admin creates a perfume with multiple images, category, stock, and five configurable decant sizes/prices.
3. Customers can search, filter by all three categories, sort, and navigate to product details.
4. Customers can add different sizes of the same perfume to cart and adjust quantities.
5. Checkout computes all amounts on the server, including delivery charges.
6. Two concurrent checkouts cannot reserve more ml than are available.
7. A 10 ml purchase consumes exactly 10 ml; two 5 ml purchases consume exactly 10 ml.
8. Failed/expired online payments release reservations exactly once.
9. Duplicate SSLCommerz callbacks cannot mark an order paid twice or double-decrement inventory.
10. Unverified success redirects do not change an order to `Paid`.
11. Verified successful sandbox payment creates a correctly paid order and confirmation page.
12. COD order remains unpaid until collection is recorded; admin can manage fulfillment.
13. Cancelled orders follow documented reservation/stock restoration rules.
14. Analytics reflect only appropriate order/payment states and accurately group sales by date, category, product, and size.
15. Product updates do not change historical order snapshots.
16. Storefront and admin are usable on small mobile screens and desktop; keyboard navigation and reduced-motion settings work.
17. No secrets appear in frontend bundles, source control, API responses, or logs.
18. App boots with documented local commands, migrations, seeded sample data, and working tests.

## 13. Implementation phases for Codex

### Phase 1 — Foundation
- Initialize monorepo, Next.js frontend, ASP.NET Core API, PostgreSQL, Docker Compose, lint/format/test setup, configuration, migrations, and basic CI.
- Define domain entities, seed categories and sample perfumes, establish OpenAPI contract.
- Build a coherent design system and responsive app shell.

### Phase 2 — Catalog and admin products
- Implement product/brand/category APIs, search/filter/sort/pagination, product detail pages, images, and admin CRUD.
- Implement decant variant pricing and liquid inventory with audited movements.

### Phase 3 — Cart, checkout, and COD
- Implement guest cart, checkout form, delivery rules, transactional reservations, order creation, COD workflow, order confirmation, and admin order management.

### Phase 4 — SSLCommerz
- Implement provider adapter, sandbox checkout, callback/IPN verification, idempotency, reconciliation, expired reservation handling, and payment status UI.
- Verify with sandbox credentials and provider documentation; do not claim live payment functionality without live merchant credentials and real validation.

### Phase 5 — Analytics and polish
- Implement admin KPI/charts, low-stock dashboard, SEO, accessibility, motion, responsive refinements, and performance improvements.

### Phase 6 — Hardening and deployment
- Complete automated tests, security review, production config, monitoring/logging, backups, deployment guide, and go-live checklist.

## 14. Deliverables

Codex must produce:
- Working frontend, backend, and database migrations.
- Fully usable storefront and protected admin dashboard.
- Seed script with clearly fictional/demo perfumes, test accounts created securely for local development only, and configurable product images.
- `.env.example` with placeholder keys and comments (no real credentials).
- `README.md` with prerequisites, quick start, local URLs, setup, and test commands.
- `docs/architecture.md`, `docs/api.md`, `docs/deployment.md`, and payment sandbox setup instructions.
- Unit, integration, and end-to-end tests covering the critical flows.
- A final implementation summary listing completed features, known gaps, test results, and exact commands to run.

## 15. Operating instructions for Codex

1. Inspect the repository before changing anything. If empty, scaffold from scratch.
2. Create a short implementation plan and checklist. Execute the phases in order, keeping the app runnable after each phase.
3. Favor real, maintainable implementations over static mock data, decorative-only UI, or placeholder buttons.
4. Keep secrets and environment-specific settings out of source code. Never invent or embed SSLCommerz merchant credentials.
5. Check the official SSLCommerz integration documentation before implementing payment details; if documentation or credentials are unavailable, isolate the integration, clearly mark what remains unverified, and continue with the rest of the app.
6. Make database and payment operations idempotent and concurrency-safe. Treat stock correctness and payment verification as release blockers.
7. Run migrations, type checks, linting, builds, and tests as available; fix failures rather than merely reporting them.
8. Keep docs updated with decisions, assumptions, and actual commands.
9. If an external dependency prevents full completion, implement a safe interface/test double, document the blocker, and do not misrepresent the integration as production-ready.
10. At the end of each phase, summarize what was implemented, what was tested, and what remains.

## 16. Explicit non-goals for initial release

- Multi-vendor marketplace or seller dashboards.
- Native iOS/Android apps.
- Subscription billing or loyalty program.
- Advanced recommendations, AI chatbots, or social features.
- International shipping and multi-currency checkout.
- Automated courier integrations (can be added later).
- Complex discount/promo engine (unless added after core checkout is stable).

## 17. Decisions to confirm before launch

These are configuration/business decisions, not blockers to building the core system:
- Store name, logo, domain, contact channels, and legal/policy text.
- Exact shipping regions/rates and COD restrictions.
- Whether all five sizes are offered for every perfume or selectively enabled (default: selective).
- Packaging type, minimum stock buffer, and low-stock alert threshold.
- SSLCommerz sandbox and production merchant credentials and enabled channels.
- Hosting provider, object storage provider, and transactional email/SMS provider.
- Return/refund policies and any applicable taxes/fees.

**Definition of done:** A customer can discover a perfume, select an available decant size, complete a COD order or a verified SSLCommerz sandbox payment, and receive an accurate confirmation; an admin can manage listings, monitor real ml-based inventory, process orders, and view correct sales metrics. All flows work on mobile and desktop and pass the critical acceptance tests.
