<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch, nextTick } from 'vue'
import Icon from './Icon.vue'

interface Props {
  src: string
  title?: string
  isLive?: boolean
  initialDuration?: number
  waveformSrc?: string
  waveformScaleMax?: number
}

const props = withDefaults(defineProps<Props>(), {
  title: 'Audio',
  isLive: false,
  initialDuration: 0,
})

const emit = defineEmits<{
  close: []
}>()

const audioRef = ref<HTMLAudioElement | null>(null)
const canvasRef = ref<HTMLCanvasElement | null>(null)
const overviewRef = ref<HTMLCanvasElement | null>(null)
const waveformContainerRef = ref<HTMLDivElement | null>(null)

const isPlaying = ref(false)
const currentTime = ref(0)
const duration = ref(props.initialDuration)
const volume = ref(1)
const isMuted = ref(false)
const showVolume = ref(false)
const isDragging = ref(false)
const isLoading = ref(true)
const waveformData = ref<number[]>([])
const waveformLoading = ref(true)

// Zoom & pan state
const zoomLevel = ref(1) // 1 = fit all, higher = more zoom
const maxZoom = 32
const minZoom = 1
const scrollOffset = ref(0) // 0-1, percentage of scrollable area
const isOverviewDragging = ref(false)

const volumeIcon = computed(() => {
  if (isMuted.value || volume.value === 0) return 'volume-x'
  if (volume.value < 0.5) return 'volume-1'
  return 'volume-2'
})

// Viewport calculations
const viewportWidth = computed(() => 1 / zoomLevel.value) // portion of waveform visible
const maxScrollOffset = computed(() => Math.max(0, 1 - viewportWidth.value))
const viewportStart = computed(() => scrollOffset.value * maxScrollOffset.value)
const viewportEnd = computed(() => viewportStart.value + viewportWidth.value)

function formatTime(sec: number): string {
  if (!isFinite(sec) || sec < 0) return '00:00'
  const s = Math.floor(sec)
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const r = s % 60
  const mm = ('00' + m.toString()).slice(-2)
  const ss = ('00' + r.toString()).slice(-2)
  if (h > 0) {
    return `${h}:${mm}:${ss}`
  }
  return `${mm}:${ss}`
}

function togglePlay() {
  const audio = audioRef.value
  if (!audio) return
  if (audio.paused) {
    audio.play().catch(() => {})
  } else {
    audio.pause()
  }
}

function onPlay() {
  isPlaying.value = true
}

function onPause() {
  isPlaying.value = false
}

function onTimeUpdate() {
  const audio = audioRef.value
  if (!audio || isDragging.value) return
  currentTime.value = audio.currentTime || 0
  
  // Auto-scroll to keep playhead visible when zoomed
  if (zoomLevel.value > 1 && duration.value > 0) {
    const playPercent = currentTime.value / duration.value
    if (playPercent < viewportStart.value || playPercent > viewportEnd.value) {
      // Center playhead in viewport
      const newOffset = (playPercent - viewportWidth.value / 2) / maxScrollOffset.value
      scrollOffset.value = Math.max(0, Math.min(1, newOffset))
    }
  }
  
  drawWaveform()
  drawOverview()
}

function onLoadedMetadata() {
  const audio = audioRef.value
  if (!audio) return
  isLoading.value = false
  if (isFinite(audio.duration) && audio.duration > 0) {
    duration.value = audio.duration
  }
}

function onCanPlay() {
  isLoading.value = false
}

function onEnded() {
  isPlaying.value = false
  currentTime.value = 0
  drawWaveform()
  drawOverview()
}

function seekByClick(event: MouseEvent) {
  const audio = audioRef.value
  const container = waveformContainerRef.value
  if (!audio || !container || !duration.value) return
  
  const rect = container.getBoundingClientRect()
  const x = event.clientX - rect.left
  const clickPercent = x / rect.width
  
  // Convert click position to actual time considering zoom/scroll
  const actualPercent = viewportStart.value + clickPercent * viewportWidth.value
  audio.currentTime = actualPercent * duration.value
  currentTime.value = audio.currentTime
  drawWaveform()
  drawOverview()
}

