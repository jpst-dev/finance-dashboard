<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useI18n } from "vue-i18n";
import type { Account } from "../api/types";
import AccountForm from "../components/AccountForm.vue";
import CheckpointForm from "../components/CheckpointForm.vue";
import Money from "../money/Money.vue";
import { useAccountsStore, validName, type NewAccountInput } from "../stores/accounts";

const { t } = useI18n();
const store = useAccountsStore();
const form = ref<InstanceType<typeof AccountForm> | null>(null);
const renaming = ref<string | null>(null);
const renameText = ref("");
const checkpointFor = ref<string | null>(null);
const confirmArchive = ref<string | null>(null);
const showArchived = ref(false);
const status = ref("");

onMounted(() => void store.load());

async function create(input: NewAccountInput) {
  if (await store.create(input)) {
    form.value?.reset();
    status.value = t("real.accounts.created");
  }
}

function startRename(a: Account) {
  renaming.value = a.id;
  renameText.value = a.name;
}
async function saveRename(a: Account) {
  if (validName(renameText.value) === null) {
    store.error = "invalid_name";
    return;
  }
  if (await store.rename(a.id, renameText.value)) {
    renaming.value = null;
    status.value = t("real.accounts.renamed");
  }
}
async function archive(a: Account) {
  confirmArchive.value = null;
  if (await store.setArchived(a.id, true)) status.value = t("real.accounts.archivedDone");
}
async function unarchive(a: Account) {
  if (await store.setArchived(a.id, false)) status.value = t("real.accounts.unarchivedDone");
}
async function checkpoint(a: Account, date: string, amount: string) {
  if (await store.addCheckpoint(a.id, date, amount)) {
    checkpointFor.value = null;
    status.value = t("real.accounts.checkpointDone");
  }
}
const btn =
  "rounded border border-gray-600 px-2 py-1 text-sm disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-300";
const field =
  "rounded border border-gray-600 bg-gray-800 px-2 py-1 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-400";
</script>

<template>
  <section>
    <h1 class="text-xl font-semibold">{{ t("real.nav.accounts") }}</h1>
    <p class="sr-only" role="status" aria-live="polite">{{ status }}</p>
    <p v-if="store.error" class="mt-2 text-sm text-red-400" role="alert" data-test="error">
      {{ t(`real.errors.${store.error}`) }}
    </p>

    <p v-if="!store.loaded && store.busy" class="mt-4 text-gray-400">{{ t("real.accounts.loading") }}</p>
    <div v-else-if="!store.loaded" class="mt-4">
      <button type="button" :class="btn" :disabled="store.busy" @click="store.load()">{{ t("real.accounts.retry") }}</button>
    </div>
    <template v-else>
      <p v-if="store.active.length === 0" class="mt-4 text-gray-400">{{ t("real.accounts.empty") }}</p>
      <ul class="mt-4 space-y-3">
        <li v-for="a in store.active" :key="a.id" class="rounded border border-gray-700 p-3" data-test="account">
          <div v-if="renaming === a.id" class="flex flex-wrap items-end gap-2">
            <div>
              <label :for="`ren-${a.id}`" class="mb-1 block text-sm">{{ t("real.accounts.name") }}</label>
              <input :id="`ren-${a.id}`" v-model="renameText" :class="field" @keydown.enter.prevent="saveRename(a)" />
            </div>
            <button type="button" :class="btn" :disabled="store.busy" data-test="rename-save" @click="saveRename(a)">
              {{ t("real.accounts.save") }}
            </button>
            <button type="button" :class="btn" :disabled="store.busy" @click="renaming = null">{{ t("real.accounts.cancel") }}</button>
          </div>
          <div v-else class="flex flex-wrap items-baseline justify-between gap-2">
            <div>
              <h2 class="font-medium">{{ a.name }}</h2>
              <p class="text-sm text-gray-400">
                {{ t(`real.accounts.types.${a.type}`) }}<template v-if="a.institution"> · {{ a.institution }}</template>
              </p>
            </div>
            <p class="text-lg" data-test="balance">
              <span v-if="a.type === 'CreditCard'" class="mr-1 text-sm text-gray-400">{{ t("real.accounts.debt") }}</span>
              <Money :value="a.balance" />
            </p>
          </div>
          <div class="mt-2 flex flex-wrap gap-2">
            <button type="button" :class="btn" :disabled="store.busy" data-test="rename" @click="startRename(a)">
              {{ t("real.accounts.rename") }}
            </button>
            <button
              type="button"
              :class="btn"
              :disabled="store.busy"
              data-test="register-balance"
              @click="checkpointFor = checkpointFor === a.id ? null : a.id"
            >
              {{ t("real.accounts.registerBalance") }}
            </button>
            <button v-if="confirmArchive !== a.id" type="button" :class="btn" :disabled="store.busy" data-test="archive" @click="confirmArchive = a.id">
              {{ t("real.accounts.archive") }}
            </button>
          </div>
          <div v-if="confirmArchive === a.id" class="mt-2 text-sm" role="group" :aria-label="t('real.accounts.archive')">
            <p>{{ t("real.accounts.archiveConfirm") }}</p>
            <div class="mt-1 flex gap-2">
              <button type="button" :class="btn" :disabled="store.busy" data-test="archive-yes" @click="archive(a)">
                {{ t("real.accounts.archiveYes") }}
              </button>
              <button type="button" :class="btn" :disabled="store.busy" @click="confirmArchive = null">{{ t("real.accounts.cancel") }}</button>
            </div>
          </div>
          <CheckpointForm
            v-if="checkpointFor === a.id"
            :account-id="a.id"
            :busy="store.busy"
            @submit="(d: string, v: string) => checkpoint(a, d, v)"
            @cancel="checkpointFor = null"
          />
        </li>
      </ul>

      <h2 class="mt-8 text-lg font-medium">{{ t("real.accounts.newTitle") }}</h2>
      <AccountForm ref="form" class="mt-2" :busy="store.busy" @submit="create" />

      <div v-if="store.archived.length" class="mt-8">
        <button type="button" :class="btn" :aria-expanded="showArchived" aria-controls="archived-list" data-test="toggle-archived" @click="showArchived = !showArchived">
          {{ t("real.accounts.archivedSection") }} ({{ store.archived.length }})
        </button>
        <ul v-show="showArchived" id="archived-list" class="mt-2 space-y-2" data-test="archived">
          <li v-for="a in store.archived" :key="a.id" class="flex flex-wrap items-center justify-between gap-2 rounded border border-gray-700 p-3">
            <span>{{ a.name }} · <Money :value="a.balance" /></span>
            <button type="button" :class="btn" :disabled="store.busy" data-test="unarchive" @click="unarchive(a)">
              {{ t("real.accounts.unarchive") }}
            </button>
          </li>
        </ul>
      </div>
    </template>
  </section>
</template>
