import type { PropsWithChildren } from "react";

const session = {
  isAuthenticated: true,
  isLoading: false,
  user: { sub: "auth0|demo-admin", name: "Demo Administrator" },
  getAccessTokenSilently: async () => "browser-test-token",
};

export function Auth0Provider({ children }: PropsWithChildren) {
  return children;
}

export function useAuth0() {
  return session;
}
