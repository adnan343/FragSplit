# SSLCommerz hosted payments

The merchant sandbox transaction is a release gate. Use test orders and an isolated database. Record the order reference, provider transaction identifier, BDT amount, verification time, stock movement, and outcome for each run. Keep customer information and payment credentials out of the evidence.

## Provider contract

Checked on 21 November 2026. Select the HTTPS origin for the merchant environment. Sandbox uses `sandbox.sslcommerz.com`, production uses `securepay.sslcommerz.com`. Each environment needs its own merchant account. All three calls originate on the API server. See the provider’s [Bangladesh integration reference](https://developer.sslcommerz.com/doc/v4/index.html).

| Purpose | Request | Result to inspect |
| --- | --- | --- |
| Create hosted session | Form POST `/gwprocess/v4/api.php` | `status=SUCCESS`, `sessionkey`, `GatewayPageURL` |
| Verify a successful charge | GET `/validator/api/validationserverAPI.php` with `val_id`, `store_id`, `store_passwd`, `format=json` | `VALID` or `VALIDATED`, merchant transaction ID, validation ID, gross amount, currency, risk |
| Reconcile a session | GET `/validator/api/merchantTransIDvalidationAPI.php` with `sessionkey` or `tran_id`, the merchant credentials, and `format=json` | Session query returns one record. Transaction query returns an `element` array and can contain several payment attempts. |

The initiation form includes `store_id`, `store_passwd`, a unique `tran_id` of up to 30 characters, `total_amount` in the range ৳10.00–৳500,000.00, `currency=BDT`, and absolute `success_url`, `fail_url`, `cancel_url`, and `ipn_url`. Include `cus_name`, `cus_email`, `cus_phone`, `cus_add1`, `cus_city`, `cus_postcode`, `cus_country`, `product_name`, `product_category`, `product_profile=physical-goods`, `shipping_method`, `num_of_item`, and applicable `ship_*` fields. The hosted page lists the channels enabled for that merchant. [Provider parameter tables](https://developer.sslcommerz.com/doc/v4/index.html)

## Verification and recovery

Use these decisions when implementing or reviewing the adapter.

| Event | Decision |
| --- | --- |
| Customer returns or an IPN arrives | Use the notification to locate an existing online payment. Successful payment requires an authenticated call to the validation API. A guest's browser can close before the IPN arrives. |
| Validation succeeds | Match the stored merchant transaction ID, submitted and returned validation IDs, decimal gross `amount`, and BDT currency. Check the original `currency_type` and `currency_amount` when supplied. `store_amount` is the settlement after fees. Gate fulfillment on a safe risk result, a live reservation, and the expected order state. |
| Successful validation after expiry or risky result | Hold fulfillment and send the transaction to reconciliation. Record the verified payment separately from the stock decision. Check the merchant portal and decide whether the order can be fulfilled or refunded. Record the resolution, amount, reason, and operator. |
| Duplicate success, or failure after success | Lock the same order during settlement. Treat an already applied validation ID as a repeat. A different successful charge on an already paid order needs a duplicate-charge review. Preserve the original stock ledger entry. |
| Decline, cancellation, or browser timeout | A browser response alone cannot authorize a release. Query the saved session. A matching terminal failure can release the reservation once. Pending, missing, and ambiguous results wait for confirmation or the local expiry deadline. A transaction ID query can contain both failed and successful attempts. |
| HTTP timeout during initiation | A timeout can occur after the provider created the session. Reconcile the existing transaction ID before offering another payment. Reuse an existing eligible hosted session when possible. |

For gateway diagnostics retain only identifiers, timestamps, status, and sanitized reason codes. Validation queries carry the merchant password in the URL. Disable URI and response-body logging for that HTTP client, reverse proxies, tracing, and exception reporting. Keep TLS certificate verification enabled.

## Sandbox acceptance run

1. Obtain a sandbox store ID and password from the merchant's test account. Put them into the API environment as `SSLCOMMERZ_STORE_ID` and `SSLCOMMERZ_STORE_PASSWORD`. Set sandbox mode and the public callback origin using the keys in the repository's `.env.example`. [Sandbox registration](https://developer.sslcommerz.com/registration/)
2. Serve the application behind an HTTPS origin with a valid certificate on port 443. For a local run, point an HTTPS tunnel at the storefront reverse proxy serving the checkout API. Set the callback origin to the tunnel's public URL. Reconfigure this value if a temporary tunnel URL changes. Register the same IPN listener in the sandbox merchant panel. Check that the proxy passes form POST bodies through.
3. Record the on-hand and reserved ml for one perfume. Buy two 5 ml decants online and record the quoted total including delivery. While waiting on the hosted page, verify that 10 ml is reserved. Confirm the hosted page displays the expected amount and merchant-enabled channels.
4. Complete the provider's successful test flow using its published sandbox payment details. Confirm the protected order page shows the correct reference and total. Verify that on-hand stock decreased by 10 ml, the reservation cleared, and the payment appeared once in analytics. Repeat with the browser closed before the return redirect to exercise the IPN path. [Sandbox test details](https://developer.sslcommerz.com/doc/v4/index.html)
5. On fresh orders, exercise a decline, cancellation, and abandoned session. Observe the payment status and the release ledger. Refreshing the return page must preserve the same outcome. Compare the provider and local order records if a decision remains pending.
6. Run automated callback cases for a mismatched amount, missing validation ID, duplicated or out-of-order notifications, concurrent settlement, and late success. Confirm one stock decision per reservation and human review for a successful charge after expiry.
7. Reconcile the sandbox merchant panel to the tested order references. Keep live mode disabled until the merchant has verified the results and supplied the production credentials, callback origin, and enabled channels.