function startDrag(event: MouseEvent) {
  isDragging.value = true
  document.addEventListener('mousemove', onDrag)
  document.addEventListener('mouseup', stopDrag)
  onDrag(event)
}

function onDrag(event: MouseEvent) {
  const container = waveformContainerRef.value
  if (!container || !duration.value) return
  
  const rect = container.getBoundingClientRect()
  const x = event.clientX - rect.left
  const clickPercent = Math.max(0, Math.min(1, x / rect.width))
  
  const actualPercent = viewportStart.value + clickPercent * viewportWidth.value
  currentTime.value = Math.max(0, Math.min(1, actualPercent)) * duration.value
  drawWaveform()
  drawOverview()
}

function stopDrag() {
  const audio = audioRef.value
  if (audio && duration.value) {
    audio.currentTime = currentTime.value
  }
  isDragging.value = false
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
}

function setVolume(value: number) {
  volume.value = Math.max(0, Math.min(1, value))
  if (audioRef.value) {
    audioRef.value.volume = volume.value
  }
  isMuted.value = volume.value === 0
}

function toggleMute() {
  if (isMuted.value) {
    isMuted.value = false
    if (audioRef.value) {
      audioRef.value.volume = volume.value || 0.5
    }
  } else {
    isMuted.value = true
    if (audioRef.value) {
      audioRef.value.volume = 0
    }
  }
}

function handleVolumeInput(event: Event) {
  const target = event.target as HTMLInputElement
  setVolume(parseFloat(target.value))
}

function skip(seconds: number) {
  const audio = audioRef.value
  if (!audio) return
  audio.currentTime = Math.max(0, Math.min(duration.value, audio.currentTime + seconds))
}

// Zoom functions
function zoomIn() {
  const oldZoom = zoomLevel.value
  zoomLevel.value = Math.min(maxZoom, zoomLevel.value * 2)
  adjustScrollAfterZoom(oldZoom)
  drawWaveform()
  drawOverview()
}

function zoomOut() {
  const oldZoom = zoomLevel.value
  zoomLevel.value = Math.max(minZoom, zoomLevel.value / 2)
  adjustScrollAfterZoom(oldZoom)
  drawWaveform()
  drawOverview()
}

function resetZoom() {
  zoomLevel.value = 1
  scrollOffset.value = 0
  drawWaveform()
  drawOverview()
}

function adjustScrollAfterZoom(_oldZoom: number) {
  if (duration.value <= 0) return
  
  // Keep current playhead position centered after zoom
  const playPercent = currentTime.value / duration.value
  const newViewportWidth = 1 / zoomLevel.value
  const newMaxScroll = Math.max(0, 1 - newViewportWidth)
  
  if (newMaxScroll > 0) {
    const newOffset = (playPercent - newViewportWidth / 2) / newMaxScroll
    scrollOffset.value = Math.max(0, Math.min(1, newOffset))
  } else {
    scrollOffset.value = 0
  }
}

function handleWheel(event: WheelEvent) {
  event.preventDefault()
  
  if (event.ctrlKey || event.metaKey) {
    // Zoom with Ctrl/Cmd + wheel
    if (event.deltaY < 0) {
      zoomIn()
    } else {
      zoomOut()
    }
  } else if (zoomLevel.value > 1) {
    // Horizontal scroll when zoomed
    const delta = event.deltaX !== 0 ? event.deltaX : event.deltaY
    const scrollStep = 0.1 / zoomLevel.value
    scrollOffset.value = Math.max(0, Math.min(1, scrollOffset.value + (delta > 0 ? scrollStep : -scrollStep)))
    drawWaveform()
    drawOverview()
  }
}

