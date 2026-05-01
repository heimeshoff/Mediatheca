// One-off icon generator for PWA — run with: node scripts/generate-pwa-icons.mjs
// Uses sharp (installed via `npm install --no-save sharp`).
import sharp from "sharp";
import { writeFileSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const outDir = resolve(__dirname, "..", "src", "Client", "public", "icons");

// dim theme colours, computed from oklch values in src/Client/index.css
const BG = "#2a303c";       // --color-base-100  → oklch(30.857% 0.023 264.149)
const STROKE = "#9fe88d";   // --color-primary   → oklch(86.133% 0.141 139.549)

// `paddingFraction` is the empty space on EACH SIDE of the 24-unit glyph,
// expressed as a fraction of the 512px canvas. So 0.10 = 10% padding (glyph fills 80% of canvas).
function buildSvg(paddingFraction) {
    const canvas = 512;
    const padPx = Math.round(canvas * paddingFraction);
    const innerSize = canvas - padPx * 2;
    const scale = innerSize / 24; // viewBox of glyph is 24x24
    return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${canvas} ${canvas}" width="${canvas}" height="${canvas}">
  <rect width="${canvas}" height="${canvas}" fill="${BG}"/>
  <g transform="translate(${padPx} ${padPx}) scale(${scale})" fill="none" stroke="${STROKE}" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
    <path d="M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"/>
    <path d="M15.91 11.672a.375.375 0 0 1 0 .656l-5.603 3.113a.375.375 0 0 1-.557-.328V8.887c0-.286.307-.466.557-.327l5.603 3.112Z"/>
  </g>
</svg>`;
}

// Tight icons (192, 512): ~10% padding so the glyph reads at favicon size.
// Maskable (512): ~20% safe-zone so Android's circular/squircle mask doesn't crop it.
const tightSvg = buildSvg(0.10);
const maskableSvg = buildSvg(0.20);

async function render(svg, size, outPath) {
    const buf = await sharp(Buffer.from(svg)).resize(size, size).png().toBuffer();
    writeFileSync(outPath, buf);
    console.log("wrote", outPath);
}

await render(tightSvg, 192, resolve(outDir, "icon-192.png"));
await render(tightSvg, 512, resolve(outDir, "icon-512.png"));
await render(maskableSvg, 512, resolve(outDir, "icon-512-maskable.png"));
console.log("done");
