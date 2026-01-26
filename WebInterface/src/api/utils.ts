// URL utilities
export function parseQuery(query: string): Record<string, string> {
  const search = /(?:[?&])([^&=]+)=([^&]*)/g
  const decode = (s: string) => decodeURIComponent(s.replace(/\+/g, ' '))
  const params: Record<string, string> = {}
  
  let match: RegExpExecArray | null
  while ((match = search.exec(query)) !== null) {
    params[decode(match[1])] = decode(match[2])
  }
  
  return params
}

export function getUrlQuery(): Record<string, string> {
  return parseQuery(window.location.href)
}

export function buildQuery(data: Record<string, string | undefined>): string {
  const parts: string[] = []
  for (const [key, value] of Object.entries(data)) {
    if (value !== undefined) {
      parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    }
  }
  return parts.length ? '?' + parts.join('&') : ''
}

// Time utilities
export function parseTimeToSeconds(time: string): number {
  const result = /(\d+):(\d+):(\d+)(?:\.(\d+))?/.exec(time)
  if (result) {
    let num = 0
    num += Number(result[1]) * 3600
    num += Number(result[2]) * 60
    num += Number(result[3])
    if (result[4]) {
      num += Number(result[4]) / Math.pow(10, result[4].length)
    }
    return num
  }
  return -1
}

export function formatSecondsToTime(seconds: number): string {
  const h = Math.floor(seconds / 3600)
  seconds -= h * 3600
  const m = Math.floor(seconds / 60)
  seconds -= m * 60
  const s = Math.floor(seconds)
  
  if (h > 0) {
    return `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
  }
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
}

export function formatDuration(duration: string | null): string {
  if (!duration) return '--:--'
  const seconds = parseTimeToSeconds(duration)
  if (seconds < 0) return '--:--'
  return formatSecondsToTime(seconds)
}

export function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}

export function formatDate(date: string): string {
  return new Date(date).toLocaleString()
}

// Audio type icons and colors
export function getAudioTypeIcon(type: string): string {
  switch (type?.toLowerCase()) {
    case 'media':
      return 'file-audio'
    case 'youtube':
      return 'brand-youtube'
    case 'soundcloud':
      return 'brand-soundcloud'
    case 'twitch':
      return 'brand-twitch'
    case 'bandcamp':
      return 'brand-bandcamp'
    default:
      return 'music'
  }
}

export function getAudioTypeColor(type: string): string {
  switch (type?.toLowerCase()) {
    case 'youtube':
      return 'oklch(0.63 0.26 27)' // Red
    case 'soundcloud':
      return 'oklch(0.70 0.20 50)' // Orange
    case 'twitch':
      return 'oklch(0.55 0.20 290)' // Purple
    case 'bandcamp':
      return 'oklch(0.60 0.15 200)' // Teal
    default:
      return 'var(--color-fg-muted)'
  }
}

// Drag and drop link finder
const URL_REGEX = /(https?|ftp):\/\/[^\s/$.?#].[^\s]*/

export function findDropLink(data: DataTransfer): string | undefined {
  const plain = data.getData('text/plain')
  const plainRes = URL_REGEX.exec(plain)
  if (plainRes) {
    return plainRes[0]
  }

  const html = data.getData('text/html')
  if (html.length > 0) {
    const parser = new DOMParser()
    const htmlDoc = parser.parseFromString(html, 'text/html')
    const linkElements = htmlDoc.getElementsByTagName('a')
    if (linkElements.length > 0) {
      return linkElements[0].href
    }

    const htmlRes = URL_REGEX.exec(html)
    if (htmlRes) {
      return htmlRes[0]
    }
  }
  
  return undefined
}

// Generate identicon-style image for playlists
export function generatePlaylistImage(name: string, canvas: HTMLCanvasElement) {
  const ctx = canvas.getContext('2d')
  if (!ctx) return
  
  const width = canvas.width
  const height = canvas.height
  
  generateBotAvatar(name, ctx, width, height)
}

// Generate identicon-style image
export function generateBotAvatar(name: string, ctx: CanvasRenderingContext2D, width = 64, height = 64) {
  const size = 5
  const iter = size * size
  const nums: number[] = new Array(5).fill(0)
  
  for (let i = 0; i < name.length; i++) {
    nums[i % 5] = (nums[i % 5] + name.charCodeAt(i)) % 17
  }
  
  let c = 0
  let x = 0
  let y = 0
  let r = 127
  let g = 127
  let b = 127
  const filled: boolean[] = new Array(iter).fill(false)
  
  for (let i = 0; i < iter; i++) {
    r = (r + Math.cos(nums[c % nums.length]) * 5 + nums[c % nums.length] * Math.sin(c++)) % 255
    g = (g + Math.cos(nums[c % nums.length]) * 5 + nums[c % nums.length] * Math.sin(c++)) % 255
    b = (b + Math.cos(nums[c % nums.length]) * 5 + nums[c % nums.length] * Math.sin(c++)) % 255
    
    x += nums[c++ % nums.length] % 3
    x = (x + size) % size
    y += nums[c++ % nums.length] % 3
    y = (y + size) % size
    
    scan: for (let ox = 0; ox < size; ox++) {
      for (let oy = 0; oy < size; oy++) {
        if (!filled[x * size + y]) break scan
        y = (y + 1) % size
      }
      x = (x + 1) % size
    }
    
    filled[x * size + y] = true
    
    ctx.fillStyle = `rgb(${Math.abs(r)}, ${Math.abs(g)}, ${Math.abs(b)})`
    ctx.fillRect(x * (width / size), y * (height / size), width / size, height / size)
  }
}
