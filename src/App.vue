<template>
  <div class="min-h-screen bg-gray-950 text-gray-50 lg:grid lg:grid-cols-[232px_1fr]">
    <!-- Barra do topo no celular -->
    <header
      class="sticky top-0 z-30 flex h-14 items-center justify-between border-b border-gray-700 bg-gray-950/90 px-4 backdrop-blur lg:hidden"
    >
      <router-link to="/" class="flex items-center gap-2.5 font-semibold">
        <span class="logo">F</span>Finance Dashboard
      </router-link>
      <button
        class="rounded-lg p-2 text-gray-300 hover:bg-gray-700"
        :aria-label="t('nav.menu')"
        :aria-expanded="menuAberto"
        @click="menuAberto = !menuAberto"
      >
        <svg class="h-5 w-5" fill="none" stroke="currentColor" stroke-width="2" viewBox="0 0 24 24">
          <path v-if="menuAberto" stroke-linecap="round" d="M6 6l12 12M18 6L6 18" />
          <path v-else stroke-linecap="round" d="M4 7h16M4 12h16M4 17h16" />
        </svg>
      </button>
    </header>

    <!-- Menu lateral (no celular, abre por cima do conteúdo) -->
    <aside
      class="fixed inset-x-0 top-14 bottom-0 z-20 flex-col gap-1 border-gray-700 bg-gray-900 p-4 lg:sticky lg:top-0 lg:flex lg:h-screen lg:border-r"
      :class="menuAberto ? 'flex' : 'hidden'"
    >
      <router-link to="/" class="mb-6 hidden items-center gap-2.5 px-2 pt-2 text-[15px] font-semibold lg:flex">
        <span class="logo">F</span>Finance Dashboard
      </router-link>

      <router-link
        v-for="item in itens"
        :key="item.to"
        :to="item.to"
        class="flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm text-gray-400 transition hover:bg-gray-800 hover:text-gray-50"
        exact-active-class="!bg-gray-700 !text-gray-50 font-medium"
        @click="menuAberto = false"
      >
        <svg class="h-[18px] w-[18px]" fill="none" stroke="currentColor" stroke-width="1.8" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" :d="item.icone" />
        </svg>
        {{ t(item.rotulo) }}
      </router-link>

      <div class="mt-auto space-y-3 border-t border-gray-700 pt-4">
        <p class="px-1 text-xs leading-relaxed text-gray-500">{{ t("nav.demoNote") }}</p>
        <div class="flex gap-2">
          <button class="acao" @click="limpar">{{ t("nav.clear") }}</button>
          <button class="acao" @click="restaurar">{{ t("nav.restore") }}</button>
        </div>
        <div class="flex rounded-lg border border-gray-700 p-0.5 text-xs" role="group" aria-label="Idioma / Language">
          <button
            v-for="lang in languages"
            :key="lang.code"
            class="flex-1 rounded-md py-1.5 transition"
            :class="locale === lang.code ? 'bg-gray-700 text-gray-50' : 'text-gray-400 hover:text-gray-50'"
            :aria-pressed="locale === lang.code"
            @click="locale = lang.code"
          >
            {{ lang.name }}
          </button>
        </div>
      </div>
    </aside>

    <main class="mx-auto w-full max-w-[1400px] px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
      <router-view />
    </main>
  </div>
</template>

<script setup lang="ts">
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { limparDados, restaurarExemplo } from "./demo/exemplo";

const { t, locale } = useI18n();
const menuAberto = ref(false);

const languages = [
  { code: "pt-BR", name: "PT" },
  { code: "en-US", name: "EN" },
];

const itens = [
  { to: "/", rotulo: "nav.overview", icone: "M4 13h6V4H4v9zm0 7h6v-5H4v5zm10 0h6v-9h-6v9zm0-16v5h6V4h-6z" },
  { to: "/portfolio", rotulo: "nav.portfolio", icone: "M4 19V9m6 10V5m6 14v-7m4 7H3" },
];

// O idioma escolhido vale para as próximas visitas.
try {
  const salvo = localStorage.getItem("finance_dashboard_locale");
  if (salvo) locale.value = salvo;
} catch {}
watch(locale, (l) => {
  document.documentElement.lang = l;
  try {
    localStorage.setItem("finance_dashboard_locale", l);
  } catch {}
}, { immediate: true });

// Os stores leem o localStorage ao iniciar, então recarregar é o jeito mais simples de refletir a troca.
const limpar = () => {
  if (!confirm(t("nav.clearConfirm"))) return;
  limparDados(localStorage);
  location.reload();
};
const restaurar = () => {
  restaurarExemplo(localStorage);
  location.reload();
};
</script>

<style scoped>
.logo {
  @apply grid h-7 w-7 place-items-center rounded-lg bg-blue-500 text-sm font-bold text-gray-950;
}
.acao {
  @apply flex-1 rounded-lg border border-gray-700 px-2 py-1.5 text-xs text-gray-300 transition hover:border-gray-500 hover:text-gray-50;
}
</style>
