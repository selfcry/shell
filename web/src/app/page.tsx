import { ParticleBackground } from "@/components/ParticleBackground"
import { Hero } from "@/components/Hero"
import { ClubsPreview } from "@/components/ClubsPreview"
import { AboutNetwork } from "@/components/AboutNetwork"
import { ReviewsCarousel } from "@/components/ReviewsCarousel"
import { Bonuses } from "@/components/Bonuses"
import { CTASection } from "@/components/CTASection"
import { MotionReveal } from "@/components/MotionReveal"

export default function Home() {
  return (
    <>
      <ParticleBackground />
      <Hero />
      <MotionReveal>
        <ClubsPreview />
      </MotionReveal>
      <MotionReveal delay={60}>
        <AboutNetwork />
      </MotionReveal>
      <MotionReveal delay={80}>
        <ReviewsCarousel />
      </MotionReveal>
      <MotionReveal delay={120}>
        <Bonuses />
      </MotionReveal>
      <MotionReveal delay={160}>
        <CTASection />
      </MotionReveal>
    </>
  )
}
