import { defineConfig } from "vite";
import fable from "vite-plugin-fable";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
    root: "./src/Client",
    plugins: [
        fable({ fsproj: "./src/Client/Client.fsproj" }),
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
