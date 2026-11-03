import { createApp } from "vue";
import { createPinia } from "pinia";
import "../assets/main.css";
import RealApp from "./RealApp.vue";
import { realRouter } from "./router";
import i18n from "./i18n";

// Marcador exclusivo do bundle real (verificado por scripts/check-bundles.mjs).
export const REAL_BUNDLE_MARKER = "data-finance-real-mode";

export async function bootReal(): Promise<void> {
  const app = createApp(RealApp);
  app.use(createPinia());
  app.use(realRouter);
  app.use(i18n);
  document.documentElement.setAttribute(REAL_BUNDLE_MARKER, "1");
  app.mount("#app");
}
