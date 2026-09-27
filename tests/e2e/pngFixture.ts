import zlib from "node:zlib";

// books-r8cfn (verifier iteration 2): the uniform-size spec needs REAL cover
// images -- a square one and a 2:3 one -- served without any network
// dependency, so the manual `addBook` path's `CoverUrl` fetch has actual
// bytes to download rather than `null` (which only ever renders the icon
// placeholder and never touches `.poster-image--contain`/the fixed-width
// fix's actual failure mode: card width coming from its own image). Rather
// than vendor a binary fixture file or reach for an image-encoding
// dependency, this hand-builds the smallest possible valid PNG -- a solid
// rectangle, uncompressed-but-zlib-wrapped (Node's own `zlib` module) scan
// data -- from bytes. `dashboard-book-poster-cards-uniform-size.spec.ts`
// serves these off a loopback-only Node http server it starts itself, so the
// app server's `addBook` -> `httpClient.GetAsync(CoverUrl)` fetch never
// leaves the machine.

const PNG_SIGNATURE = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

function crc32(buf: Buffer): number {
    let crc = 0xffffffff;
    for (let i = 0; i < buf.length; i++) {
        crc ^= buf[i];
        for (let bit = 0; bit < 8; bit++) {
            const mask = -(crc & 1);
            crc = (crc >>> 1) ^ (0xedb88320 & mask);
        }
    }
    return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type: string, data: Buffer): Buffer {
    const typeBuf = Buffer.from(type, "ascii");
    const lengthBuf = Buffer.alloc(4);
    lengthBuf.writeUInt32BE(data.length, 0);
    const crcBuf = Buffer.alloc(4);
    crcBuf.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])), 0);
    return Buffer.concat([lengthBuf, typeBuf, data, crcBuf]);
}

/// A solid-colour, 8-bit truecolour (no alpha) PNG of the given pixel
/// dimensions -- everything a browser's `<img>` decoder needs to report
/// correct `naturalWidth`/`naturalHeight`, which is all this spec cares
/// about (the fixture's visual content is irrelevant to the layout bug).
export function makeSolidPng(width: number, height: number, rgb: [number, number, number]): Buffer {
    const ihdrData = Buffer.alloc(13);
    ihdrData.writeUInt32BE(width, 0);
    ihdrData.writeUInt32BE(height, 4);
    ihdrData[8] = 8; // bit depth
    ihdrData[9] = 2; // colour type: truecolour
    ihdrData[10] = 0; // compression
    ihdrData[11] = 0; // filter
    ihdrData[12] = 0; // interlace

    const rowBytes = 1 + width * 3; // filter-type byte + RGB pixels
    const raw = Buffer.alloc(rowBytes * height);
    for (let y = 0; y < height; y++) {
        const rowStart = y * rowBytes;
        raw[rowStart] = 0; // filter type: none
        for (let x = 0; x < width; x++) {
            const pixelStart = rowStart + 1 + x * 3;
            raw[pixelStart] = rgb[0];
            raw[pixelStart + 1] = rgb[1];
            raw[pixelStart + 2] = rgb[2];
        }
    }
    const idatData = zlib.deflateSync(raw);

    return Buffer.concat([
        PNG_SIGNATURE,
        chunk("IHDR", ihdrData),
        chunk("IDAT", idatData),
        chunk("IEND", Buffer.alloc(0)),
    ]);
}