// Overview minimap drag
function startOverviewDrag(event: MouseEvent) {
  isOverviewDragging.value = true
  handleOverviewDrag(event)
  document.addEventListener('mousemove', handleOverviewDrag)
  document.addEventListener('mouseup', stopOverviewDrag)
}

function handleOverviewDrag(event: MouseEvent) {
  const overview = overviewRef.value
  if (!overview) return
  
  const rect = overview.getBoundingClientRect()
  const x = event.clientX - rect.left
  const clickPercent = Math.max(0, Math.min(1, x / rect.width))
  
  // Center viewport on click position
  if (maxScrollOffset.value > 0) {
    const targetStart = clickPercent - viewportWidth.value / 2
    scrollOffset.value = Math.max(0, Math.min(1, targetStart / maxScrollOffset.value))
  }
  
  drawWaveform()
  drawOverview()
}

function stopOverviewDrag() {
  isOverviewDragging.value = false
  document.removeEventListener('mousemove', handleOverviewDrag)
  document.removeEventListener('mouseup', stopOverviewDrag)
}

async function loadWaveform() {
  waveformLoading.value = true
  try {
    if (props.waveformSrc) {
      await loadWaveformFromBinary(props.waveformSrc)
    } else {
      await loadWaveformFromAudio(props.src)
    }
  } catch {
    try {
      if (props.waveformSrc) {
        await loadWaveformFromAudio(props.src)
      } else {
        throw new Error('fallback')
      }
    } catch {
      const samples = 4096
      waveformData.value = Array.from({ length: samples }, (_, i) => {
        return 0.3 + Math.sin(i * 0.02) * 0.2 + Math.random() * 0.3
      })
    }
  }
  waveformLoading.value = false
  nextTick(() => {
    drawWaveform()
    drawOverview()
  })
}

async function loadWaveformFromAudio(src: string) {
  const response = await fetch(src)
  const arrayBuffer = await response.arrayBuffer()
  const audioContext = new (window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext)()
  const audioBuffer = await audioContext.decodeAudioData(arrayBuffer)

  const rawData = audioBuffer.getChannelData(0)
  const samples = 4096
  const blockSize = Math.floor(rawData.length / samples)
  const filteredData: number[] = []

  for (let i = 0; i < samples; i++) {
    let min = 0
    let max = 0
    for (let j = 0; j < blockSize; j++) {
      const val = rawData[i * blockSize + j]
      if (val < min) min = val
      if (val > max) max = val
    }
    filteredData.push(Math.max(Math.abs(min), Math.abs(max)))
  }

  const maxVal = Math.max(...filteredData)
  const scale = maxVal > 0 ? maxVal : 1
  waveformData.value = filteredData.map(v => v / scale)

  audioContext.close()
}

async function loadWaveformFromBinary(src: string) {
  const response = await fetch(src)
  const arrayBuffer = await response.arrayBuffer()
  if (arrayBuffer.byteLength < 16) throw new Error('waveform too small')
  const view = new DataView(arrayBuffer)

  const magic =
    String.fromCharCode(view.getUint8(0)) +
    String.fromCharCode(view.getUint8(1)) +
    String.fromCharCode(view.getUint8(2)) +
    String.fromCharCode(view.getUint8(3))
  if (magic !== 'TSWF') throw new Error('invalid waveform magic')

  const sampleCount = view.getUint32(12, true)
  const payload = new Uint8Array(arrayBuffer, 16)
  const count = sampleCount > 0 && sampleCount <= payload.length ? sampleCount : payload.length
  const trimmed = payload.subarray(0, count)
  waveformData.value = scaleWaveform(downsampleWaveform(trimmed, 4096))
}

