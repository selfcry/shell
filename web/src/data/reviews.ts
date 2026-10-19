export interface Review {
  name: string
  text: string
  rating: number
  date: string
  source: string
  club?: string
  avatar?: string
}

export const fallbackReviews: Review[] = []
