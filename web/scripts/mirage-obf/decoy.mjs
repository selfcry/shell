import { randomBytes } from "node:crypto";

function bytesToPua(buf) {
  let s = "";
  for (let i = 0; i < buf.length; i++) s += String.fromCharCode(0xe000 + buf[i]);
  return s;
}

function sfc(str) {
  return `String.fromCharCode(${[...str].map((c) => c.charCodeAt(0)).join(",")})`;
}

/** Fake decrypt-and-run IIFE that decodes to garbage — wastes a reverse engineer's time
 *  telling it apart from the real payload. Always self-contained in try/catch. */
export function buildDecoy(Fn, TD) {
  const fakeLen = 64 + (randomBytes(1)[0] % 192);
  const fakePua = bytesToPua(randomBytes(fakeLen));
  return `;(function(){try{
var s=${JSON.stringify(fakePua)},u=new Uint8Array(s.length),k=0;
for(;k<s.length;k++)u[k]=s.charCodeAt(k)-0xE000;
var c=new ${TD}(${sfc("utf-8")}).decode(u);
(${Fn}(c))();
}catch(e){}})();`;
}

export function randomDecoyCount(min = 1, max = 3) {
  return min + (randomBytes(1)[0] % (max - min + 1));
}
