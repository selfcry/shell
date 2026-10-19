/**
 * Maximum browser-asset packer used only when MIRAGE_OBF_MAX=1.
 * HTML is never touched. JS is executable-sealed; CSS is restored by the JS boot.
 */
import { randomBytes } from "node:crypto";
import { aes256Ctr, runtimeAesSource } from "./aes-ctr.mjs";
import { pickArt, wrapArtComment, ZW } from "./ascii-art.mjs";
import { ALLOWED_HOSTS } from "./allowed-hosts.mjs";
import { buildDecoy, randomDecoyCount } from "./decoy.mjs";

const PUA0 = 0xe000;

function toPua(bytes) {
  let out = "";
  for (const byte of bytes) out += String.fromCharCode(PUA0 + (byte & 255));
  return out;
}

function shuffleString(value, seedBytes) {
  const chars = Array.from(value);
  let state =
    ((seedBytes[0] << 24) |
      (seedBytes[1] << 16) |
      (seedBytes[2] << 8) |
      seedBytes[3]) >>>
    0;
  for (let i = chars.length - 1; i > 0; i--) {
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    const j = state % (i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  return { text: chars.join(""), seed: seedBytes.subarray(0, 4) };
}

function streamXor(input, token) {
  let state = 0;
  for (let i = 0; i < token.length; i++) {
    state = (Math.imul(state, 31) + token.charCodeAt(i)) >>> 0;
  }
  const output = Buffer.alloc(input.length);
  for (let i = 0; i < input.length; i++) {
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    output[i] = input[i] ^ (state & 255);
  }
  return output;
}

function embedStegoNoisy(artText, secretBytes, token) {
  const real = [];
  for (const byte of secretBytes) {
    for (let shift = 6; shift >= 0; shift -= 2) real.push(ZW[(byte >> shift) & 3]);
  }

  let state = 0;
  for (let i = 0; i < token.length; i++) {
    state = (Math.imul(state, 31) + token.charCodeAt(i)) >>> 0;
  }

  const stream = [];
  for (const mark of real) {
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    const decoys = 1 + (state % 3);
    for (let i = 0; i < decoys; i++) {
      state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
      stream.push(ZW[state & 3]);
    }
    stream.push(mark);
  }

  let markIndex = 0;
  let glyphIndex = 0;
  let output = "";
  for (const char of artText.replaceAll("*/", "* /")) {
    output += char;
    if (markIndex >= stream.length || /\s/.test(char)) continue;
    if (++glyphIndex % 2 === 0) output += stream[markIndex++];
  }
  while (markIndex < stream.length) output += stream[markIndex++];
  return output;
}

function xorSeal(value, key) {
  const input = Buffer.from(value, "utf8");
  const output = Buffer.alloc(input.length);
  for (let i = 0; i < input.length; i++) output[i] = input[i] ^ key[i % key.length];
  return output.toString("base64");
}

function unshuffleRuntime() {
  return `function U(a,e){a=Array.from(a);var s=(e[0]<<24|e[1]<<16|e[2]<<8|e[3])>>>0,i,j,t,p=[];for(i=a.length-1;i>0;i--){s=(Math.imul(s,1664525)+1013904223)>>>0;j=s%(i+1);p.push(j)}for(i=1;i<a.length;i++){j=p.pop();t=a[i];a[i]=a[j];a[j]=t}return a.join("")}`;
}

function streamXorRuntime() {
  return `function SX(p,t){var h=0,k,i,o=new Uint8Array(p.length);for(k=0;k<t.length;k++)h=(Math.imul(h,31)+t.charCodeAt(k))>>>0;for(i=0;i<p.length;i++){h=(Math.imul(h,1664525)+1013904223)>>>0;o[i]=p[i]^(h&255)}return o}`;
}

function cssRestoreRuntime() {
  return `function C(aes){if(typeof document==="undefined")return;var g=globalThis;if(g.__MGCSS_R)return g.__MGCSS_R();function one(l){if(!l||l.dataset.mgcss||!l.href)return;l.dataset.mgcss="1";try{var x=new XMLHttpRequest();x.open("GET",l.href,false);x.send(null);var q=x.responseText,m=/MGCSS:(MGCX_[0-9a-f]+):([0-9a-f]{8}):(\\d+)\\n/.exec(q);if(!m)return;var t=m[1],n=+m[3],z=q.lastIndexOf("\\n*/"),d=q.slice(z-n,z),art=q.slice(m.index+m[0].length,z-n),sec=E(art,t),hex=m[2],seed=new Uint8Array(4);for(var i=0;i<4;i++)seed[i]=parseInt(hex.slice(i*2,i*2+2),16);var mid=aes(sec.subarray(0,32),sec.subarray(32,48),P(U(d,seed))),css=new TextDecoder("utf-8").decode(SX(mid,t)),st=document.createElement("style");st.dataset.mgcss=l.href;st.textContent=css;l.after(st);l.disabled=true}catch(e){l.dataset.mgcss="error"}}function run(){var a=document.querySelectorAll('link[rel~="stylesheet"]');for(var i=0;i<a.length;i++)one(a[i])}g.__MGCSS_R=run;run();if(typeof MutationObserver!=="undefined")new MutationObserver(run).observe(document.documentElement,{childList:true,subtree:true})}`;
}

function buildBoot({ token, sealedAes, shuffleSeed, shards }) {
  const seedLiteral = `[${[...shuffleSeed].join(",")}]`;
  const shardsLiteral = shards.map((part) => JSON.stringify(part)).join(",");
  const hostsLiteral = JSON.stringify(ALLOWED_HOSTS);
  return `(function(){var T=${JSON.stringify(token)},Z=["\\u200b","\\u200c","\\u200d","\\ufeff"],A=${JSON.stringify(sealedAes)},R=${seedLiteral},Q=[${shardsLiteral}],H=${hostsLiteral};
if(typeof location!=="undefined"&&H.indexOf(location.hostname)<0)return;
${unshuffleRuntime()}
${streamXorRuntime()}
function X(){try{if(typeof document!=="undefined"&&document.currentScript){var c=document.currentScript;if(c.src){var x=new XMLHttpRequest();x.open("GET",c.src,false);x.send(null);if(x.responseText&&x.responseText.indexOf(T)!==-1)return x.responseText}if(c.text&&c.text.indexOf(T)!==-1)return c.text}}catch(e){}try{var s=document.getElementsByTagName("script");for(var i=0;i<s.length;i++){if(!s[i].src)continue;try{var y=new XMLHttpRequest();y.open("GET",s[i].src,false);y.send(null);if(y.responseText&&y.responseText.indexOf(T)!==-1)return y.responseText}catch(e2){}}}catch(e3){}return""}
function W(t){var i=t.indexOf("/*!"),j=t.indexOf("*/",i+1);return i<0||j<0?"":t.slice(i,j+2)}
function E(b,t){var m=[],i,d;for(i=0;i<b.length;i++){d=Z.indexOf(b[i]);if(d>=0)m.push(d)}var h=0,k,o=[],r=0,n;for(k=0;k<t.length;k++)h=(Math.imul(h,31)+t.charCodeAt(k))>>>0;while(r<m.length){h=(Math.imul(h,1664525)+1013904223)>>>0;n=1+h%3;for(k=0;k<n;k++){h=(Math.imul(h,1664525)+1013904223)>>>0;r++}if(r>=m.length)break;o.push(m[r++])}var a=new Uint8Array(48),v,s,p=0;for(i=0;i+3<o.length&&p<48;i+=4){v=0;for(s=0;s<4;s++)v=v<<2|o[i+s];a[p++]=v}return a}
function Y(v,k){var r=atob(v),o=new Uint8Array(r.length),i,s="";for(i=0;i<r.length;i++){o[i]=r.charCodeAt(i)^k[i%k.length];s+=String.fromCharCode(o[i])}return s}
function P(s){var a=new Uint8Array(s.length),i;for(i=0;i<s.length;i++)a[i]=s.charCodeAt(i)-${PUA0};return a}
${cssRestoreRuntime()}
try{var src=X();if(!src)return;var sec=E(W(src),T),aes=Function(Y(A,sec))();C(aes);var mid=aes(sec.subarray(0,32),sec.subarray(32,48),P(U(Q.join(""),R))),code=new TextDecoder("utf-8").decode(SX(mid,T));Function(code)()}catch(e){}})();`;
}

function encryptAsset(source, prefix) {
  const token = `${prefix}_${randomBytes(10).toString("hex")}`;
  const secret = randomBytes(48);
  const prepared = streamXor(Buffer.from(source, "utf8"), token);
  const cipher = aes256Ctr(secret.subarray(0, 32), secret.subarray(32, 48), prepared);
  const { text, seed } = shuffleString(toPua(cipher), randomBytes(4));
  return { token, secret, cipher, text, seed };
}

export function packMax(source) {
  const encrypted = encryptAsset(source, "MGFX");
  const art = pickArt();
  const artBlock = wrapArtComment(
    embedStegoNoisy(art.text, encrypted.secret, encrypted.token),
  ).replace("MIRAGE_OBF", `MIRAGE_OBF ${encrypted.token}`);

  const shardCount = 5 + (encrypted.seed[0] % 5);
  const shardLength = Math.ceil(encrypted.text.length / shardCount);
  const shards = [];
  for (let i = 0; i < encrypted.text.length; i += shardLength) {
    shards.push(encrypted.text.slice(i, i + shardLength));
  }

  const realBoot = buildBoot({
    token: encrypted.token,
    sealedAes: xorSeal(runtimeAesSource(), encrypted.secret),
    shuffleSeed: encrypted.seed,
    shards,
  });
  const decoyCount = randomDecoyCount();
  const decoys = Array.from({ length: decoyCount }, () => buildDecoy("Function", "TextDecoder"));
  const insertAt = randomBytes(1)[0] % (decoyCount + 1);
  decoys.splice(insertAt, 0, realBoot);

  return {
    code:
      artBlock +
      `/* decoy ${randomBytes(4).toString("hex")} ${toPua(randomBytes(64))} */\n` +
      decoys.join("\n"),
    meta: { token: encrypted.token, art: art.name, bytes: encrypted.cipher.length },
  };
}

export function packCssMax(source) {
  const encrypted = encryptAsset(source, "MGCX");
  const art = pickArt();
  const stegoArt = embedStegoNoisy(art.text, encrypted.secret, encrypted.token);
  const seedHex = Buffer.from(encrypted.seed).toString("hex");
  return {
    code:
      `/*!MGCSS:${encrypted.token}:${seedHex}:${encrypted.text.length}\n` +
      stegoArt +
      "\n" +
      encrypted.text +
      "\n*/",
    meta: { token: encrypted.token, art: art.name, bytes: encrypted.cipher.length },
  };
}
