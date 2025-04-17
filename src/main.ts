import { createApp } from "vue";
import { createPinia } from "pinia";
import App from "./App.vue";
import router from "./router";
import "./assets/main.css";
import Toast from "vue-toastification";
import "vue-toastification/dist/index.css";
import { useInvestmentStore } from "./store/investments";
import i18n from "./i18n";

const app = createApp(App);
const pinia = createPinia();

// Initialize store before mounting app
const initializeStore = async () => {
  try {
    const investmentStore = useInvestmentStore(pinia);
    await investmentStore.initializeStore();
    console.log("Store initialized successfully");
  } catch (error) {
    console.error("Error initializing store:", error);
  }
};

const toastOptions = {
  position: "top-right",
  timeout: 3000,
  closeOnClick: true,
  pauseOnFocusLoss: true,
  pauseOnHover: true,
  draggable: true,
  draggablePercent: 0.6,
  showCloseButtonOnHover: false,
  hideProgressBar: true,
  closeButton: "button",
  icon: true,
  rtl: false,
};

app.use(i18n);
app.use(pinia);
app.use(router);
app.use(Toast, toastOptions);

// Initialize store and then mount app
initializeStore()
  .then(() => {
    app.mount("#app");
  })
  .catch((error) => {
    console.error("Failed to initialize store:", error);
    // Still mount the app even if store initialization fails
    app.mount("#app");
  });
