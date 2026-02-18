import { defineConfig, loadEnv } from "vite";
import tailwindcss from "@tailwindcss/vite";
import { tanstackRouter } from "@tanstack/router-plugin/vite";
import path from "path";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  const apiUrl =
    process.env.VITE_API_URL || env.VITE_API_URL || "https://localhost:7248";

  return {
    plugins: [tanstackRouter(), tailwindcss()],
    resolve: {
      alias: {
        "@": path.resolve(__dirname, "./src"),
      },
    },
    server: {
      proxy: {
        "/api": { target: apiUrl, changeOrigin: true, secure: false },
        "/openapi": { target: apiUrl, changeOrigin: true, secure: false },
      },
    },
    build: {
      outDir: "../HomeSuiteHome.WebApi/wwwroot",
      emptyOutDir: true,
      // Suppress "use client" warnings from TanStack React Query.
      // These directives are for React Server Components and are
      // irrelevant in a Vite SPA build.
      rollupOptions: {
        onwarn(warning, defaultHandler) {
          if (warning.code === "MODULE_LEVEL_DIRECTIVE") return;
          defaultHandler(warning);
        },
      },
    },
  };
});
