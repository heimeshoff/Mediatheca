import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
    plugins: [
        react({ jsxRuntime: "classic", include: /\.(fs\.js|jsx?)$/ }),
        tailwindcss(),
    ],
    server: {
        proxy: {
            "/api": {
                target: "http://localhost:5000",
                changeOrigin: true,
            },
        },
    },
    build: {
        outDir: "../../deploy/public",
        emptyOutDir: true,
    },
});
