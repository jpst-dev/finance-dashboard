import { watch } from "vue";
import { createRouter, createWebHistory, type RouterHistory } from "vue-router";
import i18n from "./i18n";
import { setRouter } from "./routerRef";
import { useSessionStore } from "./stores/session";
import Login from "./pages/Login.vue";
import Overview from "./pages/Overview.vue";
import Transactions from "./pages/Transactions.vue";
import Pending from "./pages/Pending.vue";
import Accounts from "./pages/Accounts.vue";
import Import from "./pages/Import.vue";
import Rules from "./pages/Rules.vue";

export function createRealRouter(history: RouterHistory = createWebHistory()) {
  const router = createRouter({
    history,
    routes: [
      { path: "/login", name: "login", component: Login },
      { path: "/", name: "overview", component: Overview, meta: { requiresAuth: true } },
      { path: "/transactions", name: "transactions", component: Transactions, meta: { requiresAuth: true } },
      { path: "/pending", name: "pending", component: Pending, meta: { requiresAuth: true } },
      { path: "/accounts", name: "accounts", component: Accounts, meta: { requiresAuth: true } },
      { path: "/import", name: "import", component: Import, meta: { requiresAuth: true } },
      { path: "/rules", name: "rules", component: Rules, meta: { requiresAuth: true } },
      { path: "/:pathMatch(.*)*", redirect: "/" },
    ],
  });

  router.beforeEach(async (to) => {
    const session = useSessionStore();
    if (session.status === "unknown") await session.bootstrap();
    if (to.meta.requiresAuth && session.status !== "authenticated") {
      return { name: "login", query: { next: to.fullPath } };
    }
    if (to.name === "login" && session.status === "authenticated") return { path: "/" };
    return true;
  });

  // Título só com texto traduzido; nunca valores. Atualiza também ao trocar o idioma.
  function updateTitle() {
    const name = router.currentRoute.value.name;
    const key = typeof name === "string" ? `real.titles.${name}` : "real.app.title";
    document.title = `${i18n.global.t(key)} · ${i18n.global.t("real.app.title")}`;
  }
  router.afterEach(() => {
    // Uma navegação bem-sucedida descarta o aviso de logout que falhou.
    useSessionStore().error = null;
    updateTitle();
  });
  watch(i18n.global.locale, updateTitle);

  setRouter(router);
  return router;
}