function downsampleWaveform(data: Uint8Array, targetSamples: number): number[] {
  if (data.length === 0) return []
  if (data.length <= targetSamples) {
    return Array.from(data, v => v / 255)
  }
  const result: number[] = []
  const blockSize = data.length / targetSamples
  for (let i = 0; i < targetSamples; i++) {
    const start = Math.floor(i * blockSize)
    const end = Math.min(data.length, Math.floor((i + 1) * blockSize))
    let max = 0
    for (let j = start; j < end; j++) {
      if (data[j] > max) max = data[j]
    }
    result.push(max / 255)
  }
  return result
}

function scaleWaveform(data: number[]): number[] {
  if (data.length === 0) return data
  const scaleMax = props.waveformScaleMax ?? 0
  if (scaleMax > 0) {
    const scale = scaleMax / 255
    if (scale > 0) {
      return data.map(v => {
        const scaled = v / scale
        return scaled > 1 ? 1 : scaled
      })
    }
  }
  const maxVal = Math.max(...data)
  const scale = maxVal > 0 ? maxVal : 1
  return data.map(v => v / scale)
}

function drawWaveform() {
  const canvas = canvasRef.value
  if (!canvas || waveformData.value.length === 0) return
  
  const ctx = canvas.getContext('2d')
  if (!ctx) return
  
  const dpr = window.devicePixelRatio || 1
  const rect = canvas.getBoundingClientRect()
  canvas.width = rect.width * dpr
  canvas.height = rect.height * dpr
  ctx.scale(dpr, dpr)
  
  const width = rect.width
  const height = rect.height
  const data = waveformData.value
  const dataLen = data.length
  const progressPercent = duration.value > 0 ? currentTime.value / duration.value : 0
  const centerY = height / 2
  const maxAmplitude = height * 0.48
  
  // Calculate visible data range based on zoom/scroll
  const dataStart = Math.floor(viewportStart.value * dataLen)
  const dataEnd = Math.ceil(viewportEnd.value * dataLen)
  const visibleDataLen = dataEnd - dataStart
  
  ctx.clearRect(0, 0, width, height)
  
  // Create gradients
  const playedGradient = ctx.createLinearGradient(0, 0, 0, height)
  playedGradient.addColorStop(0, 'oklch(0.68 0.22 293 / 0.9)')
  playedGradient.addColorStop(0.5, 'oklch(0.58 0.24 293)')
  playedGradient.addColorStop(1, 'oklch(0.68 0.22 293 / 0.9)')
  
  const unplayedGradient = ctx.createLinearGradient(0, 0, 0, height)
  unplayedGradient.addColorStop(0, 'oklch(0.55 0.08 270 / 0.4)')
  unplayedGradient.addColorStop(0.5, 'oklch(0.50 0.10 270 / 0.6)')
  unplayedGradient.addColorStop(1, 'oklch(0.55 0.08 270 / 0.4)')
  
  // Calculate progress position in viewport
  const progressInViewport = (progressPercent - viewportStart.value) / viewportWidth.value
  const progressX = progressInViewport * width
  
  // Draw waveform helper
  const drawHalf = (c: CanvasRenderingContext2D, yDirection: number, gradient: CanvasGradient) => {
    c.beginPath()
    c.moveTo(0, centerY)
    for (let x = 0; x < width; x++) {
      const dataIndex = dataStart + Math.floor((x / width) * visibleDataLen)
      const clampedIndex = Math.max(0, Math.min(dataLen - 1, dataIndex))
      const amplitude = data[clampedIndex] * maxAmplitude
      c.lineTo(x, centerY + yDirection * amplitude)
    }
    c.lineTo(width, centerY)
    c.closePath()
    c.fillStyle = gradient
    c.fill()
  }
  
  // Draw played portion
  if (progressX > 0) {
    ctx.save()
    ctx.beginPath()
    ctx.rect(0, 0, Math.min(progressX, width), height)
    ctx.clip()
    drawHalf(ctx, -1, playedGradient)
    drawHalf(ctx, 1, playedGradient)
    ctx.restore()
  }
  
  // Draw unplayed portion
  if (progressX < width) {
    ctx.save()
    ctx.beginPath()
    ctx.rect(Math.max(0, progressX), 0, width - Math.max(0, progressX), height)
    ctx.clip()
    drawHalf(ctx, -1, unplayedGradient)
    drawHalf(ctx, 1, unplayedGradient)
    ctx.restore()
  }
  
  // Draw subtle center line
  ctx.strokeStyle = 'oklch(0.50 0.05 270 / 0.15)'
  ctx.lineWidth = 1
  ctx.beginPath()
  ctx.moveTo(0, centerY)
  ctx.lineTo(width, centerY)
  ctx.stroke()
  
  // Draw time markers when zoomed
  if (zoomLevel.value >= 2 && duration.value > 0) {
    ctx.fillStyle = 'oklch(0.50 0.05 270 / 0.5)'
    ctx.font = '10px system-ui, sans-serif'
    
    const visibleDuration = duration.value / zoomLevel.value
    const interval = getTimeInterval(visibleDuration)
    const startTime = viewportStart.value * duration.value
    const endTime = viewportEnd.value * duration.value
    
    for (let t = Math.ceil(startTime / interval) * interval; t < endTime; t += interval) {
      const x = ((t / duration.value) - viewportStart.value) / viewportWidth.value * width
      if (x > 10 && x < width - 30) {
        ctx.fillText(formatTime(t), x, height - 4)
        
        // Draw tick mark
        ctx.strokeStyle = 'oklch(0.50 0.05 270 / 0.2)'
        ctx.beginPath()
        ctx.moveTo(x, 0)
        ctx.lineTo(x, 4)
        ctx.stroke()
      }
    }
  }
  
  // Draw progress indicator line
  if (progressX > 0 && progressX < width) {
    ctx.strokeStyle = 'oklch(0.65 0.24 293)'
    ctx.lineWidth = 1.5
    ctx.beginPath()
    ctx.moveTo(progressX, 0)
    ctx.lineTo(progressX, height)
    ctx.stroke()
    
    // Playhead handle
    ctx.fillStyle = 'oklch(0.65 0.24 293)'
    ctx.beginPath()
    ctx.moveTo(progressX - 5, 0)
    ctx.lineTo(progressX + 5, 0)
    ctx.lineTo(progressX, 6)
    ctx.closePath()
    ctx.fill()
  }
}

