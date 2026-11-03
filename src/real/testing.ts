import { vi } from "vitest";
import type { ApiClient } from "./api";

/** Cliente falso para testes: só `auth` é funcional; o resto lança se for usado. */
export function fakeApi(over: Partial<ApiClient["auth"]> = {}) {
  const auth = {
    me: vi.fn(async () => null as { email: string } | null),
    login: vi.fn(async () => undefined),
    logout: vi.fn(async () => undefined),
    ...over,
  };
  return { auth } as unknown as ApiClient & { auth: typeof auth };
}
