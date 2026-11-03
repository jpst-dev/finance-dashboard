import { inject, provide, type App, type InjectionKey } from "vue";
import type { ApiClient } from "./client";

// Cliente de API compartilhado: disponível por provide/inject nos componentes e por
// setApi/getApi nas stores Pinia (que podem rodar fora de componentes). Testes injetam um fake.
export const API_KEY: InjectionKey<ApiClient> = Symbol("finance-api");

let current: ApiClient | null = null;

export function setApi(client: ApiClient | null): void {
  current = client;
}

export function getApi(): ApiClient {
  if (!current) throw new Error("ApiClient não configurado: chame setApi()/provideApi() antes de usar a API.");
  return current;
}

/** Em `setup()` de componente raiz: provê o cliente aos descendentes e às stores. */
export function provideApi(client: ApiClient): void {
  setApi(client);
  provide(API_KEY, client);
}

/** Instala o cliente no app inteiro (usado pelo boot, fora de componentes). */
export function installApi(app: App, client: ApiClient): void {
  setApi(client);
  app.provide(API_KEY, client);
}

export function useApi(): ApiClient {
  const injected = inject(API_KEY, null) ?? current;
  if (!injected) throw new Error("ApiClient não fornecido: use provideApi()/installApi() no app.");
  return injected;
}