function getTimeInterval(visibleDuration: number): number {
  if (visibleDuration <= 5) return 1
  if (visibleDuration <= 15) return 2
  if (visibleDuration <= 30) return 5
  if (visibleDuration <= 60) return 10
  if (visibleDuration <= 300) return 30
  return 60
}

function drawOverview() {
  const canvas = overviewRef.value
  if (!canvas || waveformData.value.length === 0) return
  
  const ctx = canvas.getContext('2d')
  if (!ctx) return
  
  const dpr = window.devicePixelRatio || 1
  const rect = canvas.getBoundingClientRect()
  canvas.width = rect.width * dpr
  canvas.height = rect.height * dpr
  ctx.scale(dpr, dpr)
  
  const width = rect.width
  const height = rect.height
  const data = waveformData.value
  const dataLen = data.length
  const progressPercent = duration.value > 0 ? currentTime.value / duration.value : 0
  const centerY = height / 2
  const maxAmplitude = height * 0.45
  
  ctx.clearRect(0, 0, width, height)
  
  // Draw dimmed background waveform
  ctx.fillStyle = 'oklch(0.45 0.05 270 / 0.3)'
  ctx.beginPath()
  ctx.moveTo(0, centerY)
  for (let x = 0; x < width; x++) {
    const dataIndex = Math.floor((x / width) * dataLen)
    const amplitude = data[dataIndex] * maxAmplitude
    ctx.lineTo(x, centerY - amplitude)
  }
  ctx.lineTo(width, centerY)
  ctx.closePath()
  ctx.fill()
  
  ctx.beginPath()
  ctx.moveTo(0, centerY)
  for (let x = 0; x < width; x++) {
    const dataIndex = Math.floor((x / width) * dataLen)
    const amplitude = data[dataIndex] * maxAmplitude
    ctx.lineTo(x, centerY + amplitude)
  }
  ctx.lineTo(width, centerY)
  ctx.closePath()
  ctx.fill()
  
  // Draw viewport indicator
  const vpStartX = viewportStart.value * width
  const vpWidth = viewportWidth.value * width
  
  // Dim areas outside viewport
  ctx.fillStyle = 'oklch(0.15 0 0 / 0.5)'
  ctx.fillRect(0, 0, vpStartX, height)
  ctx.fillRect(vpStartX + vpWidth, 0, width - vpStartX - vpWidth, height)
  
  // Viewport border
  ctx.strokeStyle = 'oklch(0.58 0.22 293 / 0.8)'
  ctx.lineWidth = 1.5
  ctx.strokeRect(vpStartX, 0.5, vpWidth, height - 1)
  
  // Progress indicator
  const progressX = progressPercent * width
  if (progressX > 0) {
    ctx.strokeStyle = 'oklch(0.65 0.24 293)'
    ctx.lineWidth = 1
    ctx.beginPath()
    ctx.moveTo(progressX, 0)
    ctx.lineTo(progressX, height)
    ctx.stroke()
  }
}

