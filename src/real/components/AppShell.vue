<script setup lang="ts">
import { useI18n } from "vue-i18n";
import { RouterLink } from "vue-router";
import { useSessionStore } from "../stores/session";
import { useValuesVisibility } from "../money/visibility";
import { setLocale, type AppLocale } from "../i18n";

const { t, locale } = useI18n();
const session = useSessionStore();
const visibility = useValuesVisibility();

const items = [
  { to: "/", key: "overview" },
  { to: "/transactions", key: "transactions" },
  { to: "/pending", key: "pending" },
  { to: "/accounts", key: "accounts" },
  { to: "/import", key: "import" },
];
const languages: { code: AppLocale; label: string }[] = [
  { code: "pt-BR", label: "PT" },
  { code: "en-US", label: "EN" },
];
const focus = "focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-400";
</script>

<template>
  <div class="min-h-screen bg-gray-950 text-gray-50 lg:grid lg:grid-cols-[232px_1fr]">
    <a
      href="#conteudo"
      class="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-50 focus:rounded focus:bg-gray-800 focus:px-3 focus:py-2"
    >
      {{ t("real.shell.skip") }}
    </a>
    <aside class="flex flex-col gap-1 border-b border-gray-700 bg-gray-900 p-4 lg:sticky lg:top-0 lg:h-screen lg:border-b-0 lg:border-r">
      <p class="mb-3 px-2 text-[15px] font-semibold">{{ t("real.app.title") }}</p>
      <nav :aria-label="t('real.shell.navLabel')" class="flex flex-wrap gap-1 lg:flex-col">
        <RouterLink
          v-for="item in items"
          :key="item.to"
          :to="item.to"
          class="rounded-lg px-3 py-2 text-sm text-gray-400 transition hover:bg-gray-800 hover:text-gray-50"
          :class="focus"
          exact-active-class="!bg-gray-700 !text-gray-50 font-medium"
        >
          {{ t(`real.nav.${item.key}`) }}
        </RouterLink>
      </nav>

      <div class="mt-4 space-y-3 border-t border-gray-700 pt-4 lg:mt-auto">
        <p v-if="session.user" class="truncate px-1 text-xs text-gray-400" data-testid="user-email">
          {{ session.user.email }}
        </p>
        <p v-if="session.error" role="alert" class="px-1 text-xs text-red-400">
          {{ t("real.shell.logoutFailed") }}
        </p>
        <button
          type="button"
          data-testid="toggle-values"
          class="w-full rounded-lg border border-gray-700 px-3 py-2 text-sm hover:bg-gray-800"
          :class="focus"
          :aria-pressed="visibility.hidden"
          @click="visibility.toggle()"
        >
          <span aria-hidden="true">{{ visibility.hidden ? "◉" : "○" }}</span>
          {{ visibility.hidden ? t("real.shell.showValues") : t("real.shell.hideValues") }}
        </button>
        <div class="flex rounded-lg border border-gray-700 p-0.5 text-xs" role="group" :aria-label="t('real.shell.language')">
          <button
            v-for="lang in languages"
            :key="lang.code"
            type="button"
            class="flex-1 rounded-md py-1.5 transition"
            :class="[focus, locale === lang.code ? 'bg-gray-700 text-gray-50' : 'text-gray-400 hover:text-gray-50']"
            :aria-pressed="locale === lang.code"
            @click="setLocale(lang.code)"
          >
            {{ lang.label }}
          </button>
        </div>
        <button
          type="button"
          data-testid="logout"
          class="w-full rounded-lg border border-gray-700 px-3 py-2 text-sm hover:bg-gray-800"
          :class="focus"
          @click="session.logout()"
        >
          {{ t("real.shell.logout") }}
        </button>
      </div>
    </aside>
    <main id="conteudo" class="p-4 lg:p-8">
      <slot />
    </main>
  </div>
</template>
