import { ArcElement, BarElement, CategoryScale, Chart as ChartJS, Legend, LinearScale, Tooltip } from "chart.js";

// Registro mínimo (só o que os gráficos da Visão geral usam), restrito a src/real.
ChartJS.register(ArcElement, BarElement, CategoryScale, LinearScale, Tooltip, Legend);
