import { defineStore } from "pinia";
import { ref } from "vue";
import { getApi } from "../api/context";
import { getRouter } from "../routerRef";

export type SessionStatus = "unknown" | "anonymous" | "authenticated";
export type SessionUser = { email: string };

export const useSessionStore = defineStore("fd-session", () => {
  const user = ref<SessionUser | null>(null);
  const status = ref<SessionStatus>("unknown");
  /** Código do último erro de sessão a exibir (ex.: `logout_failed`); nunca texto do servidor. */
  const error = ref<string | null>(null);
  let redirecting = false;

  function setAnonymous() {
    user.value = null;
    status.value = "anonymous";
  }

  async function bootstrap(): Promise<void> {
    try {
      const me = await getApi().auth.me();
      if (me) {
        user.value = { email: me.email };
        status.value = "authenticated";
      } else setAnonymous();
    } catch {
      setAnonymous();
    }
  }

  /** Lança o erro da API (o chamador traduz); nunca registra credenciais. */
  async function login(email: string, password: string): Promise<void> {
    const api = getApi();
    await api.auth.login(email, password);
    error.value = null;
    const me = await api.auth.me();
    if (!me) throw new Error("session_not_established");
    user.value = { email: me.email };
    status.value = "authenticated";
  }

  async function logout(): Promise<void> {
    try {
      await getApi().auth.logout();
    } catch {
      // A sessão pode continuar viva: mantém o usuário e avisa.
      error.value = "logout_failed";
      return;
    }
    error.value = null;
    setAnonymous();
    await getRouter()?.push({ name: "login" });
  }

  function handleUnauthorized(): void {
    setAnonymous();
    const router = getRouter();
    if (!router || redirecting) return;
    const current = router.currentRoute.value;
    if (current.name === "login") return;
    redirecting = true;
    router
      .push({ name: "login", query: { next: current.fullPath } })
      .catch(() => undefined)
      .finally(() => {
        redirecting = false;
      });
  }

  return { user, status, error, bootstrap, login, logout, handleUnauthorized };
});
