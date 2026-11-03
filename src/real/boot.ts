import { createApp } from "vue";
import { createPinia } from "pinia";
import RealApp from "./RealApp.vue";
import { createRealRouter } from "./router";
import i18n from "./i18n";
import legacyEn from "../i18n/locales/en-US.json";
import legacyPt from "../i18n/locales/pt-BR.json";
import realEn from "./i18n/en-US.json";
import realPt from "./i18n/pt-BR.json";
import { createHttpApi, installApi } from "./api";
import { useSessionStore } from "./stores/session";

// Marcador exclusivo do bundle real (verificado por scripts/check-bundles.mjs).
export const REAL_BUNDLE_MARKER = "data-finance-real-mode";

export async function bootReal(): Promise<void> {
  const app = createApp(RealApp);
  const pinia = createPinia();
  app.use(pinia);
  app.use(i18n);
  i18n.global.mergeLocaleMessage("pt-BR", { ...legacyPt, ...realPt });
  i18n.global.mergeLocaleMessage("en-US", { ...legacyEn, ...realEn });
  document.documentElement.lang = i18n.global.locale.value;

  const session = useSessionStore(pinia);
  installApi(app, createHttpApi({ onUnauthorized: () => session.handleUnauthorized() }));
  const router = createRealRouter();

  // O estado da sessão precisa existir antes da primeira navegação (guards).
  await session.bootstrap();
  app.use(router);
  await router.isReady();

  document.documentElement.setAttribute(REAL_BUNDLE_MARKER, "1");
  app.mount("#app");
}
