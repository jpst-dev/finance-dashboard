<script setup lang="ts">
import { useI18n } from "vue-i18n";
import Money from "../money/Money.vue";
import { useAccountsStore } from "../stores/accounts";

const { t } = useI18n();
const accounts = useAccountsStore();
</script>

<template>
  <section aria-labelledby="strip-h" data-test="account-strip">
    <h2 id="strip-h" class="sr-only">{{ t("real.overview.accounts") }}</h2>
    <p v-if="accounts.error" class="text-sm text-red-300" role="alert" data-test="accounts-error">
      {{ t(`real.errors.${accounts.error}`) }}
      <button type="button" class="ml-2 underline" @click="accounts.load()">{{ t("real.accounts.retry") }}</button>
    </p>
    <p v-else-if="!accounts.loaded" class="text-sm text-gray-400" data-test="accounts-loading">{{ t("real.accounts.loading") }}</p>
    <ul v-else class="flex flex-wrap items-stretch gap-3">
      <li v-for="a in accounts.active" :key="a.id" class="rounded border border-gray-700 px-3 py-2" data-test="strip-account">
        <div class="text-sm text-gray-300">{{ a.name }}</div>
        <div class="text-lg">
          <span v-if="a.type === 'CreditCard'" class="mr-1 text-sm text-gray-400">{{ t("real.accounts.debt") }}</span>
          <Money :value="a.balance" />
        </div>
      </li>
      <li class="flex items-center">
        <RouterLink
          to="/accounts"
          class="rounded border border-dashed border-gray-600 px-3 py-2 text-sm underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300"
          >{{ t("real.overview.addAccount") }}</RouterLink
        >
      </li>
    </ul>
  </section>
</template>
