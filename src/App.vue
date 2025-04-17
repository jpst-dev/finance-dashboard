<template>
  <div class="min-h-screen bg-background text-foreground">
    <nav class="bg-white dark:bg-gray-800 shadow-lg">
      <div class="max-w-7xl mx-auto px-4">
        <div class="flex justify-between h-16">
          <div class="flex items-center space-x-8">
            <h1 class="text-xl font-bold">Finance Dashboard</h1>
            <div class="hidden md:flex space-x-4">
              <router-link
                to="/"
                class="px-3 py-2 rounded-md text-sm font-medium"
                :class="[
                  $route.path === '/'
                    ? 'bg-gray-100 dark:bg-gray-700 text-gray-900 dark:text-white'
                    : 'text-gray-500 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700',
                ]"
              >
                Dashboard
              </router-link>
              <router-link
                to="/portfolio"
                class="px-3 py-2 rounded-md text-sm font-medium"
                :class="[
                  $route.path === '/portfolio'
                    ? 'bg-gray-100 dark:bg-gray-700 text-gray-900 dark:text-white'
                    : 'text-gray-500 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700',
                ]"
              >
                Portfolio
              </router-link>
            </div>
          </div>
          <div class="flex items-center space-x-4">
            <!-- Language Selector -->
            <div class="relative">
              <button
                @click="showLanguageDropdown = !showLanguageDropdown"
                class="flex items-center space-x-2 px-3 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 rounded-md"
              >
                <span>{{ currentLanguage }}</span>
                <svg
                  class="w-4 h-4"
                  fill="none"
                  stroke="currentColor"
                  viewBox="0 0 24 24"
                >
                  <path
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    stroke-width="2"
                    d="M19 9l-7 7-7-7"
                  />
                </svg>
              </button>
              <div
                v-if="showLanguageDropdown"
                class="absolute right-0 mt-2 w-32 bg-white dark:bg-gray-800 rounded-md shadow-lg z-10"
              >
                <div class="py-1">
                  <button
                    v-for="lang in languages"
                    :key="lang.code"
                    @click="changeLanguage(lang.code)"
                    class="block w-full text-left px-4 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700"
                  >
                    {{ lang.name }}
                  </button>
                </div>
              </div>
            </div>
            <!-- Dark Mode Toggle -->
            <button
              @click="toggleDarkMode"
              class="p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-gray-700"
            >
              <span v-if="isDarkMode">🌞</span>
              <span v-else>🌙</span>
            </button>
          </div>
        </div>
      </div>
    </nav>

    <main class="max-w-7xl mx-auto px-4 py-8">
      <router-view />
    </main>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from "vue";
import { useRoute } from "vue-router";
import { useI18n } from "vue-i18n";

const route = useRoute();
const { locale } = useI18n();
const isDarkMode = ref(false);
const showLanguageDropdown = ref(false);

const languages = [
  { code: "pt-BR", name: "Português" },
  { code: "en-US", name: "English" },
];

const currentLanguage = computed(() => {
  return (
    languages.find((lang) => lang.code === locale.value)?.name || "English"
  );
});

const changeLanguage = (langCode: string) => {
  locale.value = langCode;
  showLanguageDropdown.value = false;
};

const toggleDarkMode = () => {
  isDarkMode.value = !isDarkMode.value;
  document.documentElement.classList.toggle("dark");
};

onMounted(() => {
  // Check system preference for dark mode
  if (window.matchMedia("(prefers-color-scheme: dark)").matches) {
    isDarkMode.value = true;
    document.documentElement.classList.add("dark");
  }
});
</script>
