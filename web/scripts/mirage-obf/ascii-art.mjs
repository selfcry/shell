import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { randomInt } from "node:crypto";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(__dirname, "../..");

const ART_FILES = [
  path.join(ROOT, "art.txt"),
  path.join(ROOT, "art2.txt"),
];

/** 2-bit invisible alphabet */
export const ZW = ["\u200b", "\u200c", "\u200d", "\ufeff"];

export function loadArts() {
  return ART_FILES.map((f) => {
    if (!fs.existsSync(f)) throw new Error(`missing art file: ${f}`);
    return {
      name: path.basename(f),
      text: fs.readFileSync(f, "utf8").replace(/\r\n/g, "\n").replace(/\s+$/g, ""),
    };
  });
}

export function pickArt(arts = loadArts()) {
  return arts[randomInt(arts.length)];
}

/**
 * Embed exact secretBytes as zero-width dibits after visible glyphs.
 * Runtime reads the first secretBytes.length * 4 zero-width marks (2 bits each).
 */
export function embedStego(artText, secretBytes) {
  const marks = [];
  for (const b of secretBytes) {
    for (let shift = 6; shift >= 0; shift -= 2) {
      marks.push(ZW[(b >> shift) & 3]);
    }
  }

  let mi = 0;
  let out = "";
  let step = 0;
  for (const ch of artText) {
    out += ch;
    if (mi >= marks.length) continue;
    if (/\s/.test(ch)) continue;
    step++;
    if (step % 2 !== 0) continue;
    out += marks[mi++];
  }
  // leftover marks appended (short art / whitespace-heavy)
  while (mi < marks.length) out += marks[mi++];
  return out;
}

export function wrapArtComment(stegoArt) {
  const body = stegoArt
    .split("\n")
    .map((line) => " * " + line)
    .join("\n");
  return `/*!\n * MIRAGE_OBF\n${body}\n */\n`;
}
