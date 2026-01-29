<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { Button, Icon, Slider } from '@/components/ui'
import { cmd, bot, jmerge, isError, getErrorMessage } from '@/api/client'
import { useToast } from '@/composables/useToast'
import { RepeatKind, type BotInfoSync, type CmdSong } from '@/api/types'
import { formatSecondsToTime, getAudioTypeIcon, getAudioTypeColor } from '@/api/utils'

interface Props {
  botId: number
  info: BotInfoSync
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'request-refresh': []
}>()

const toast = useToast()

const localVolume = ref(props.info.volume)
const isSeeking = ref(false)
const seekPosition = ref(0)
const muteToggleVolume = ref(0)

// Timers for progress update and echo refresh
let playTickInterval: ReturnType<typeof setInterval> | null = null
let echoTickInterval: ReturnType<typeof setInterval> | null = null
let echoCounter = 0

watch(() => props.info.volume, (v) => {
  if (!isSeeking.value) {
    localVolume.value = v
  }
})

const isPlaying = computed(() => props.info.song && !props.info.song.Paused)
const currentPosition = computed(() => isSeeking.value ? seekPosition.value : (props.info.song?.Position ?? 0))
const duration = computed(() => props.info.song?.Length ?? 0)

// Watch song changes to manage timers
watch(() => props.info.song, (song) => {
  updateTimers(song)
}, { deep: true })

function updateTimers(song: CmdSong | null) {
  if (song && !song.Paused) {
    startPlayTick()
  } else {
    stopPlayTick()
  }
}

function startPlayTick() {
  if (playTickInterval) return
  playTickInterval = setInterval(() => {
    if (!props.info.song) {
      stopPlayTick()
      return
    }
    if (props.info.song.Position < props.info.song.Length) {
      props.info.song.Position += 1
    } else {
      stopPlayTick()
      startEcho()
    }
  }, 1000)
}

function stopPlayTick() {
  if (playTickInterval) {
    clearInterval(playTickInterval)
    playTickInterval = null
  }
}

function startEcho() {
  echoCounter = 0
  if (echoTickInterval) clearInterval(echoTickInterval)
  echoTickInterval = setInterval(async () => {
    echoCounter += 1
    if (echoCounter === 1 || echoCounter === 3 || echoCounter === 6) {
      emit('request-refresh')
    }
    if (echoCounter >= 6) {
      if (echoTickInterval) {
        clearInterval(echoTickInterval)
        echoTickInterval = null
      }
    }
  }, 1000)
}

onMounted(() => {
  updateTimers(props.info.song)
})

onUnmounted(() => {
  stopPlayTick()
  if (echoTickInterval) {
    clearInterval(echoTickInterval)
  }
})

async function togglePlay() {
  if (!props.info.song) return
  
  const action = isPlaying.value ? 'pause' : 'play'
  const res = await bot(
    jmerge(cmd<void>(action), cmd<CmdSong | null>('song')),
    props.botId
  ).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    return
  }
  
  props.info.song = res[1]
  
  if (action === 'pause') {
    stopPlayTick()
  } else {
    startPlayTick()
  }
  
  startEcho()
}

async function previous() {
  const res = await bot(cmd<void>('previous'), props.botId).fetch()
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    return
  }
  startEcho()
}

async function next() {
  const res = await bot(cmd<void>('next'), props.botId).fetch()
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    return
  }
  startEcho()
}

async function toggleRepeat() {
  const nextRepeat = (props.info.repeat + 1) % 3
  const res = await bot(
    jmerge(
      cmd<void>('repeat', RepeatKind[nextRepeat].toLowerCase()),
      cmd<RepeatKind>('repeat')
    ),
    props.botId
  ).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    return
  }
  
  props.info.repeat = res[1]
}

async function toggleShuffle() {
  // Use on/off as per the original API
  const newValue = !props.info.shuffle ? 'on' : 'off'
  const res = await bot(
    jmerge(
      cmd<void>('random', newValue),
      cmd<boolean>('random')
    ),
    props.botId
  ).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    return
  }
  
  props.info.shuffle = res[1]
}

async function setVolume(value: number) {
  localVolume.value = value
  const res = await bot(
    jmerge(
      cmd<void>('volume', value.toString()),
      cmd<number>('volume')
    ),
    props.botId
  ).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    return
  }
  
  props.info.volume = Math.floor(res[1])
}

// Mute toggle
async function clickVolume() {
  if (muteToggleVolume.value !== 0 && localVolume.value === 0) {
    // Unmute
    await setVolume(muteToggleVolume.value)
    muteToggleVolume.value = 0
  } else {
    // Mute
    muteToggleVolume.value = localVolume.value
    await setVolume(0)
  }
}

async function seek(value: number) {
  isSeeking.value = false
  const wasRunning = playTickInterval !== null
  stopPlayTick()
  
  const targetSeconds = Math.floor(value)
  const res = await bot(cmd<void>('seek', targetSeconds.toString()), props.botId).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    return
  }
  
  if (props.info.song) {
    props.info.song.Position = targetSeconds
  }
  
  if (wasRunning) {
    startPlayTick()
  }
}

function onSeekStart() {
  isSeeking.value = true
  seekPosition.value = props.info.song?.Position ?? 0
}

const repeatIcon = computed(() => {
  switch (props.info.repeat) {
    case RepeatKind.Off:
      return 'repeat'
    case RepeatKind.One:
      return 'repeat-one'
    case RepeatKind.All:
      return 'repeat'
    default:
      return 'repeat'
  }
})

