import { createI18n } from "vue-i18n";
import realEn from "./en-US.json";
import realPt from "./pt-BR.json";

export type AppLocale = "pt-BR" | "en-US";
const KEY = "fd.locale";

function loadLocale(): AppLocale {
  try {
    const v = localStorage.getItem(KEY);
    if (v === "pt-BR" || v === "en-US") return v;
  } catch {
    /* armazenamento indisponível: usa o padrão */
  }
  return "pt-BR";
}

const i18n = createI18n({
  legacy: false,
  locale: loadLocale(),
  fallbackLocale: "en-US",
  messages: {
    "en-US": realEn,
    "pt-BR": realPt,
  },
});

export function setLocale(l: AppLocale): void {
  i18n.global.locale.value = l;
  try {
    localStorage.setItem(KEY, l);
  } catch {
    /* preferência vale só na sessão */
  }
  if (typeof document !== "undefined") document.documentElement.lang = l;
}

export default i18n;
