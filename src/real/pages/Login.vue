<script setup lang="ts">
import { ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { useI18n } from "vue-i18n";
import { ApiError } from "../api/errors";
import { useSessionStore } from "../stores/session";
import { safeNext } from "../safeNext";

const { t } = useI18n();
const session = useSessionStore();
const router = useRouter();
const route = useRoute();

const email = ref("");
const password = ref("");
const busy = ref(false);
const errorKey = ref<string | null>(null);

async function submit() {
  if (busy.value) return;
  busy.value = true;
  errorKey.value = null;
  try {
    await session.login(email.value, password.value);
    await router.replace(safeNext(route.query.next));
  } catch (e) {
    password.value = "";
    if (e instanceof ApiError && e.status === 401) errorKey.value = "real.login.errorInvalid";
    else if (e instanceof ApiError && e.status === 429) errorKey.value = "real.login.errorRateLimit";
    else errorKey.value = "real.login.errorNetwork";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <main class="flex min-h-screen items-center justify-center bg-gray-950 px-4 text-gray-50">
    <form class="w-full max-w-sm space-y-4 rounded-xl border border-gray-700 bg-gray-900 p-6" @submit.prevent="submit">
      <h1 class="text-xl font-semibold">{{ t("real.login.title") }}</h1>
      <div>
        <label for="login-email" class="mb-1 block text-sm text-gray-300">{{ t("real.login.email") }}</label>
        <input
          id="login-email"
          v-model="email"
          type="email"
          name="email"
          autocomplete="username"
          required
          class="w-full rounded-lg border border-gray-700 bg-gray-950 px-3 py-2 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-400"
        />
      </div>
      <div>
        <label for="login-password" class="mb-1 block text-sm text-gray-300">{{ t("real.login.password") }}</label>
        <input
          id="login-password"
          v-model="password"
          type="password"
          name="password"
          autocomplete="current-password"
          required
          class="w-full rounded-lg border border-gray-700 bg-gray-950 px-3 py-2 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-400"
        />
      </div>
      <p v-if="session.bootstrapError" role="status" data-testid="bootstrap-error" class="text-sm text-amber-400">{{ t("real.login.serverUnreachable") }}</p>
      <p v-if="errorKey" role="alert" class="text-sm text-red-400">{{ t(errorKey) }}</p>
      <button
        type="submit"
        :disabled="busy"
        class="w-full rounded-lg bg-indigo-500 px-3 py-2 font-medium text-white hover:bg-indigo-400 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-300 disabled:opacity-50"
      >
        {{ busy ? t("real.login.submitting") : t("real.login.submit") }}
      </button>
    </form>
  </main>
</template>
