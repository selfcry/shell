const path = require("path");
const { pathToFileURL } = require("url");

class MirageObfWebpackPlugin {
  apply(compiler) {
    const { webpack } = compiler;
    const { Compilation, sources } = webpack;
    compiler.hooks.compilation.tap("MirageObfWebpackPlugin", (compilation) => {
      compilation.hooks.processAssets.tapPromise(
        {
          name: "MirageObfWebpackPlugin",
          stage: Compilation.PROCESS_ASSETS_STAGE_OPTIMIZE_SIZE,
        },
        async () => {
          const mod = await import(pathToFileURL(path.join(__dirname, "pack.mjs")).href);
          const { packSource, MARKER } = mod;
          let sealed = 0;
          for (const name of Object.keys(compilation.assets)) {
            if (!name.endsWith(".js")) continue;
            // Only browser chunk files under chunks/
            if (!name.includes("chunks/")) continue;
            if (
              /(webpack|polyfill|framework|main-app|main\.|react-dom|react\.|pages\/_app|_buildManifest|_ssgManifest)/i.test(
                name,
              )
            ) {
              continue;
            }
            let src;
            try {
              src = compilation.assets[name].source();
              if (typeof src !== "string") src = Buffer.from(src).toString("utf8");
            } catch {
              continue;
            }
            if (!src || src.includes(MARKER) || src.trim().length < 64) continue;
            // Keep tiny runtime stubs readable
            if (src.length < 500) continue;
            try {
              compilation.updateAsset(name, new sources.RawSource(packSource(src, name)));
              sealed++;
            } catch (err) {
              compilation.errors.push(
                new Error(`[mirage-obf] ${name}: ${err && err.message ? err.message : err}`),
              );
            }
          }
          if (sealed) console.log(`[mirage-obf:webpack] sealed ${sealed} chunk(s)`);
        },
      );
    });
  }
}

module.exports = { MirageObfWebpackPlugin };
