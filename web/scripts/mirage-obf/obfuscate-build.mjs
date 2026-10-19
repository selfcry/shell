#!/usr/bin/env node
/**
 * Post-build obfuscator for Next.js client chunks.
 * Usage: node scripts/mirage-obf/obfuscate-build.mjs [.next]
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { packSource, MARKER } from "./pack.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, "../..");
const nextDir = path.resolve(root, process.argv[2] || ".next");

// Opt-in only: sealing every JS/CSS chunk costs 40-65+ Lighthouse performance
// points (measured), so it must never run silently by default.
if (process.env.MIRAGE_OBF !== "1") {
  console.log("[mirage-obf] MIRAGE_OBF!=1, skipping (shipping unobfuscated build)")
  process.exit(0)
}

const maxMode = process.env.MIRAGE_OBF_MAX === "1"
const maxPacker = maxMode ? (await import("./pack-max.mjs")).packMax : null;

const SKIP_NAME =
  /(?:^|\/)(?:polyfills[^/]*|webpack[^/]*|framework[^/]*|main-app[^/]*|_buildManifest|_ssgManifest|_clientMiddlewareManifest)\.js$/i;

function walk(dir, out = []) {
  if (!fs.existsSync(dir)) return out;
  for (const name of fs.readdirSync(dir)) {
    const full = path.join(dir, name);
    let st;
    try {
      st = fs.statSync(full);
    } catch {
      continue;
    }
    if (st.isDirectory()) walk(full, out);
    else if (name.endsWith(".js")) out.push(full);
  }
  return out;
}

function shouldProcess(file, staticRoot) {
  const rel = path.relative(staticRoot, file).replace(/\\/g, "/");
  if (maxMode) return true;
  if (SKIP_NAME.test(rel)) return false;
  return true;
}

function stripMaps(dir) {
  function walkMaps(d) {
    if (!fs.existsSync(d)) return;
    for (const name of fs.readdirSync(d)) {
      const full = path.join(d, name);
      let st;
      try {
        st = fs.statSync(full);
      } catch {
        continue;
      }
      if (st.isDirectory()) walkMaps(full);
      else if (name.endsWith(".map")) {
        fs.unlinkSync(full);
        console.log("  - map", path.relative(root, full));
      }
    }
  }
  walkMaps(dir);
}

function sealStaticDir(staticDir, label) {
  if (!fs.existsSync(staticDir)) return { ok: 0, skipped: 0, bytesIn: 0, bytesOut: 0 };
  const files = walk(staticDir).filter((f) => shouldProcess(f, staticDir));
  console.log(`[mirage-obf] sealing ${files.length} file(s) in ${label}`);

  let ok = 0;
  let skipped = 0;
  let bytesIn = 0;
  let bytesOut = 0;

  for (const file of files) {
    const src = fs.readFileSync(file, "utf8");
    if (src.includes(maxMode ? "MGFX_" : MARKER)) {
      skipped++;
      continue;
    }
    if (src.trim().length < 32) {
      skipped++;
      continue;
    }
    const rel = path.relative(staticDir, file);
    const packed = maxMode ? maxPacker(src).code : packSource(src, rel);
    fs.writeFileSync(file, packed, "utf8");
    bytesIn += Buffer.byteLength(src);
    bytesOut += Buffer.byteLength(packed);
    ok++;
    console.log(`  ✓ ${rel}  (${src.length} → ${packed.length} chars)`);
  }

  stripMaps(staticDir);
  return { ok, skipped, bytesIn, bytesOut };
}

function main() {
  const nextStatic = path.join(nextDir, "static");
  const label = path.relative(root, nextStatic) || ".next/static";

  if (!fs.existsSync(nextStatic)) {
    console.error(`[mirage-obf] no static dir found (expected ${nextStatic})`);
    process.exit(1);
  }

  const r = sealStaticDir(nextStatic, label);
  console.log(
    `[mirage-obf] ${label}: ${r.ok} sealed, ${r.skipped} skipped, ${(r.bytesIn / 1024).toFixed(1)}KB → ${(r.bytesOut / 1024).toFixed(1)}KB`,
  );
  if (r.ok === 0) {
    console.log("[mirage-obf] nothing new to seal (already obfuscated or empty)");
  }
}

main();
