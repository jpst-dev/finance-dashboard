import type { Router } from "vue-router";

// Referência ao roteador para stores (que navegam fora de componentes). Evita import circular.
let current: Router | null = null;
export function setRouter(r: Router | null): void {
  current = r;
}
export function getRouter(): Router | null {
  return current;
}
