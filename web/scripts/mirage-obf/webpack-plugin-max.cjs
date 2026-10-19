const path = require("path");
const { pathToFileURL } = require("url");

class MirageObfMaxWebpackPlugin {
  apply(compiler) {
    const { Compilation, sources } = compiler.webpack;
    compiler.hooks.compilation.tap("MirageObfMaxWebpackPlugin", (compilation) => {
      compilation.hooks.processAssets.tapPromise(
        {
          name: "MirageObfMaxWebpackPlugin",
          stage: Compilation.PROCESS_ASSETS_STAGE_OPTIMIZE_SIZE,
        },
        async () => {
          const { packMax, packCssMax } = await import(
            pathToFileURL(path.join(__dirname, "pack-max.mjs")).href
          );
          let js = 0;
          let css = 0;
          for (const name of Object.keys(compilation.assets)) {
            const isJs = name.endsWith(".js");
            const isCss = name.endsWith(".css") && name.includes("static/css/");
            if (!isJs && !isCss) continue;

            let source;
            try {
              source = compilation.assets[name].source();
              if (typeof source !== "string") source = Buffer.from(source).toString("utf8");
            } catch {
              continue;
            }
            if (!source || source.trim().length < 64) continue;
            if (source.includes("MGFX_") || source.includes("MGCSS:")) continue;

            try {
              const packed = isJs ? packMax(source) : packCssMax(source);
              compilation.updateAsset(name, new sources.RawSource(packed.code));
              if (isJs) js++;
              else css++;
            } catch (error) {
              compilation.errors.push(
                new Error(
                  `[mirage-obf-max] ${name}: ${
                    error && error.message ? error.message : error
                  }`,
                ),
              );
            }
          }
          console.log(`[mirage-obf-max:webpack] sealed ${js} JS + ${css} CSS asset(s)`);
        },
      );
    });
  }
}

module.exports = { MirageObfMaxWebpackPlugin };
