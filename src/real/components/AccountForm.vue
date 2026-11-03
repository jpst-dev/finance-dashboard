<script setup lang="ts">
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import type { AccountType } from "../api/types";
import { validName, type NewAccountInput } from "../stores/accounts";

defineProps<{ busy: boolean }>();
const emit = defineEmits<{ submit: [input: NewAccountInput] }>();
const { t } = useI18n();

const name = ref("");
const type = ref<AccountType>("Checking");
const institution = ref("");
const externalIdentifier = ref("");
const invalid = ref(false);
const field =
  "w-full rounded border border-gray-600 bg-gray-800 px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";

function onSubmit() {
  const n = validName(name.value);
  invalid.value = n === null;
  if (!n) return;
  emit("submit", { name: n, type: type.value, institution: institution.value, externalIdentifier: externalIdentifier.value });
}

function reset() {
  name.value = "";
  institution.value = "";
  externalIdentifier.value = "";
  invalid.value = false;
}
defineExpose({ reset });
</script>

<template>
  <form class="grid gap-3 sm:grid-cols-2" novalidate @submit.prevent="onSubmit">
    <div>
      <label for="acc-name" class="mb-1 block text-sm">{{ t("real.accounts.name") }}</label>
      <input id="acc-name" v-model="name" :aria-invalid="invalid" aria-describedby="acc-name-err" :class="field" />
      <p v-if="invalid" id="acc-name-err" class="text-sm text-red-400" role="alert">{{ t("real.errors.invalid_name") }}</p>
    </div>
    <div>
      <label for="acc-type" class="mb-1 block text-sm">{{ t("real.accounts.type") }}</label>
      <select id="acc-type" v-model="type" :class="field">
        <option value="Checking">{{ t("real.accounts.types.Checking") }}</option>
        <option value="CreditCard">{{ t("real.accounts.types.CreditCard") }}</option>
        <option value="Wallet">{{ t("real.accounts.types.Wallet") }}</option>
      </select>
    </div>
    <div>
      <label for="acc-inst" class="mb-1 block text-sm">{{ t("real.accounts.institution") }}</label>
      <input id="acc-inst" v-model="institution" maxlength="200" :class="field" />
    </div>
    <div>
      <label for="acc-ext" class="mb-1 block text-sm">{{ t("real.accounts.externalIdentifier") }}</label>
      <input id="acc-ext" v-model="externalIdentifier" maxlength="200" :class="field" />
    </div>
    <div class="sm:col-span-2">
      <button
        type="submit"
        :disabled="busy"
        class="rounded bg-blue-600 px-3 py-1 text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300"
      >
        {{ t("real.accounts.create") }}
      </button>
    </div>
  </form>
</template>
