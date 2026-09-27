// Generates windows/DMShot/Resources/TrayIcon.ico from the modern DM Screenshot
// SVG source. The tray icon intentionally uses the same capture-corners and
// capture-aperture mark as the app icon instead of the legacy tray glyph.
//
// Deps: rsvg-convert (librsvg) on PATH.
// Run:  node windows/tools/make-tray-icon.mjs

import { readFileSync, writeFileSync, mkdtempSync, rmSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';

const toolsDir = dirname(fileURLToPath(import.meta.url));
const repo = dirname(dirname(toolsDir));
const src = join(repo, 'mac', 'Resources', 'AppIcon.svg');
const out = join(repo, 'windows', 'DMShot', 'Resources', 'TrayIcon.ico');

const requiredSourceMarkers = [
  'screenshot-color-modern-mark',
  'capture-corners',
  'capture-aperture'
];
const WIN_FILL = 1.214;
const sizes = [16, 20, 24, 32, 40, 48, 64];

const source = readFileSync(src, 'utf8');
for (const marker of requiredSourceMarkers) {
  if (!source.includes(marker)) {
    throw new Error(`AppIcon.svg is missing ${marker}`);
  }
}

const inner = source
  .replace(/^[\s\S]*?<svg[^>]*>/, '')
  .replace(/<\/svg>\s*$/, '');
const winSvg =
  '<svg width="1024" height="1024" viewBox="0 0 1024 1024" xmlns="http://www.w3.org/2000/svg">' +
  '<g transform="translate(512 512) scale(' + WIN_FILL + ') translate(-512 -512)">' +
  inner +
  '</g></svg>';

const work = mkdtempSync(join(tmpdir(), 'dmscreenshot-tray-ico-'));
try {
  const svgPath = join(work, 'tray.svg');
  writeFileSync(svgPath, winSvg);

  const frames = sizes.map((size) => {
    const png = join(work, size + '.png');
    execFileSync('rsvg-convert', ['-w', String(size), '-h', String(size), svgPath, '-o', png]);
    return { size, data: readFileSync(png) };
  });

  const header = Buffer.alloc(6 + 16 * frames.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(frames.length, 4);
  let offset = 6 + 16 * frames.length;
  frames.forEach((frame, index) => {
    const entry = 6 + index * 16;
    header.writeUInt8(frame.size >= 256 ? 0 : frame.size, entry);
    header.writeUInt8(frame.size >= 256 ? 0 : frame.size, entry + 1);
    header.writeUInt8(0, entry + 2);
    header.writeUInt8(0, entry + 3);
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(frame.data.length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += frame.data.length;
  });
  writeFileSync(out, Buffer.concat([header, ...frames.map((frame) => frame.data)]));
  console.log('wrote ' + out + ' (' + frames.length + ' frames: ' + sizes.join(', ') + ')');
} finally {
  rmSync(work, { recursive: true, force: true });
}