function resizeCanvas() {
  nextTick(() => {
    drawWaveform()
    drawOverview()
  })
}

watch(() => props.src, () => {
  currentTime.value = 0
  isLoading.value = true
  isPlaying.value = false
  waveformData.value = []
  zoomLevel.value = 1
  scrollOffset.value = 0
  
  loadWaveform()
  
  nextTick(() => {
    const audio = audioRef.value
    if (audio) {
      audio.load()
      audio.play().catch(() => {})
    }
  })
})

watch(() => props.waveformSrc, () => {
  waveformData.value = []
  loadWaveform()
})

watch(() => props.initialDuration, (val) => {
  if (val && val > 0 && (!duration.value || duration.value <= 0)) {
    duration.value = val
  }
})

onMounted(() => {
  window.addEventListener('resize', resizeCanvas)
  loadWaveform()
  
  nextTick(() => {
    const audio = audioRef.value
    if (audio) {
      audio.load()
      audio.play().catch(() => {})
    }
  })
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', resizeCanvas)
  document.removeEventListener('mousemove', onDrag)
  document.removeEventListener('mouseup', stopDrag)
  document.removeEventListener('mousemove', handleOverviewDrag)
  document.removeEventListener('mouseup', stopOverviewDrag)
})
</script>

<template>
  <div class="audio-player">
    <!-- Main Waveform -->
    <div
      ref="waveformContainerRef"
      class="waveform-container"
      @click="seekByClick"
      @mousedown="startDrag"
      @wheel="handleWheel"
    >
      <div v-if="waveformLoading" class="waveform-loading">
        <Icon name="loader" :size="16" class="spin" />
        <span>Loading waveform...</span>
      </div>
      <canvas ref="canvasRef" class="waveform-canvas" />
      <div class="waveform-time">
        <span>{{ formatTime(currentTime) }}</span>
        <span class="zoom-hint" v-if="zoomLevel === 1">Ctrl+Scroll to zoom</span>
        <span>{{ formatTime(duration) }}</span>
      </div>
    </div>
    
    <!-- Overview (minimap) - shown when zoomed -->
    <div v-if="zoomLevel > 1" class="overview-container">
      <canvas
        ref="overviewRef"
        class="overview-canvas"
        @mousedown="startOverviewDrag"
      />
      <div class="overview-label">
        <span>{{ Math.round(zoomLevel) }}x</span>
      </div>
    </div>
    
    <!-- Controls -->
    <div class="controls">
      <div class="controls-left">
        <div
          class="volume-control"
          @mouseenter="showVolume = true"
          @mouseleave="showVolume = false"
        >
          <button class="control-btn" @click="toggleMute" title="Toggle mute">
            <Icon :name="volumeIcon" :size="16" />
          </button>
          <div class="volume-slider" :class="{ visible: showVolume }">
            <input
              type="range"
              min="0"
              max="1"
              step="0.01"
              :value="isMuted ? 0 : volume"
              class="volume-input"
              @input="handleVolumeInput"
            />
          </div>
        </div>
      </div>
      
      <div class="controls-center">
        <button class="skip-btn" @click="skip(-10)" title="Rewind 10s">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="11 17 6 12 11 7" />
            <polyline points="18 17 13 12 18 7" />
          </svg>
          <span class="skip-label">10</span>
        </button>
        <button
          class="play-btn"
          :class="{ playing: isPlaying }"
          :disabled="isLoading"
          @click="togglePlay"
        >
          <Icon v-if="isLoading" name="loader" :size="20" class="spin" />
          <Icon v-else :name="isPlaying ? 'pause' : 'play'" :size="20" />
        </button>
        <button class="skip-btn" @click="skip(10)" title="Forward 10s">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="6 17 11 12 6 7" />
            <polyline points="13 17 18 12 13 7" />
          </svg>
          <span class="skip-label">10</span>
        </button>
      </div>
      
      <div class="controls-right">
        <!-- Zoom controls -->
        <div class="zoom-controls">
          <button
            class="control-btn"
            :disabled="zoomLevel <= minZoom"
            @click="zoomOut"
            title="Zoom out"
          >
            <Icon name="minus" :size="14" />
          </button>
          <button
            class="control-btn zoom-reset"
            :class="{ active: zoomLevel > 1 }"
            @click="resetZoom"
            :title="`${Math.round(zoomLevel)}x - Click to reset`"
          >
            <span class="zoom-value">{{ Math.round(zoomLevel) }}x</span>
          </button>
          <button
            class="control-btn"
            :disabled="zoomLevel >= maxZoom"
            @click="zoomIn"
            title="Zoom in"
          >
            <Icon name="plus" :size="14" />
          </button>
        </div>
        
        <button class="control-btn close-btn" @click="emit('close')" title="Close">
          <Icon name="x" :size="16" />
        </button>
      </div>
    </div>
    
    <!-- Hidden Audio Element -->
    <audio
      ref="audioRef"
      :src="src"
      preload="metadata"
      @play="onPlay"
      @pause="onPause"
      @timeupdate="onTimeUpdate"
      @loadedmetadata="onLoadedMetadata"
      @canplay="onCanPlay"
      @ended="onEnded"
    />
  </div>
