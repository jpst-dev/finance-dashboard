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
          <div class="flex items-center">
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
import { ref, onMounted } from "vue";
import { useRoute } from "vue-router";

const route = useRoute();
const isDarkMode = ref(false);

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
