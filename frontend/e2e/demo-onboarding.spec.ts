import { expect, test } from "@playwright/test";

test("a new user confirms their email before opening the referral queue", async ({ page }) => {
  const code = "01234567";
  let emailRequests = 0;
  let confirmed = false;

  await page.route("**/api/me", async (route) => {
    await route.fulfill(confirmed
      ? {
          json: {
            isAdministrator: true, isDemoAdministrator: true, canCreateReferrals: true,
            providerPublicId: null, patientPublicId: null, memberships: [],
          },
        }
      : { status: 403 });
  });
  await page.route("**/api/me/demo-email", async (route) => {
    expect(route.request().method()).toBe("POST");
    emailRequests += 1;
    await route.fulfill({ status: 202 });
  });
  await page.route("**/api/me/demo-workspace", async (route) => {
    const submitted = route.request().postDataJSON();
    if (submitted.code === "11111111") {
      await route.fulfill({ status: 400, json: { title: "Incorrect confirmation code." } });
      return;
    }
    expect(submitted).toEqual({ code });
    confirmed = true;
    await route.fulfill({ status: 201 });
  });
  await page.route("**/api/referrals*", (route) => route.fulfill({ json: [] }));

  await page.goto("/");
  await expect(page.getByText("We emailed you an 8-digit code", { exact: false })).toBeVisible();
  expect(emailRequests).toBe(1);

  await expect(page.getByText("Spam or Junk folder", { exact: false })).toBeVisible();

  const input = page.getByRole("textbox", { name: "Confirmation code" });
  const confirm = page.getByRole("button", { name: "Confirm email" });
  await expect(confirm).toBeDisabled();
  await input.fill("11111111");
  await confirm.click();
  await expect(page.getByRole("alert")).toHaveText("Incorrect confirmation code.");
  await input.fill(code);
  await confirm.click();
  await expect(page).toHaveURL(/\/referrals$/);
  await expect(page.getByRole("heading", { name: "Referral queue" })).toBeVisible();
  expect(emailRequests).toBe(1);
  expect(page.context().pages()).toHaveLength(1);
});
