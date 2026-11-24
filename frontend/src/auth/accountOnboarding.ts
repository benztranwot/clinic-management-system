export type AccountOnboardingState = {
  status: "ready" | "workspace_pending" | "email_confirmation_required";
  email?: string | null;
};

export async function getAccountOnboarding(
  accessToken: string, signal?: AbortSignal,
): Promise<AccountOnboardingState> {
  const response = await fetch("/api/me/onboarding", {
    headers: { Authorization: "Bearer " + accessToken },
    cache: "no-store", signal,
  });
  if (!response.ok) {
    if (response.status === 401)
      throw new Error("Your session expired. Sign out and sign in again.");
    const body: { message?: unknown; detail?: unknown; title?: unknown } | null =
      await response.json().catch(() => null);
    const message = [body?.message, body?.detail, body?.title].find(value => typeof value === "string");
    throw new Error(typeof message === "string" ? message : "Unable to check your account. Please try again.");
  }
  return response.json();
}
