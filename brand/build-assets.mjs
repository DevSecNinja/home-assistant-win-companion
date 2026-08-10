// Generates every shipped brand asset from the vector masters in brand/src.
//
// Nothing this script writes should ever be edited by hand: change a master,
// re-run `.\scripts\build-brand-assets.ps1`, and commit the regenerated output.
//
//   node brand/build-assets.mjs [--check]
//
// --check regenerates into memory and fails if any committed asset differs,
// which is what CI uses to prove the assets still match the masters.

import sharp from 'sharp';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import path from 'node:path';

const BRAND = path.resolve(import.meta.dirname);
const REPO = path.resolve(BRAND, '..');
const SRC = path.join(BRAND, 'src');
const DIST = path.join(BRAND, 'dist');
const APP_ASSETS = path.join(REPO, 'src', 'WindowsCompanion.App', 'Assets');

const CHECK = process.argv.includes('--check');

export const PALETTE = {
  window: '#2DD4BF',
  companion: '#F59E0B',
  ink: '#0F2E2A',
  surface: '#FFFFFF',
};

// Density high enough that the 16-unit master rasterises exactly at 512 px.
const DENSITY = 2400;

const failures = [];
let written = 0;

async function emit(target, buffer, { deterministic = true } = {}) {
  await mkdir(path.dirname(target), { recursive: true });
  const rel = path.relative(REPO, target).replace(/\\/g, '/');
  if (CHECK) {
    // Assets whose bytes depend on the host's installed fonts cannot be
    // compared across machines, so --check only asserts that they exist.
    if (!existsSync(target)) {
      failures.push(`missing: ${rel}`);
      return;
    }
    if (!deterministic) return;

    const current = await readFile(target);
    if (current.equals(buffer)) return;

    // PNG compression is not reproducible across libvips/zlib builds: the same
    // pixels can encode to different bytes on a different machine. Compare the
    // decoded image instead, which still fails loudly when the artwork itself
    // changes. Vector and ICO outputs stay a strict byte comparison.
    if (target.endsWith('.png') && (await pixelsEqual(current, buffer))) return;

    failures.push(`stale: ${rel}`);
    return;
  }
  await writeFile(target, buffer);
  written += 1;
  console.log(`  ${rel}`);
}

async function pixelsEqual(a, b) {
  try {
    const [left, right] = await Promise.all(
      [a, b].map((input) =>
        sharp(input).ensureAlpha().raw().toBuffer({ resolveWithObject: true }),
      ),
    );

    return (
      left.info.width === right.info.width &&
      left.info.height === right.info.height &&
      left.data.equals(right.data)
    );
  } catch {
    return false;
  }
}

const readMaster = (name) => readFile(path.join(SRC, name), 'utf8');

// Recolours a master to a single flat colour. The palette hexes are the only
// colours the masters use, so this substitution is exact rather than heuristic.
function monochrome(svg, colour) {
  return svg
    .replaceAll(PALETTE.window, colour)
    .replaceAll(PALETTE.companion, colour);
}

function render(svg, width, height = width) {
  return sharp(Buffer.from(svg), { density: DENSITY })
    .resize(width, height, {
      fit: 'contain',
      background: { r: 0, g: 0, b: 0, alpha: 0 },
    })
    .png({ compressionLevel: 9 })
    .toBuffer();
}

// Places the mark on a fixed canvas at a given fraction of the shorter edge.
// Windows tile assets need the mark inset rather than bled to the edges.
async function onCanvas(svg, width, height, coverage, background = null) {
  const mark = Math.round(Math.min(width, height) * coverage);
  const rendered = await render(svg, mark);
  const canvas = sharp({
    create: {
      width,
      height,
      channels: 4,
      background: background ?? { r: 0, g: 0, b: 0, alpha: 0 },
    },
  });
  return canvas
    .composite([
      {
        input: rendered,
        left: Math.round((width - mark) / 2),
        top: Math.round((height - mark) / 2),
      },
    ])
    .png({ compressionLevel: 9 })
    .toBuffer();
}

const markSvg = await readMaster('mark.svg');
const mark16Svg = await readMaster('mark-16.svg');
const updateMarkSvg = await readMaster('mark-update.svg');
const updateMark16Svg = await readMaster('mark-update-16.svg');

