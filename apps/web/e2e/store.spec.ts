import { test, expect } from "@playwright/test";
test("guest discovers a fragrance and completes a multi-size COD order", async ({
  page,
  request,
}) => {
  await page.goto("/");
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
    .filter({ has: page.getByRole("heading", { name: "Amber Dusk" }) })
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
  for (const [label, value] of [
    ["Full name", "Browser Test"],
    ["Email", "receipt@example.test"],
    ["Mobile number", "01700000000"],
    ["Street address", "7 Example Road"],
    ["Area", "Dhanmondi"],
    ["City or district", "Dhaka"],
  ])
    await page.getByLabel(label, { exact: true }).fill(value);
  await page.getByLabel("I accept the delivery and return policies.").check();
  await page.getByRole("button", { name: "Confirm order" }).click();
  await expect(page.getByRole("heading", { name: /Order FS-/ })).toBeVisible();
  await expect(page.getByText(/Cash on delivery · Unpaid/)).toBeVisible();
  await expect(page.getByLabel(/Private order link/)).toHaveValue(/#token=/);
  const reference = (
    await page.getByRole("heading", { name: /Order FS-/ }).innerText()
  ).match(/FS-[A-F0-9]+/)![0];
  const headers = { "X-Shop-Request": "1" };
  expect(
    (
      await request.post("/api/v1/auth/login", {
        headers,
        data: {
          email: process.env.ADMIN_EMAIL,
          password: process.env.ADMIN_PASSWORD,
        },
      })
    ).ok(),
  ).toBeTruthy();
  expect(
    (
      await request.patch("/api/v1/admin/orders/" + reference + "/status", {
        headers,
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
test("admin creates a five-size listing, uploads artwork and restocks", async ({
  page,
}, info) => {
  const email = process.env.ADMIN_EMAIL,
    password = process.env.ADMIN_PASSWORD;
  if (!email || !password)
    throw Error("Set the ephemeral admin credentials for E2E");
  const slug = "browser-" + info.project.name + "-" + Date.now();
  await page.goto("/admin/products");
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
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
  await page.goto("/fragrances/" + slug);
  await expect(page.getByRole("heading", { name: slug })).toBeVisible();
  await expect(
    page.getByRole("button", { name: "30 ml", exact: true }),
  ).toBeEnabled();
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