</template>

<style scoped>
.audio-player {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 1rem;
  background: var(--color-bg-inset);
  border-top: 1px solid var(--color-border);
}

/* Waveform */
.waveform-container {
  position: relative;
  height: 80px;
  border-radius: var(--radius-md);
  background: var(--color-bg-elevated);
  cursor: crosshair;
  overflow: hidden;
}

.waveform-loading {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  font-size: 12px;
  color: var(--color-fg-muted);
  z-index: 1;
}

.waveform-canvas {
  width: 100%;
  height: 100%;
}

.waveform-time {
  position: absolute;
  bottom: 4px;
  left: 8px;
  right: 8px;
  display: flex;
  justify-content: space-between;
  font-size: 10px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  color: var(--color-fg-muted);
  pointer-events: none;
}

.zoom-hint {
  font-size: 9px;
  opacity: 0.6;
}

/* Overview minimap */
.overview-container {
  position: relative;
  height: 24px;
  border-radius: var(--radius-sm);
  background: var(--color-bg-elevated);
  overflow: hidden;
}

.overview-canvas {
  width: 100%;
  height: 100%;
  cursor: grab;
}

.overview-canvas:active {
  cursor: grabbing;
}

.overview-label {
  position: absolute;
  right: 4px;
  top: 50%;
  transform: translateY(-50%);
  font-size: 9px;
  font-weight: 600;
  color: var(--color-accent);
  background: var(--color-bg-elevated);
  padding: 1px 4px;
  border-radius: 3px;
  pointer-events: none;
}

