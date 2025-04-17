import { createI18n } from "vue-i18n";
import enUS from "./locales/en-US.json";
import ptBR from "./locales/pt-BR.json";

const i18n = createI18n({
  legacy: false,
  locale: "pt-BR",
  fallbackLocale: "en-US",
  messages: {
    "en-US": enUS,
    "pt-BR": ptBR,
  },
});

export default i18n;