// ---------------------------------------------------------------------------
// Windows application icon
// ---------------------------------------------------------------------------
// Every scaling factor a Windows tray icon can be asked for (100/125/150/200%
// of 16 and 24 px) gets an exact entry, so the shell never has to rescale.
// 16 px comes from the hand-hinted master; larger sizes come from the full one.
//
// Entries up to 64 px are written as uncompressed 32-bit DIBs, which every
// Windows icon consumer understands. 128 and 256 px are written as PNG, which
// is the conventional encoding at those sizes and keeps the file small.
const ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const ICO_PNG_FROM = 128;

// Builds the BITMAPINFOHEADER + bottom-up BGRA + AND mask payload for one
// icon entry. The height field is doubled because the format expects the
// colour bitmap and the 1-bpp mask stacked.
//
// The mask must be derived from the alpha channel rather than left blank:
// consumers that draw the icon through a non-alpha-blended path fall back to
// it, and an all-zero mask means "fully opaque", which paints the transparent
// background as solid black.
function encodeIcoDib(rgba, size) {
  const header = Buffer.alloc(40);
  const xor = Buffer.alloc(size * size * 4);
  const maskStride = Math.ceil(size / 32) * 4;
  const and = Buffer.alloc(maskStride * size);

  for (let y = 0; y < size; y += 1) {
    const sourceRow = (size - 1 - y) * size * 4;
    const targetRow = y * size * 4;
    for (let x = 0; x < size; x += 1) {
      const s = sourceRow + x * 4;
      const t = targetRow + x * 4;
      xor[t] = rgba[s + 2];
      xor[t + 1] = rgba[s + 1];
      xor[t + 2] = rgba[s];
      xor[t + 3] = rgba[s + 3];
      if (rgba[s + 3] === 0) {
        and[y * maskStride + (x >> 3)] |= 0x80 >> (x & 7);
      }
    }
  }

  header.writeUInt32LE(40, 0);
  header.writeInt32LE(size, 4);
  header.writeInt32LE(size * 2, 8);
  header.writeUInt16LE(1, 12);
  header.writeUInt16LE(32, 14);
  header.writeUInt32LE(0, 16);
  header.writeUInt32LE(xor.length + and.length, 20);

  return Buffer.concat([header, xor, and]);
}

function encodeIco(entries) {
  const directory = Buffer.alloc(6 + entries.length * 16);
  directory.writeUInt16LE(0, 0);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(entries.length, 4);

  let offset = directory.length;
  entries.forEach((entry, index) => {
    const at = 6 + index * 16;
    directory.writeUInt8(entry.size >= 256 ? 0 : entry.size, at);
    directory.writeUInt8(entry.size >= 256 ? 0 : entry.size, at + 1);
    directory.writeUInt8(0, at + 2);
    directory.writeUInt8(0, at + 3);
    directory.writeUInt16LE(1, at + 4);
    directory.writeUInt16LE(32, at + 6);
    directory.writeUInt32LE(entry.payload.length, at + 8);
    directory.writeUInt32LE(offset, at + 12);
    offset += entry.payload.length;
  });

  return Buffer.concat([directory, ...entries.map((entry) => entry.payload)]);
}

async function buildIco(fullSvg, hintedSvg) {
  const entries = [];
  for (const size of ICO_SIZES) {
    const svg = size === 16 ? hintedSvg : fullSvg;
    const png = await render(svg, size);
    if (size >= ICO_PNG_FROM) {
      entries.push({ size, payload: png });
    } else {
      const rgba = await sharp(png).ensureAlpha().raw().toBuffer();
      entries.push({ size, payload: encodeIcoDib(rgba, size) });
    }
  }
  return encodeIco(entries);
}

console.log('Windows application icons');
await emit(path.join(APP_ASSETS, 'AppIcon.ico'), await buildIco(markSvg, mark16Svg));
await emit(
  path.join(APP_ASSETS, 'UpdateIcon.ico'),
  await buildIco(updateMarkSvg, updateMark16Svg),
);

// ---------------------------------------------------------------------------
// Windows packaging assets
// ---------------------------------------------------------------------------
// Coverage values follow the Windows icon design guidance: the 44x44 app list
// icon and the target-size icons keep a small margin, the tile assets keep a
// large one so the mark is not clipped by tile padding.
console.log('Windows packaging assets');
const APP_TILE_ASSETS = [
  { file: 'Square44x44Logo.scale-200.png', width: 88, height: 88, coverage: 0.86 },
  { file: 'Square44x44Logo.targetsize-24_altform-unplated.png', width: 24, height: 24, coverage: 1 },
  { file: 'Square44x44Logo.targetsize-48_altform-lightunplated.png', width: 48, height: 48, coverage: 1 },
  { file: 'Square150x150Logo.scale-200.png', width: 300, height: 300, coverage: 0.5 },
  { file: 'Wide310x150Logo.scale-200.png', width: 620, height: 300, coverage: 0.5 },
  { file: 'StoreLogo.png', width: 50, height: 50, coverage: 0.9 },
  { file: 'SplashScreen.scale-200.png', width: 1240, height: 600, coverage: 0.4 },
];