.spin {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* Controls */
.controls {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.controls-left,
.controls-right {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.controls-right {
  justify-content: flex-end;
}

.controls-center {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.control-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--color-fg-muted);
  cursor: pointer;
  transition: all 0.15s ease-out;
}

.control-btn:hover:not(:disabled) {
  background: var(--color-bg-elevated);
  color: var(--color-fg);
}

.control-btn:disabled {
  opacity: 0.3;
  cursor: not-allowed;
}

.close-btn:hover {
  background: var(--color-error-muted);
  color: var(--color-error);
}

/* Zoom controls */
.zoom-controls {
  display: flex;
  align-items: center;
  gap: 2px;
  background: var(--color-bg-elevated);
  border-radius: var(--radius-sm);
  padding: 2px;
}

.zoom-reset {
  width: auto;
  padding: 0 6px;
  font-size: 10px;
  font-weight: 600;
}

.zoom-reset.active {
  color: var(--color-accent);
}

.zoom-value {
  font-variant-numeric: tabular-nums;
}

/* Skip buttons with label */
.skip-btn {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--color-fg-muted);
  cursor: pointer;
  transition: all 0.15s ease-out;
}

.skip-btn svg {
  width: 20px;
  height: 20px;
}

.skip-btn .skip-label {
  position: absolute;
  font-size: 8px;
  font-weight: 700;
  color: var(--color-fg-muted);
  bottom: 2px;
  right: 2px;
}

.skip-btn:hover {
  background: var(--color-bg-elevated);
  color: var(--color-fg);
}

.skip-btn:hover .skip-label {
  color: var(--color-fg);
}

.play-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  border: none;
  border-radius: 50%;
  background: var(--color-accent);
  color: var(--color-accent-fg);
  cursor: pointer;
  transition: all 0.15s ease-out;
  box-shadow: 0 2px 8px var(--color-accent-muted);
}

.play-btn:hover {
  background: var(--color-accent-hover);
  transform: scale(1.05);
}

.play-btn:active {
  transform: scale(0.98);
}

.play-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
  transform: none;
}

.play-btn.playing {
  background: var(--color-bg-elevated);
  color: var(--color-fg);
  box-shadow: var(--shadow-sm);
}

/* Volume */
.volume-control {
  position: relative;
  display: flex;
  align-items: center;
}

.volume-slider {
  position: absolute;
  left: 100%;
  top: 50%;
  transform: translateY(-50%);
  width: 0;
  overflow: hidden;
  opacity: 0;
  transition: all 0.2s ease-out;
  padding-left: 0.5rem;
}

.volume-slider.visible {
  width: 80px;
  opacity: 1;
}

.volume-input {
  width: 100%;
  height: 4px;
  -webkit-appearance: none;
  appearance: none;
  background: var(--color-bg-elevated);
  border-radius: 2px;
  cursor: pointer;
}

.volume-input::-webkit-slider-thumb {
  -webkit-appearance: none;
  width: 12px;
  height: 12px;
  background: var(--color-accent);
  border-radius: 50%;
  cursor: pointer;
  transition: transform 0.1s ease-out;
}

.volume-input::-webkit-slider-thumb:hover {
  transform: scale(1.2);
}

.volume-input::-moz-range-thumb {
  width: 12px;
  height: 12px;
  background: var(--color-accent);
  border: none;
  border-radius: 50%;
  cursor: pointer;
}
</style>
