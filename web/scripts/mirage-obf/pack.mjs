import { randomBytes } from "node:crypto";
import { aes256Ctr, runtimeAesSource } from "./aes-ctr.mjs";
import { loadArts, pickArt, wrapArtComment } from "./ascii-art.mjs";
import { ALLOWED_HOSTS } from "./allowed-hosts.mjs";
import { buildDecoy, randomDecoyCount } from "./decoy.mjs";

export const MARKER = "MIRAGE_OBF";

function bytesToPua(buf) {
  let s = "";
  for (let i = 0; i < buf.length; i++) s += String.fromCharCode(0xe000 + buf[i]);
  return s;
}

function sfc(str) {
  return `String.fromCharCode(${[...str].map((c) => c.charCodeAt(0)).join(",")})`;
}

/** Split a string into shuffled fragments so payload isn't one obvious blob. */
function shatter(str, seed) {
  const size = 96 + (seed % 64);
  const parts = [];
  for (let i = 0; i < str.length; i += size) parts.push(str.slice(i, i + size));
  const order = parts.map((_, i) => i);
  let s = seed || 1;
  for (let i = order.length - 1; i > 0; i--) {
    s = (Math.imul(s, 1664525) + 1013904223) >>> 0;
    const j = s % (i + 1);
    [order[i], order[j]] = [order[j], order[i]];
  }
  return { parts: order.map((i) => parts[i]), order };
}

function buildBoot(keyBytes, ivBytes, puaPayload, seed) {
  const aesFactory = runtimeAesSource();
  const Fn = `globalThis[${sfc("Function")}]`;
  const TD = `globalThis[${sfc("TextDecoder")}]`;

  // Opaque key material as numeric arrays (no base64 / atob)
  const keyArr = `[${[...keyBytes].join(",")}]`;
  const ivArr = `[${[...ivBytes].join(",")}]`;

  const { parts, order } = shatter(puaPayload, seed);
  const partsLit = JSON.stringify(parts);
  const orderLit = JSON.stringify(order);
  const hostsLit = JSON.stringify(ALLOWED_HOSTS);

  const real = `;(function(){
var B=0xE000,P=${partsLit},O=${orderLit},R=[],i=0;
for(;i<O.length;i++)R[O[i]]=P[i];
var T=R.join(''),U=new Uint8Array(T.length),j=0;
for(;j<T.length;j++)U[j]=T.charCodeAt(j)-B;
if(typeof location!=="undefined"&&${hostsLit}.indexOf(location.hostname)<0)return;
var key=Uint8Array.from(${keyArr}),iv=Uint8Array.from(${ivArr});
var CTR=(${Fn}(${JSON.stringify(aesFactory)}))();
var X=CTR(key,iv,U);
var code=new ${TD}(${sfc("utf-8")}).decode(X);
(${Fn}(code))();
})();`;

  const decoyCount = randomDecoyCount();
  const decoys = Array.from({ length: decoyCount }, () => buildDecoy(Fn, TD));

  // Real payload placed at a random position among the decoys so it isn't always first/last.
  const insertAt = randomBytes(1)[0] % (decoyCount + 1);
  decoys.splice(insertAt, 0, real);
  return decoys.join("\n");
}

export function packSource(source, fileKey) {
  if (source.includes(MARKER)) return source;

  const arts = loadArts();
  const art = pickArt(arts);

  const key = randomBytes(32);
  const iv = randomBytes(16);

  // Visual only — runtime does not depend on reading the comment (async scripts break currentScript)
  const comment = wrapArtComment(art.text);

  const plain = Buffer.from(source, "utf8");
  const cipher = Buffer.from(aes256Ctr(key, iv, plain));
  const pua = bytesToPua(cipher);

  const seed =
    [...fileKey].reduce((a, c) => a + c.charCodeAt(0), 0) ^ plain.length;
  const boot = buildBoot(key, iv, pua, seed);
  const decoy = `/* ${art.name} */\n`;

  return comment + decoy + boot + "\n";
}
