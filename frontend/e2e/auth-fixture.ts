import { useSyncExternalStore, type PropsWithChildren } from "react";

const signedOutKey = "clinic-test:signed-out";
let authenticated = sessionStorage.getItem(signedOutKey) !== "true";
const listeners = new Set<() => void>();
const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
};
const getSnapshot = () => authenticated;
const user = { sub: "auth0|demo-admin", name: "Demo Administrator" };

async function logout(options?: { openUrl?: false; logoutParams?: { returnTo?: string } }) {
  sessionStorage.setItem("clinic-test:logout-options", JSON.stringify(options ?? {}));
  sessionStorage.setItem(signedOutKey, "true");
  authenticated = false;
  listeners.forEach(listener => listener());
  if (options?.openUrl !== false) window.location.assign(options?.logoutParams?.returnTo ?? "/");
}

async function loginWithRedirect() {
  sessionStorage.removeItem(signedOutKey);
  authenticated = true;
  listeners.forEach(listener => listener());
  window.location.assign("/referrals");
}

async function getAccessTokenSilently(options?: { detailedResponse?: boolean }) {
  if (!authenticated) throw new Error("No signed-in browser-test session.");
  return options?.detailedResponse
    ? { access_token: "browser-test-token", scope: (globalThis as typeof globalThis & { __testScopes?: string }).__testScopes ?? "manage:referrals" }
    : "browser-test-token";
}

export function Auth0Provider({ children }: PropsWithChildren) {
  return children;
}

export function useAuth0() {
  const isAuthenticated = useSyncExternalStore(subscribe, getSnapshot);
  return { isAuthenticated, isLoading: false, user: isAuthenticated ? user : undefined,
    getAccessTokenSilently, logout, loginWithRedirect };
}
