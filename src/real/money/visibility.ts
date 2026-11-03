import { defineStore } from "pinia";
import { ref } from "vue";

export const MASK = "••••";
const KEY = "fd.hideValues";

function load(): boolean {
  try {
    return localStorage.getItem(KEY) === "1";
  } catch {
    return false;
  }
}

function save(v: boolean): void {
  try {
    localStorage.setItem(KEY, v ? "1" : "0");
  } catch {
    /* armazenamento indisponível: preferência vale só na sessão */
  }
}

export const useValuesVisibility = defineStore("fd-values-visibility", () => {
  const hidden = ref(load());
  function toggle() {
    hidden.value = !hidden.value;
    save(hidden.value);
  }
  return { hidden, toggle };
});
