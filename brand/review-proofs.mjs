// Renders review proofs for the application mark so small-size legibility can be
// judged before committing a change to the masters.
//
//   node brand/review-proofs.mjs
//
// Writes brand/proofs/ (git-ignored): the mark at the four notification-area
// sizes, magnified 8x with nearest-neighbour so per-pixel artefacts are visible,
// on both a light and a dark taskbar, plus actual-size samples underneath.

import sharp from 'sharp';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';

const BRAND = path.resolve(import.meta.dirname);
const SRC = path.join(BRAND, 'src');
const OUT = path.join(BRAND, 'proofs');

const LIGHT = { r: 243, g: 243, b: 243, alpha: 1 };
const DARK = { r: 32, g: 32, b: 32, alpha: 1 };
const TRAY = [16, 20, 24, 32];
const ZOOM_BOX = 128;
const PAD = 16;

const markSvg = await readFile(path.join(SRC, 'mark.svg'), 'utf8');
const mark16Svg = await readFile(path.join(SRC, 'mark-16.svg'), 'utf8');

const monochrome = (svg, colour) =>
  svg.replaceAll('#2DD4BF', colour).replaceAll('#F59E0B', colour);

const raster = (svg, size) =>
  sharp(Buffer.from(svg), { density: 2400 })
    .resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
    .png()
    .toBuffer();

async function strip(background, colour) {
  const cells = [];
  for (const size of TRAY) {
    const svg = monochrome(size === 16 ? mark16Svg : markSvg, colour);
    const actual = await raster(svg, size);
    const zoomed = await sharp(actual)
      .resize(ZOOM_BOX, ZOOM_BOX, { kernel: 'nearest' })
      .png()
      .toBuffer();
    cells.push({ size, actual, zoomed });
  }

  const width = PAD + cells.length * (ZOOM_BOX + PAD);
  const height = PAD + ZOOM_BOX + PAD + 40 + PAD;
  const composites = [];
  let x = PAD;

  for (const cell of cells) {
    composites.push({ input: cell.zoomed, left: x, top: PAD });
    composites.push({
      input: cell.actual,
      left: Math.round(x + (ZOOM_BOX - cell.size) / 2),
      top: PAD + ZOOM_BOX + PAD + Math.round((40 - cell.size) / 2),
    });
    x += ZOOM_BOX + PAD;
  }

  return sharp({ create: { width, height, channels: 4, background } })
    .composite(composites)
    .png()
    .toBuffer();
}

await mkdir(OUT, { recursive: true });

const lightStrip = await strip(LIGHT, '#000000');
const darkStrip = await strip(DARK, '#FFFFFF');
const colour = await raster(markSvg, 256);

const lm = await sharp(lightStrip).metadata();
const dm = await sharp(darkStrip).metadata();
const width = Math.max(lm.width, dm.width);
const headerHeight = 256 + PAD * 2;

const sheet = await sharp({
  create: {
    width,
    height: headerHeight + lm.height + dm.height,
    channels: 4,
    background: LIGHT,
  },
})
  .composite([
    { input: colour, left: PAD, top: PAD },
    { input: lightStrip, left: 0, top: headerHeight },
    { input: darkStrip, left: 0, top: headerHeight + lm.height },
  ])
  .png()
  .toBuffer();

await writeFile(path.join(OUT, 'tray-proof.png'), sheet);
console.log(`Wrote ${path.relative(BRAND, path.join(OUT, 'tray-proof.png'))}`);
