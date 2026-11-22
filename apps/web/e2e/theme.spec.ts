import { expect, test } from "@playwright/test";

test("color theme follows the device or a saved override across the site", async ({
  page,
  context,
}) => {
  await page.emulateMedia({ colorScheme: "dark" });
  await page.goto("/");
  const root = page.locator("html");
  const picker = page.getByRole("combobox", { name: "Color theme" });
  await expect(picker).toHaveValue("system");
  await expect(root).toHaveCSS("color-scheme", "dark");
  const darkPaper = await root.evaluate(
    (el) => getComputedStyle(el).backgroundColor,
  );
  await page.emulateMedia({ colorScheme: "light" });
  await expect(root).toHaveCSS("color-scheme", "light");
  await expect(root).not.toHaveCSS("background-color", darkPaper);

  await picker.selectOption("dark");
  await expect(root).toHaveCSS("background-color", darkPaper);
  await page.getByRole("link", { name: "My account" }).click();
  await expect(page.getByRole("heading", { name: "My account" })).toBeVisible();
  await expect(picker).toHaveValue("dark");
  await expect(root).toHaveCSS("color-scheme", "dark");
  await page.reload();
  await expect(root).toHaveCSS("background-color", darkPaper);
  // A direct request must render the saved theme before hydration.
  const admin = await context.request.get("/admin");
  expect(admin.ok()).toBeTruthy();
  expect(await admin.text()).toMatch(/<html[^>]+data-theme="dark"/);
  await page.getByRole("button", { name: /^Bag \(/ }).click();
  await expect(page.getByRole("dialog")).toHaveCSS(
    "background-color",
    darkPaper,
  );
  await page.getByRole("button", { name: "Close", exact: true }).click();

  await picker.selectOption("light");
  await page.emulateMedia({ colorScheme: "dark" });
  await page.goto("/admin");
  await expect(root).toHaveCSS("color-scheme", "light");
  await expect(picker).toHaveValue("light");
  await picker.selectOption("system");
  await page.reload();
  await expect(picker).toHaveValue("system");
  await expect(root).toHaveCSS("color-scheme", "dark");
});