for (const asset of APP_TILE_ASSETS) {
  const svg = Math.min(asset.width, asset.height) * asset.coverage <= 20 ? mark16Svg : markSvg;
  await emit(
    path.join(APP_ASSETS, asset.file),
    await onCanvas(svg, asset.width, asset.height, asset.coverage),
  );
}

// ---------------------------------------------------------------------------
// Distributable brand artwork
// ---------------------------------------------------------------------------
console.log('Brand artwork');
await emit(path.join(DIST, 'mark.svg'), Buffer.from(markSvg, 'utf8'));
await emit(path.join(DIST, 'mark-16.svg'), Buffer.from(mark16Svg, 'utf8'));
await emit(
  path.join(DIST, 'mark-mono-dark.svg'),
  Buffer.from(monochrome(markSvg, '#000000'), 'utf8'),
);
await emit(
  path.join(DIST, 'mark-mono-light.svg'),
  Buffer.from(monochrome(markSvg, '#FFFFFF'), 'utf8'),
);

for (const size of [512, 256, 128, 64, 48, 32, 24, 16]) {
  await emit(
    path.join(DIST, `mark-${size}.png`),
    await render(size === 16 ? mark16Svg : markSvg, size),
  );
}
await emit(
  path.join(DIST, 'mark-mono-dark-256.png'),
  await render(monochrome(markSvg, '#000000'), 256),
);
await emit(
  path.join(DIST, 'mark-mono-light-256.png'),
  await render(monochrome(markSvg, '#FFFFFF'), 256),
);

// ---------------------------------------------------------------------------
// GitHub social preview
// ---------------------------------------------------------------------------
// GitHub renders the social preview at 1280x640 and crops nothing, so the
// composition is built at exactly that size.
console.log('GitHub social preview');

const escapeXml = (value) =>
  value.replace(/[<>&]/g, (c) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;' })[c]);

const socialText = `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="640">
  <rect width="1280" height="640" fill="${PALETTE.surface}" />
  <rect x="0" y="600" width="1280" height="40" fill="${PALETTE.window}" />
  <text x="440" y="286" font-family="Segoe UI Variable Display, Segoe UI, Selawik, Arial, sans-serif"
        font-size="70" font-weight="600" fill="${PALETTE.ink}">Windows Companion</text>
  <text x="440" y="372" font-family="Segoe UI Variable Display, Segoe UI, Selawik, Arial, sans-serif"
        font-size="70" font-weight="600" fill="${PALETTE.ink}">for ${escapeXml('Home Assistant')}</text>
  <text x="440" y="438" font-family="Segoe UI Variable Text, Segoe UI, Selawik, Arial, sans-serif"
        font-size="30" fill="#5B6B69">PC sensors and notifications for your desktop.</text>
  <text x="128" y="556" font-family="Segoe UI Variable Text, Segoe UI, Selawik, Arial, sans-serif"
        font-size="22" fill="#8A9997">Independent project. Not affiliated with or endorsed by the Open Home Foundation.</text>
</svg>`;

// The social preview is the one generated asset that is not reproducible
// byte-for-byte: it rasterises real text, so its pixels depend on which font
// file the host resolves. --check therefore only asserts that it exists.
const socialMark = await render(markSvg, 264);
await emit(
  path.join(DIST, 'social-preview.png'),
  await sharp(Buffer.from(socialText))
    .composite([{ input: socialMark, left: 128, top: 188 }])
    .png({ compressionLevel: 9 })
    .toBuffer(),
  { deterministic: false },
);

// ---------------------------------------------------------------------------
if (CHECK) {
  if (failures.length > 0) {
    console.error('\nGenerated brand assets are out of date:');
    for (const failure of failures) console.error(`  ${failure}`);
    console.error('\nRun .\\scripts\\build-brand-assets.ps1 and commit the result.');
    process.exit(1);
  }
  console.log('\nAll generated brand assets match the masters.');
} else {
  console.log(`\nWrote ${written} asset(s) from brand/src.`);
}
