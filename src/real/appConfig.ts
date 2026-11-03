import { inject, type App, type InjectionKey } from "vue";

/** Pontos de extensão do app real. O boot real não fornece nada (padrões = comportamento atual). */
export interface AppConfig {
  /** Mostra a faixa de somente leitura (demo). */
  readOnly: boolean;
  /** Oculta o botão de sair. */
  hideLogout: boolean;
  /** Data fixa (YYYY-MM-DD) usada como "hoje"; null = relógio local. */
  fixedToday: string | null;
}

export const DEFAULT_APP_CONFIG: Readonly<AppConfig> = Object.freeze({ readOnly: false, hideLogout: false, fixedToday: null });

export const APP_CONFIG_KEY: InjectionKey<AppConfig> = Symbol("finance-app-config");

const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

let current: AppConfig = { ...DEFAULT_APP_CONFIG };

/** Mescla com os padrões; `null` restaura. `fixedToday` fora de YYYY-MM-DD lança. */
export function setAppConfig(c: Partial<AppConfig> | null): void {
  if (c === null) {
    current = { ...DEFAULT_APP_CONFIG };
    return;
  }
  const next: AppConfig = { ...DEFAULT_APP_CONFIG, ...c };
  if (next.fixedToday !== null && !DATE_RE.test(next.fixedToday)) {
    throw new Error("fixedToday inválido: use YYYY-MM-DD.");
  }
  current = next;
}

export function getAppConfig(): AppConfig {
  return current;
}

export function installAppConfig(app: App, c: Partial<AppConfig>): void {
  setAppConfig(c);
  app.provide(APP_CONFIG_KEY, getAppConfig());
}

export function useAppConfig(): AppConfig {
  return inject(APP_CONFIG_KEY, null) ?? getAppConfig();
}
