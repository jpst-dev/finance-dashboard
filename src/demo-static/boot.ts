import { createApp, type App } from "vue";
import { createPinia, type Pinia } from "pinia";
import type { Router, RouterHistory } from "vue-router";
import RealApp from "../real/RealApp.vue";
import { createRealRouter } from "../real/router";
import i18n from "../real/i18n";
import { installApi } from "../real/api/context";
import { installAppConfig, type AppConfig } from "../real/appConfig";
import { useSessionStore } from "../real/stores/session";
import { DEMO_FIXTURES } from "./fixtures";
import { createStaticApi } from "./staticApi";

// Marcador exclusivo do bundle do demo estático (a verificação em scripts/check-bundles.mjs é ligada na Task 5).
export const DEMO_BUNDLE_MARKER = "data-finance-demo-mode";

export const DEMO_CONFIG: AppConfig = {
  readOnly: true,
  hideLogout: true,
  fixedToday: DEMO_FIXTURES.meta.referenceDate,
};

/** Monta a interface real sobre o adaptador estático (sem rede). Não faz mount. */
export async function createDemoApp(history?: RouterHistory): Promise<{ app: App; router: Router; pinia: Pinia }> {
  const app = createApp(RealApp);
  const pinia = createPinia();
  app.use(pinia);
  app.use(i18n);
  document.documentElement.lang = String(i18n.global.locale.value);

  installApi(app, createStaticApi(DEMO_FIXTURES));
  installAppConfig(app, DEMO_CONFIG);

  const router = createRealRouter(history);
  // O estado da sessão precisa existir antes da primeira navegação (guards).
  await useSessionStore(pinia).bootstrap();
  app.use(router);
  await router.isReady();

  document.documentElement.setAttribute(DEMO_BUNDLE_MARKER, "1");
  return { app, router, pinia };
}

export async function bootDemo(): Promise<void> {
  try {
    const { app } = await createDemoApp();
    app.mount("#app");
  } catch (e) {
    console.error(e);
    const el = document.getElementById("app");
    if (el) el.textContent = "Demo indisponível";
    throw e;
  }
}
