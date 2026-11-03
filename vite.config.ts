/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";
import path from "path";

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
  // Fixa VITE_APP_MODE em build (inclusive o padrão "demo" quando ausente), para o
  // Rollup eliminar o ramo do modo não selecionado em src/main.ts.
  const appMode = loadEnv(mode, process.cwd(), "VITE_").VITE_APP_MODE === "real" ? "real" : "demo";
  return {
  define: { "import.meta.env.VITE_APP_MODE": JSON.stringify(appMode) },
  plugins: [vue()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  test: {
    environment: "jsdom",
  },
  server: {
    proxy: {
      // Same-origin API in development (http profile of backend/.../launchSettings.json).
      "/api": "http://localhost:5288",
    },
  },
};
});
