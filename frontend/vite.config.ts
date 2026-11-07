import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

export default defineConfig({
  // Enable React support and generate Tailwind styles during development and builds.
  plugins: [react(), tailwindcss()],
  server: {
    // Forward API requests to ASP.NET Core during local development.
    proxy: {
      "/api": "http://localhost:5089",
    },
  },
});
