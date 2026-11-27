import { test, expect, type APIRequestContext } from "@playwright/test";
import { randomInt, randomUUID } from "node:crypto";
async function emailLink(
  request: APIRequestContext,
  email: string,
  subject: string,
  baseURL: string,
) {
  const inbox = process.env.E2E_MAILPIT_ORIGIN || "http://127.0.0.1:8025";
  const response = await request.get(inbox + "/api/v1/search", {
    params: { query: "to:" + email },
  });
  expect(response.ok()).toBeTruthy();
  const { messages } = (await response.json()) as {
    messages: { ID: string; Subject: string }[];
  };
  const found = messages.find((m) => m.Subject === subject);
  expect(found).toBeDefined();
  const detail = await request.get(inbox + "/api/v1/message/" + found!.ID);
  const { Text } = (await detail.json()) as { Text: string };
  const url = new URL(Text.match(/https?:\/\/[^\s]+#token=[A-Za-z0-9_-]+/)![0]);
  expect(url.origin).toBe(new URL(baseURL).origin);
  return url.href;
}

test("a customer continues a guest bag and tracks the resulting order on a second device", async ({
  page,
  browser,
  request,
  baseURL,
}) => {
  const email = randomUUID() + "@example.test",
    password = "Aa1!" + randomUUID();
  await page.goto("/fragrances/amber-dusk");
  await page.getByRole("button", { name: "5 ml", exact: true }).click();
  await page.getByRole("button", { name: /Add to bag/ }).click();
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("link", { name: "My account", exact: true }).click();
  await page
    .getByRole("button", { name: "Create account", exact: true })
    .click();
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByRole("button", { name: "Send verification email" }).click();
  await expect(
    page.getByRole("heading", { name: "Check your email" }),
  ).toBeVisible();
  await page.goto(
    await emailLink(
      request,
      email,
      "Verify your email to open your account",
      baseURL!,
    ),
  );
  await page.getByLabel("New password").fill(password);
  await page.getByRole("button", { name: "Verify and create account" }).click();
  await expect(page.getByText(email, { exact: true })).toBeVisible();
  await page.getByText("Profile and delivery address", { exact: true }).click();
  for (const [label, value] of [
    ["Full name", "Account Customer"],
    ["Mobile number", "018" + String(randomInt(100_000_000)).padStart(8, "0")],
    ["Street address", "21 Library Road"],
    ["City or district", "Dhaka"],
    ["Area", "Dhanmondi"],
  ])
    await page.getByLabel(label, { exact: true }).fill(value);
  await page.getByRole("button", { name: "Save delivery details" }).click();
  await expect(page.getByText("Delivery details saved.")).toBeVisible();
  const second = await browser.newContext({ baseURL });
  try {
    const phone = await second.newPage();
    await phone.goto("/account");
    await phone.getByLabel("Email", { exact: true }).fill(email);
    await phone.getByLabel("Password", { exact: true }).fill(password);
    await phone.getByRole("button", { name: "Sign in", exact: true }).click();
    await phone.getByRole("link", { name: "View saved bag" }).click();
    await expect(
      phone.getByRole("main").getByText("Amber Dusk", { exact: true }),
    ).toBeVisible();
    await phone.getByRole("link", { name: "Continue to checkout" }).click();
    await expect(
      phone.getByLabel("Street address", { exact: true }),
    ).toHaveValue("21 Library Road");
    await expect(phone.getByLabel("Email", { exact: true })).toHaveValue(email);
    await phone
      .getByLabel("Street address", { exact: true })
      .fill("23 Library Road");
    await phone
      .getByLabel("I accept the delivery and return policies.")
      .check();
    await phone.getByRole("button", { name: "Confirm order" }).click();
    const heading = phone.getByRole("heading", { name: /Order VM-/ });
    await expect(heading).toBeVisible();
    const reference = (await heading.innerText()).match(/VM-[A-F0-9]+/)![0];
    await expect(
      phone.getByRole("link", { name: "View all my orders" }),
    ).toBeVisible();
    await page.goto("/account");
    await page.getByRole("link", { name: new RegExp(reference) }).click();
    await expect(
      page.getByRole("heading", { name: "Order " + reference }),
    ).toBeVisible();
    await expect(page.getByText(/Cash on delivery · Unpaid/)).toBeVisible();
    await page.goto("/cart");
    await expect(
      page.getByRole("main").getByText("Your bag is empty."),
    ).toBeVisible();
    await page.goto("/fragrances/amber-dusk");
    await page.getByRole("button", { name: /Add to bag/ }).click();
    await page.getByRole("button", { name: "Close", exact: true }).click();
    await page.goto("/checkout");
    await expect(
      page.getByLabel("Street address", { exact: true }),
    ).toHaveValue("23 Library Road");
    await expect(page.getByLabel("Email", { exact: true })).toHaveValue(email);
    await phone.goto("/account");
    await phone.getByRole("button", { name: "Sign out", exact: true }).click();
    await phone.getByRole("button", { name: "Forgot password?" }).click();
    await phone.getByLabel("Email", { exact: true }).fill(email);
    await phone.getByRole("button", { name: "Send reset email" }).click();
    await expect(
      phone.getByRole("heading", { name: "Check your email" }),
    ).toBeVisible();
    await phone.goto(
      await emailLink(request, email, "Reset your account password", baseURL!),
    );
    const replacement = "Aa1!" + randomUUID();
    await phone.getByLabel("New password").fill(replacement);
    await phone.getByRole("button", { name: "Reset password" }).click();
    await phone
      .getByRole("link", { name: "Sign in with your new password" })
      .click();
    await phone.getByLabel("Email", { exact: true }).fill(email);
    await phone.getByLabel("Password", { exact: true }).fill(replacement);
    await phone.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(
      phone.getByRole("link", { name: new RegExp(reference) }),
    ).toBeVisible();
  } finally {
    await second.close();
  }
});
