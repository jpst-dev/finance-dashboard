import type { Config } from "tailwindcss";

// Identidade "Grafite + lima": os componentes usam as paletas padrão do Tailwind
// (gray, blue, green, red), então redefini-las aqui aplica o tema no app inteiro.
const lima = { 50: "#1a2210", 100: "#222d12", 200: "#e8fca8", 300: "#ddfa86", 400: "#d2f75c", 500: "#c6f432", 600: "#c6f432", 700: "#b4e01f", 800: "#9cc414", 900: "#1f2a0e" };

export default {
  content: ["./index.html", "./src/**/*.{vue,js,ts,jsx,tsx}"],
  theme: {
    extend: {
      colors: {
        background: "hsl(var(--background))",
        foreground: "hsl(var(--foreground))",
        border: "hsl(var(--border))",
        gray: { 50: "#f2f4f7", 100: "#e4e7ec", 200: "#c9ced6", 300: "#b3bac5", 400: "#8b93a1", 500: "#6b7380", 600: "#2a2f37", 700: "#1d2128", 800: "#121418", 900: "#0d0f12", 950: "#0a0b0d" },
        blue: lima,
        indigo: lima,
        purple: lima,
        green: lima,
        red: { 50: "#2a1414", 100: "#331717", 200: "#ffc2c2", 300: "#ffa3a3", 400: "#ff8585", 500: "#ff6b6b", 600: "#ff6b6b", 700: "#f25555", 800: "#d94343", 900: "#2e1515" },
      },
      fontFamily: {
        sans: ["Inter", "system-ui", "sans-serif"],
        mono: ['"JetBrains Mono"', "ui-monospace", "monospace"],
      },
    },
  },
  plugins: [],
  darkMode: "class",
} satisfies Config;
