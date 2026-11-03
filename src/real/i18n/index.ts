import { createI18n } from "vue-i18n";
import legacyEn from "../../i18n/locales/en-US.json";
import legacyPt from "../../i18n/locales/pt-BR.json";
import realEn from "./en-US.json";
import realPt from "./pt-BR.json";

// Mescla os rótulos compartilhados do legado com as chaves exclusivas do modo real.
const i18n = createI18n({
  legacy: false,
  locale: "pt-BR",
  fallbackLocale: "en-US",
  messages: {
    "en-US": { ...legacyEn, ...realEn },
    "pt-BR": { ...legacyPt, ...realPt },
  },
});

export default i18n;