const volumeIcon = computed(() => {
  if (localVolume.value <= 0) return 'volume-mute'
  if (localVolume.value <= 33) return 'volume'
  return 'volume-high'
})
</script>

<template>
  <div class="play-controls">
    <div class="controls-inner">
      <!-- Song Info -->
      <div class="controls-song">
        <template v-if="info.song">
          <div class="song-icon">
            <Icon :name="getAudioTypeIcon(info.song.AudioType)" :size="18" :color="getAudioTypeColor(info.song.AudioType)" />
          </div>
          <div class="song-info">
            <div class="song-title">{{ info.song.Title }}</div>
            <div class="song-source">{{ info.song.Source }}</div>
          </div>
        </template>
        <div v-else class="no-song">No song playing</div>
      </div>

      <!-- Main Controls -->
      <div class="controls-main">
        <div class="controls-buttons">
          <Button variant="ghost" color="neutral" size="sm" icon-only :disabled="!info.song" @click="previous" title="Previous Song">
            <Icon name="skip-back" :size="16" />
          </Button>
          
          <Button 
            variant="solid" 
            color="neutral" 
            size="md" 
            icon-only
            :disabled="!info.song"
            @click="togglePlay"
            :title="isPlaying ? 'Pause' : 'Play'"
          >
            <Icon :name="isPlaying ? 'pause' : 'play'" :size="18" />
          </Button>
          
          <Button variant="ghost" color="neutral" size="sm" icon-only :disabled="!info.song" @click="next" title="Next Song">
            <Icon name="skip-forward" :size="16" />
          </Button>
        </div>
        
        <!-- Progress -->
        <div class="controls-progress">
          <span class="progress-time">{{ formatSecondsToTime(currentPosition) }}</span>
          <div class="progress-bar-wrapper">
            <Slider
              :model-value="currentPosition"
              :min="0"
              :max="duration || 100"
              :disabled="!info.song"
              @update:model-value="seekPosition = $event"
              @mousedown="onSeekStart"
              @change="seek"
            />
          </div>
          <span class="progress-time">{{ formatSecondsToTime(duration) }}</span>
        </div>
      </div>

      <!-- Secondary Controls -->
      <div class="controls-secondary">
        <Button 
          variant="ghost" 
          color="neutral" 
          size="sm" 
          icon-only
          :class="{ 'control-active': info.shuffle }"
          @click="toggleShuffle"
          :title="info.shuffle ? 'Disable Shuffle' : 'Enable Shuffle'"
        >
          <Icon name="shuffle" :size="16" />
        </Button>
        
        <Button 
          variant="ghost" 
          color="neutral" 
          size="sm" 
          icon-only
          :class="{ 'control-active': info.repeat !== RepeatKind.Off }"
          @click="toggleRepeat"
          title="Toggle Repeat"
        >
          <Icon :name="repeatIcon" :size="16" />
        </Button>
        
        <div class="volume-control">
          <Button variant="ghost" color="neutral" size="sm" icon-only @click="clickVolume" :title="localVolume === 0 ? 'Unmute' : 'Mute'">
            <Icon :name="volumeIcon" :size="16" />
          </Button>
          <div class="volume-slider">
            <Slider
              v-model="localVolume"
              :min="0"
              :max="100"
              @change="setVolume"
            />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.play-controls {
  position: fixed;
  bottom: 0;
  left: var(--sidebar-width);
  right: calc(var(--bot-sidebar-width) + 1.5rem);
  z-index: 100;
  background: var(--color-bg-overlay);
  backdrop-filter: blur(16px) saturate(180%);
  -webkit-backdrop-filter: blur(16px) saturate(180%);
  border-top: 1px solid var(--color-border);
  padding: 0.75rem 1.5rem;
}

.controls-inner {
  display: flex;
  gap: 1.5rem;
  align-items: center;
}

/* Large screens */
@media (max-width: 1280px) {
  .play-controls {
    right: calc(280px + 1.5rem);
  }
}

/* Tablet: no bot sidebar */
@media (max-width: 1024px) {
  .play-controls {
    right: 0;
  }
}

/* Mobile */
@media (max-width: 768px) {
  .play-controls {
    left: 0;
    right: 0;
    padding: 0.5rem 1rem;
  }
  
  .controls-inner {
    gap: 0.75rem;
  }
}

/* Song Info */
.controls-song {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  min-width: 180px;
  max-width: 240px;
  flex-shrink: 0;
}

.song-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: var(--radius-md);
  background: var(--color-bg-inset);
  flex-shrink: 0;
}

.song-info {
  min-width: 0;
}

.song-title {
  font-size: 13px;
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.song-source {
  font-size: 11px;
  color: var(--color-fg-muted);
}

.no-song {
  font-size: 13px;
  color: var(--color-fg-muted);
}

/* Main Controls */
.controls-main {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex: 1;
  min-width: 0;
}

.controls-buttons {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-shrink: 0;
}

.controls-progress {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex: 1;
  min-width: 0;
}

.progress-bar-wrapper {
  flex: 1;
}

.progress-time {
  font-size: 11px;
  font-variant-numeric: tabular-nums;
  color: var(--color-fg-muted);
  min-width: 40px;
  text-align: center;
}

/* Secondary Controls */
.controls-secondary {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex-shrink: 0;
}

.control-active {
  color: var(--color-accent) !important;
}

.volume-control {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.volume-slider {
  width: 100px;
}

/* Small mobile: simplified controls */
@media (max-width: 480px) {
  .controls-song {
    display: none;
  }
  
  .controls-secondary {
    display: none;
  }
  
  .controls-main {
    flex: 1;
  }
}
</style>
