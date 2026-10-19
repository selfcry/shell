"use client"

import { useEffect, useRef } from "react"

interface Particle {
  x: number
  y: number
  vx: number
  vy: number
  size: number
  alpha: number
}

const FRAME_INTERVAL = 1000 / 30

export function ParticleBackground() {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const ctx = canvas.getContext("2d")
    if (!ctx) return

    const reducedMotionQuery = window.matchMedia("(prefers-reduced-motion: reduce)")
    const mobileQuery = window.matchMedia("(max-width: 767px)")

    if (mobileQuery.matches || reducedMotionQuery.matches) return

    let animationId: number | undefined
    let particles: Particle[] = []
    let width = window.innerWidth
    let height = window.innerHeight
    let lastFrameTime = 0
    let connectionDistance = 140

    const resize = () => {
      width = window.innerWidth
      height = window.innerHeight
      const isMobile = mobileQuery.matches
      const pixelRatio = Math.min(window.devicePixelRatio || 1, isMobile ? 1 : 1.5)

      canvas.width = Math.round(width * pixelRatio)
      canvas.height = Math.round(height * pixelRatio)
      ctx.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0)

      connectionDistance = isMobile ? 100 : 140
      const density = isMobile ? 24000 : 15000
      const maxParticles = isMobile ? 28 : 64
      const count = Math.min(Math.floor((width * height) / density), maxParticles)
      particles = Array.from({ length: count }, () => ({
        x: Math.random() * width,
        y: Math.random() * height,
        vx: (Math.random() - 0.5) * 0.5,
        vy: (Math.random() - 0.5) * 0.5,
        size: Math.random() * 2 + 1,
        alpha: Math.random() * 0.5 + 0.1,
      }))
    }

    const draw = (frameScale: number) => {
      ctx.clearRect(0, 0, width, height)
      const maxDistanceSquared = connectionDistance * connectionDistance

      particles.forEach((p, i) => {
        p.x += p.vx * frameScale
        p.y += p.vy * frameScale

        if (p.x < 0 || p.x > width) p.vx *= -1
        if (p.y < 0 || p.y > height) p.vy *= -1

        ctx.beginPath()
        ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2)
        ctx.fillStyle = `rgba(139, 0, 0, ${p.alpha})`
        ctx.fill()

        for (let j = i + 1; j < particles.length; j++) {
          const dx = particles[j].x - p.x
          const dy = particles[j].y - p.y
          const distanceSquared = dx * dx + dy * dy

          if (distanceSquared < maxDistanceSquared) {
            const distance = Math.sqrt(distanceSquared)
            ctx.beginPath()
            ctx.moveTo(p.x, p.y)
            ctx.lineTo(particles[j].x, particles[j].y)
            ctx.strokeStyle = `rgba(139, 0, 0, ${0.06 * (1 - distance / connectionDistance)})`
            ctx.lineWidth = 0.5
            ctx.stroke()
          }
        }
      })
    }

    const animate = (time: number) => {
      if (document.hidden || reducedMotionQuery.matches) {
        animationId = undefined
        return
      }

      const elapsed = time - lastFrameTime
      if (elapsed >= FRAME_INTERVAL) {
        const frameScale = Math.min(elapsed / (1000 / 60), 4)
        lastFrameTime = time - (elapsed % FRAME_INTERVAL)
        draw(frameScale)
      }

      animationId = requestAnimationFrame(animate)
    }

    const startAnimation = () => {
      if (animationId !== undefined || document.hidden || reducedMotionQuery.matches) return
      lastFrameTime = performance.now()
      animationId = requestAnimationFrame(animate)
    }

    const stopAnimation = () => {
      if (animationId !== undefined) cancelAnimationFrame(animationId)
      animationId = undefined
    }

    const handleMotionChange = () => {
      stopAnimation()
      draw(0)
      startAnimation()
    }

    const handleVisibilityChange = () => {
      if (document.hidden) stopAnimation()
      else startAnimation()
    }

    const handleResize = () => {
      resize()
      draw(0)
    }

    resize()
    draw(0)
    startAnimation()
    window.addEventListener("resize", handleResize)
    document.addEventListener("visibilitychange", handleVisibilityChange)
    reducedMotionQuery.addEventListener("change", handleMotionChange)
    mobileQuery.addEventListener("change", handleResize)

    return () => {
      stopAnimation()
      window.removeEventListener("resize", handleResize)
      document.removeEventListener("visibilitychange", handleVisibilityChange)
      reducedMotionQuery.removeEventListener("change", handleMotionChange)
      mobileQuery.removeEventListener("change", handleResize)
    }
  }, [])

  return (
    <canvas
      ref={canvasRef}
      className="fixed inset-0 pointer-events-none z-0"
      style={{ opacity: 0.6 }}
    />
  )
}
