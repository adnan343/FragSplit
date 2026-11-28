# API guide

The API serves the machine-readable contract at `/openapi/v1.json` on its internal listener. All routes below are relative to `/api/v1`. JSON uses camel case. Stock uses integer ml and money uses BDT decimals. Failed form validation or state transitions return RFC problem details. A 409 stock or price conflict requires review of a fresh quote.

| Method and path | Operation |
| --- | --- |
| GET /settings, /categories, /brands | Public branding, active delivery rules and discovery lists. The online flag is false for this release. |
| GET /perfumes | Search, category, brand, featured, available, minPrice, maxPrice, page and pageSize. Sort values are newest, price-asc, price-desc, name and best. |
| GET /perfumes/{slug} | Active listing, variants and ordered gallery |
| POST /checkout/quote | Accepts items with perfumeId, sizeMl, quantity, plus zoneId and method COD. Returns prices, delivery, total and quote signature. |
| POST /orders | Accepts basket, customer, accepted, quote signature and UUID requestKey. For account checkout also send the session accountId and current cartVersion. Returns the reference and a protected token for guest orders. Keep the original key until the attempt resolves. A request with a different payload and a committed key receives 409. |
| POST /orders/recover | Recover the receipt for an interrupted submission using its original private UUID key. |
| POST /orders/receipt-link | Accepts a guest order reference and checkout email. Returns 204 for well-formed requests and emails a 30-minute private link for a match. |
| GET /orders/{reference}/status | Customer session for an owned order or X-Order-Token from a guest receipt. |
| POST /auth/login, /auth/logout | Same-origin session actions. Customer login accepts email and password. |
| POST /auth/admin/login | Accepts administrator email, password and six-digit authenticator code. |
| GET /auth/session | Session role, email and customer ID; null fields for guests |
| POST /auth/register, /auth/forgot-password | Accept an email address and send the appropriate instructions to that mailbox. Return 204 after SMTP acceptance. |
| POST /auth/verify-email | Accept an emailed registration token and a new password. Create the customer and issue a session. |
| POST /auth/reset-password | Accept an emailed reset token and a new password. Revoke existing sessions. Legacy accounts also supply their current password or original recovery code in `previousCredential` on first reset. |
| GET, PUT /account/profile | Default delivery name, phone and address |
| GET, PUT /account/cart | Read lines and cartVersion; write accountId, version, items and optional merge flag. A stale revision returns 409. |
| GET /account/orders | Newest first, 25 entries per page; use before with the last order ID for older entries |
| POST /account/orders/{reference}/claim | Attach a guest receipt to the signed-in customer using X-Order-Token |
| POST /account/sign-out-all | Invalidate the customer’s sessions across devices |
| GET /admin/me | Current authorized administrator |
| GET, POST /admin/perfumes | List or create listings. POST accepts optional openingStockMl (whole ml, 0–1,000,000, default 0). Positive stock records an opening restock in the creation transaction. |
| GET, PATCH /admin/perfumes/{id} | Get or update all editable listing fields and the five variants. Set active=false to archive. |
| POST, PUT /admin/perfumes/{id}/images | Multipart image and alt text, or an ordered JSON array of existing image IDs with alt text. Omitted images are detached. |
| DELETE /admin/perfumes/{id}/images/{imageId} | Immediately detach one image. The remaining images keep their order. Repeating the deletion returns 204. |
| GET /admin/inventory | Current balances and threshold per perfume |
| GET, POST /admin/inventory/{id}/movements | Latest 200 movements, or record a signed delta, type, reason and UUID key |
| GET /admin/orders | Search text, status, method, date bounds and page. Page size is 40. |
| GET /admin/orders/{reference} | Customer, immutable lines, state, tracking and history |
| PATCH /admin/orders/{reference}/status | Action, reason, tracking and optional recoverStock approval |
| GET /admin/analytics/overview | Inclusive from/to dates, up to 366 days, all sales metrics and breakdowns |
| GET, PATCH /admin/settings | Update store fields and delivery regions |

Every admin handler checks the Admin role, MFA sign-in, account status and session version. Browser mutations under /admin, /auth and /account and cookie-backed order writes require the X-Shop-Request header set to 1. Requests with an Origin header must match PUBLIC_ORIGIN. The API and web share a browser origin through the ingress proxy. Guest tokens travel in a request header and in the fragment of the private customer link.
