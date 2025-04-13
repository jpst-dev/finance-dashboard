import { createRouter, createWebHistory } from "vue-router";
import Dashboard from "@/pages/Dashboard.vue";
import Portfolio from "@/pages/Portfolio.vue";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: "/",
      name: "Dashboard",
      component: Dashboard,
    },
    {
      path: "/portfolio",
      name: "Portfolio",
      component: Portfolio,
    },
  ],
});

export default router;
