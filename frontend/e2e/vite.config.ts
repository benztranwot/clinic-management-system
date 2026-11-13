import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import appConfig from "../vite.config.ts";

// Only the browser tests use the simulated session and fictional API responses.
export default defineConfig({
  ...appConfig,
  resolve: {
    alias: {
      "@auth0/auth0-react": fileURLToPath(new URL("./auth-fixture.ts", import.meta.url)),
    },
  },
  define: {
    "import.meta.env.VITE_AUTH0_DOMAIN": JSON.stringify("auth.example.invalid"),
    "import.meta.env.VITE_AUTH0_CLIENT_ID": JSON.stringify("browser-test-client"),
    "import.meta.env.VITE_AUTH0_AUDIENCE": JSON.stringify("https://api.example.invalid"),
  },
  server: { host: "127.0.0.1", port: 4174, strictPort: true },
});
