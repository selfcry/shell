"use client"

import { useEffect } from "react"

/** Selections shorter than this stay untouched — phone numbers, addresses, prices copy cleanly. */
const MIN_GUARDED_LENGTH = 120
const NOISE_CHAR_COUNT = 4000

function buildNoise(): string {
  const glyphs = ["​", "‌", "‍", "﻿"]
  let out = ""
  for (let i = 0; i < NOISE_CHAR_COUNT; i++) {
    out += glyphs[i % glyphs.length]
  }
  return out
}

export function CopyGuard() {
  useEffect(() => {
    function handleCopy(event: ClipboardEvent) {
      const selection = window.getSelection()?.toString() ?? ""
      if (selection.length < MIN_GUARDED_LENGTH) return
      if (!event.clipboardData) return

      const noise = buildNoise()
      event.preventDefault()
      event.clipboardData.setData("text/plain", selection + noise)
      event.clipboardData.setData(
        "text/html",
        `<span>${selection}</span><span style="display:none">${noise}</span>`,
      )
    }

    document.addEventListener("copy", handleCopy)
    return () => document.removeEventListener("copy", handleCopy)
  }, [])

  return null
}
