import { createRouter, createWebHistory } from "vue-router";
import Placeholder from "./Placeholder.vue";

export const realRouter = createRouter({
  history: createWebHistory(),
  routes: [{ path: "/:pathMatch(.*)*", name: "placeholder", component: Placeholder }],
});
