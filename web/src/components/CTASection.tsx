export function CTASection() {
  return (
    <section className="relative overflow-hidden px-3 py-20 sm:px-4 sm:py-32">
      <div className="cta-ambient absolute inset-0 opacity-20">
        <div className="cta-orb absolute top-1/2 left-1/2 w-[600px] h-[600px] bg-orange-500/20 rounded-full blur-[200px]" />
      </div>

      <div className="cta-panel-motion relative mx-auto max-w-4xl overflow-hidden rounded-3xl p-7 text-center glass sm:p-24">
        <div
          className="absolute inset-0 bg-cover bg-center opacity-15"
          style={{ backgroundImage: "url('/bg-blocks/5.webp')" }}
        />
        <div className="relative z-10">
        <h2 className="mb-5 text-4xl font-black sm:mb-6 sm:text-6xl">
          Готов к игре?
        </h2>
        <p className="mx-auto mb-8 max-w-lg text-base leading-relaxed text-white/60 sm:mb-10 sm:text-xl">
          Два компьютерных клуба в Пятигорске: Мираж PRO на Панагюриште, 4 и Мираж на
          Октябрьской, 46. Игровые ПК, PS5 и работа 24/7.
        </p>
        <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
          <a
            href="https://t.me/mirage_cyber26"
            target="_blank"
            rel="noopener noreferrer"
            className="motion-cta motion-cta-primary w-full rounded-xl gradient-bg px-6 py-4 text-base font-bold text-black sm:w-auto sm:px-12 sm:py-5 sm:text-xl"
          >
            Забронировать
          </a>
        </div>
        </div>
      </div>
    </section>
  )
}
