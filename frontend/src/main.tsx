import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { Auth0Provider } from "@auth0/auth0-react";
import { BrowserRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import "./index.css";
import App from "./App.tsx";
import { createAuthSessionCache } from "./auth/sessionCache";

// Read the public Auth0 configuration supplied by Vite.
const domain = import.meta.env.VITE_AUTH0_DOMAIN;
const clientId = import.meta.env.VITE_AUTH0_CLIENT_ID;
const audience = import.meta.env.VITE_AUTH0_AUDIENCE;

if (!domain || !clientId || !audience) {
  throw new Error("Missing required Auth0 configuration.");
}

// Share one query cache across the application.
const queryClient = new QueryClient();
const sessionCache = createAuthSessionCache(window);

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    {/* Make authentication available to all components inside the provider. */}
    <Auth0Provider
      domain={domain}
      clientId={clientId}
      useRefreshTokens
      cache={sessionCache}
      authorizationParams={{
        redirect_uri: window.location.origin,
        audience,
        scope: "openid profile email read:clinics manage:clinics read:patients manage:patients read:providers manage:providers read:services manage:services read:provider-services manage:provider-services read:referrals manage:referrals",
      }}
    >
      {/* Keep page navigation synchronized with the browser URL. */}
      <BrowserRouter>
        <QueryClientProvider client={queryClient}>
          <App />
        </QueryClientProvider>
      </BrowserRouter>
    </Auth0Provider>
  </StrictMode>,
);
