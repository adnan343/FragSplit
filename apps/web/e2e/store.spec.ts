import { randomInt, randomUUID } from "node:crypto";
import {
  type APIRequestContext,
  type Locator,
  test as base,
  expect,
} from "@playwright/test";
import { adminSession } from "./admin-session";

const test = base.extend<{ admin: APIRequestContext }>({
  admin: async ({ playwright, baseURL }, provide) => {
    const admin = await playwright.request.newContext({
      baseURL,
      storageState: adminSession,
      extraHTTPHeaders: { "X-Shop-Request": "1" },
    });
    try {
      await provide(admin);
    } finally {
      await admin.dispose();
    }
  },
});

async function expectImageLoaded(image: Locator) {
  await image.scrollIntoViewIfNeeded();
  await expect
    .poll(
      () =>
        image.evaluate((element) => {
          const img = element as HTMLImageElement;
          return img.complete && img.naturalWidth > 0;
        }),
      { timeout: 10_000 },
    )
    .toBe(true);
}

test("guest discovers a fragrance and completes a multi-size COD order", async ({
  admin,
  page,
  request,
}) => {
  await page.goto("/");
  await expect(page.locator('a[href^="/admin"]')).toHaveCount(0);
  await expect(
    page.getByRole("heading", { name: "Perfume, by the milliliter." }),
  ).toBeVisible();
  await page
    .getByRole("main")
    .getByRole("link", { name: "Shop fragrances", exact: true })
    .click();
  await page
    .getByRole("combobox", { name: "category", exact: true })
    .selectOption("Middle Eastern");
  await page
    .getByRole("textbox", { name: "Search by fragrance, brand or note" })
    .fill("Amber Dusk");
  await expect(page).toHaveURL(/search=Amber/);
  await page
    .getByRole("link")
    .filter({
      has: page.getByRole("heading", { name: "Amber Dusk", level: 2 }),
    })
    .click();
  await page.getByRole("button", { name: "3 ml", exact: true }).click();
  await page.getByRole("button", { name: /Add to bag/ }).click();
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("button", { name: "5 ml", exact: true }).click();
  await page.getByRole("button", { name: /Add to bag/ }).click();
  await page.getByRole("link", { name: "Review bag" }).click();
  await expect(page.getByRole("heading", { name: "Your bag" })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("main").getByLabel("Quantity")).toHaveCount(2);
  await page.getByRole("main").getByLabel("Quantity").first().fill("2");
  await page.getByRole("link", { name: "Continue to checkout" }).click();
  await expect(
    page.getByRole("link", { name: "Sign in or create an account" }),
  ).toHaveCSS("text-decoration-line", "underline");
  for (const [label, value] of [
    ["Full name", "Browser Test"],
    ["Email", "receipt@example.test"],
    ["Mobile number", "017" + String(randomInt(100_000_000)).padStart(8, "0")],
    ["Street address", "7 Example Road"],
    ["Area", "Dhanmondi"],
    ["City or district", "Dhaka"],
  ])
    await page.getByLabel(label, { exact: true }).fill(value);
  await page.getByLabel("I accept the delivery and return policies.").check();
  await page.getByRole("button", { name: "Confirm order" }).click();
  await expect(page.getByRole("heading", { name: /Order VM-/ })).toBeVisible();
  await expect(page.getByText(/Cash on delivery · Unpaid/)).toBeVisible();
  await expect(page.getByLabel(/Private order link/)).toHaveValue(/#token=/);
  const reference = (
    await page.getByRole("heading", { name: /Order VM-/ }).innerText()
  ).match(/VM-[A-F0-9]+/)![0];
  expect(
    (
      await admin.patch("/api/v1/admin/orders/" + reference + "/status", {
        data: { action: "Confirmed" },
      })
    ).ok(),
  ).toBeTruthy();
  const inbox = process.env.E2E_MAILPIT_ORIGIN || "http://127.0.0.1:8025";
  let messageId = "";
  await expect
    .poll(
      async () => {
        const search = await request.get(inbox + "/api/v1/search", {
          params: { query: "to:receipt@example.test subject:" + reference },
        });
        const { messages } = await search.json();
        messageId = messages[0]?.ID;
        return messages.length;
      },
      { timeout: 20000 },
    )
    .toBe(1);
  const mail = await (
    await request.get(inbox + "/api/v1/message/" + messageId)
  ).json();
  expect(mail.Text).toContain("Due on delivery:");
  expect(mail.Attachments).toHaveLength(1);
  const attachment = mail.Attachments[0];
  expect(attachment.FileName).toBe(reference + "-order-slip.html");
  const slip = await (
    await request.get(
      inbox + "/api/v1/message/" + messageId + "/part/" + attachment.PartID,
    )
  ).text();
  expect(slip).toContain("Amber Dusk");
  expect(slip).toContain("Paid: BDT 0.00");
  expect(slip).toContain("Balance due: BDT ");
});
test("a dropped order response and edited notes leave one order and one reservation", async ({
  admin,
  page,
  request,
}) => {
  const product = await (
    await request.get("/api/v1/perfumes/cedar-archive")
  ).json();
  const name = "Retry-" + randomUUID();
  const email = randomUUID() + "@example.test";
  let reference = "";
  let submissions = 0;
  await page.route("**/api/v1/orders", async (route) => {
    const accepted = await route.fetch();
    submissions++;
    if (submissions === 1) {
      expect(accepted.ok()).toBeTruthy();
      reference = (await accepted.json()).reference;
      await route.abort("connectionreset");
    } else {
      await route.fulfill({ response: accepted });
    }
  });
  await page.goto("/fragrances/cedar-archive");
  await page.getByRole("button", { name: "3 ml", exact: true }).click();
  await page.getByRole("button", { name: /Add to bag/ }).click();
  await page.getByRole("link", { name: "Review bag" }).click();
  await page.getByRole("link", { name: "Continue to checkout" }).click();
  for (const [label, value] of [
    ["Full name", name],
    ["Email", email],
    ["Mobile number", "017" + String(randomInt(100_000_000)).padStart(8, "0")],
    ["Street address", "7 Example Road"],
    ["Area", "Dhanmondi"],
    ["City or district", "Dhaka"],
    ["Delivery notes (optional)", "Call first"],
  ])
    await page.getByLabel(label, { exact: true }).fill(value);
  await page.getByLabel("I accept the delivery and return policies.").check();
  const confirm = page.getByRole("button", { name: "Confirm order" });
  await confirm.click();
  await expect(page.getByRole("main").getByRole("alert")).toContainText(
    "The connection dropped",
  );
  await page
    .getByLabel("Delivery notes (optional)", { exact: true })
    .fill("Leave at the door");
  await confirm.click();
  await expect(
    page.getByRole("heading", { name: "Order " + reference }),
  ).toBeVisible();
  expect(submissions).toBe(1);
  const orders = await (
    await admin.get("/api/v1/admin/orders", { params: { search: name } })
  ).json();
  expect(orders).toHaveLength(1);
  expect(orders[0].reference).toBe(reference);
  const movements: { key: string; type: string; reserved: number }[] = await (
    await admin.get(`/api/v1/admin/inventory/${product.id}/movements`)
  ).json();
  expect(movements.filter((m) => m.key.startsWith(reference + ":"))).toEqual([
    expect.objectContaining({ type: "Reserve", reserved: 3 }),
  ]);
  await expect(page.getByText("Call first", { exact: true })).toBeVisible();

  const tab = await page.context().newPage();
  try {
    await tab.goto("/orders?reference=" + reference);
    await expect(tab.getByLabel("Order reference")).toHaveValue(reference);
    await tab.getByLabel("Email", { exact: true }).fill(email);
    await tab.getByRole("button", { name: "Email me a receipt link" }).click();
    await expect(tab.getByRole("status")).toContainText(
      "expires in 30 minutes",
    );
    const inbox = process.env.E2E_MAILPIT_ORIGIN || "http://127.0.0.1:8025";
    const result = await (
      await request.get(inbox + "/api/v1/search", {
        params: { query: "to:" + email },
      })
    ).json();
    expect(result.messages).toHaveLength(1);
    const mail = await (
      await request.get(inbox + "/api/v1/message/" + result.messages[0].ID)
    ).json();
    const link = mail.Text.match(
      /https?:\/\/[^\s]+#token=r1\.[A-Za-z0-9_-]+/,
    )?.[0];
    expect(link).toBeDefined();
    await tab.goto(link!);
    await expect(
      tab.getByRole("heading", { name: "Order " + reference }),
    ).toBeVisible();
  } finally {
    await tab.close();
  }
});

test.describe("signed-in administration", () => {
  test.use({ storageState: adminSession });
  test("admin creates a five-size listing, uploads artwork and restocks", async ({
    page,
  }, info) => {
    const slug = "browser-" + info.project.name + "-" + Date.now();
    await page.goto("/admin/products");
    await page
      .getByRole("button", { name: "New fragrance", exact: true })
      .click();
    for (const [label, value] of [
      ["Perfume name", slug],
      ["URL slug", slug],
      ["Brand", "Browser house"],
      ["Description", "Fictional browser test perfume."],
    ])
      await page.getByLabel(label, { exact: true }).fill(value);
    for (const n of [3, 5, 10, 15, 30])
      await page
        .getByLabel(n + " ml price (BDT)", { exact: true })
        .fill(String(n * 80));
    await page.getByLabel("Opening stock (ml)", { exact: true }).fill("45");
    await page.getByRole("button", { name: "Save fragrance" }).click();
    for (const a of ["amber", "citrus"]) {
      await page
        .getByLabel("Image file")
        .setInputFiles("public/art/demo-" + a + ".webp");
      await page.getByLabel("Image alt text").fill("Illustrated " + a);
      await page.getByRole("button", { name: "Upload image" }).click();
      await expect(page.getByLabel("Image description").last()).toHaveValue(
        "Illustrated " + a,
      );
    }
    await page.getByRole("link", { name: "Inventory", exact: true }).click();
    const row = page.getByRole("row").filter({ hasText: slug });
    await expect(row.getByRole("cell").nth(1)).toHaveText("45 ml");
    await expect(row.getByRole("cell").nth(3)).toHaveText("45 ml");
    await page
      .getByRole("row")
      .filter({ hasText: slug })
      .getByRole("button", { name: "Manage" })
      .click();
    await expect(page.getByText(/Opening stock/)).toBeVisible();
    await page.getByLabel("Signed change in ml").fill("35");
    await page.getByLabel("Reason", { exact: true }).fill("Opening bottle");
    await page.getByRole("button", { name: "Record adjustment" }).click();
    await expect(page.getByText(/Opening bottle/)).toBeVisible();
    await expect(row.getByRole("cell").nth(1)).toHaveText("80 ml");
    await page.goto("/fragrances?search=" + encodeURIComponent(slug));
    const listing = page.getByRole("link").filter({
      has: page.getByRole("heading", { name: slug, level: 2, exact: true }),
    });
    await expectImageLoaded(
      listing.getByRole("img", { name: "Illustrated amber", exact: true }),
    );
    await listing.click();
    await expect(page.getByRole("heading", { name: slug })).toBeVisible();
    const galleryImage = page.locator(".detail-art").getByRole("img");
    await expect(galleryImage).toHaveAttribute("alt", "Illustrated amber");
    await expectImageLoaded(galleryImage);
    const thumbnail = page.getByRole("button", {
      name: "Illustrated citrus",
      exact: true,
    });
    await expectImageLoaded(thumbnail.getByRole("img"));
    await thumbnail.click();
    await expect(galleryImage).toHaveAttribute("alt", "Illustrated citrus");
    await expectImageLoaded(galleryImage);
    await expect(
      page.getByRole("button", { name: "30 ml", exact: true }),
    ).toBeEnabled();
  });
});

test("keyboard navigation, shareable filters and mobile layout", async ({
  page,
}, info) => {
  await page.goto("/");
  await page.keyboard.press("Tab");
  await expect(
    page.getByRole("link", { name: "Skip to content" }),
  ).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(/#main$/);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBeTruthy();
  if (info.project.name === "mobile") {
    await page.getByRole("button", { name: "Toggle navigation" }).focus();
    await page.keyboard.press("Enter");
    await expect(
      page.getByRole("button", { name: "Toggle navigation" }),
    ).toHaveAttribute("aria-expanded", "true");
    expect(
      await page.evaluate(
        () => matchMedia("(prefers-reduced-motion: reduce)").matches,
      ),
    ).toBeTruthy();
  }
  await page.goto("/fragrances?category=Niche&sort=price-asc");
  await expect(
    page.getByRole("combobox", { name: "category", exact: true }),
  ).toHaveValue("Niche");
  await expect(
    page.getByRole("combobox", { name: "sort", exact: true }),
  ).toHaveValue("price-asc");
  await page
    .getByRole("textbox", { name: "Search by fragrance, brand or note" })
    .fill("no-fragrance-matches-xyz");
  await expect(page.getByText(/No fragrances match/)).toBeVisible();
  await page.getByRole("link", { name: "Clear filters" }).click();
  await expect(
    page
      .getByRole("main")
      .getByRole("heading", { name: "Shop fragrances", exact: true }),
  ).toBeVisible();
});

test("quantity can be cleared and retyped before checkout", async ({
  page,
  request,
}) => {
  const response = await request.get("/api/v1/perfumes?available=true");
  const { items } = await response.json();
  await page.goto("/fragrances/" + items[0].slug);
  const quantity = page.getByRole("main").getByLabel("Quantity");
  await quantity.fill("");
  await expect(quantity).toHaveValue("");
  await expect(
    page.getByRole("status").filter({ hasText: "Enter a whole quantity" }),
  ).toBeVisible();
  await quantity.pressSequentially("2");
  await expect(quantity).toHaveValue("2");
  await page.getByRole("button", { name: /Add to bag/ }).click();
  await page.getByRole("link", { name: "Review bag" }).click();
  await expect(page).toHaveURL(/\/cart$/);
  await expect(quantity).toHaveValue("2");
  if (page.viewportSize()!.width < 600) await quantity.tap();
  else await quantity.click();
  await quantity.pressSequentially("3");
  await expect(quantity).toHaveValue("3");
  await quantity.press("End");
  await quantity.press("Backspace");
  await expect(quantity).toHaveValue("");
  await quantity.pressSequentially("012");
  await expect(quantity).toHaveValue("12");
  await quantity.press("Enter");
  await expect(quantity).toHaveValue("12");
  await quantity.fill("");
  await quantity.pressSequentially("1");
  await page.getByRole("link", { name: "Continue to checkout" }).click();
  await expect(page.getByText(/ml × 1\b/)).toBeVisible();
});
