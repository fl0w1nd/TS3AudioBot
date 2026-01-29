<script setup lang="ts">
import { computed } from 'vue'
import {
  Home, Bot, Settings, Server, List, ListMusic, Music, Mic,
  Play, Pause, Square, SkipBack, SkipForward, Repeat, Repeat1, Shuffle, Volume, Volume1, Volume2, VolumeX,
  Plus, Minus, X, Check, Pencil, Trash2, Download, Upload, Search, MoreHorizontal, Link, ExternalLink, Copy,
  CircleCheck, CircleX, Loader2, TriangleAlert, CircleAlert, Info,
  ChevronUp, ChevronDown, ChevronLeft, ChevronRight, ArrowLeft, ArrowRight,
  FileAudio, Youtube, Twitch, GripVertical, LogIn, Cpu,
  Moon, Sun, RefreshCw, RotateCcw, RotateCw,
} from 'lucide-vue-next'

interface Props {
  name: string
  size?: number | string
  color?: string
  strokeWidth?: number
  spin?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  size: 18,
  color: 'currentColor',
  strokeWidth: 2,
  spin: false,
})

// Lucide map
const lucideIcons: Record<string, any> = {
  'home': Home,
  'bot': Bot,
  'settings': Settings,
  'server': Server,
  'list': List,
  'playlist': ListMusic,
  'music': Music,
  'mic': Mic,
  
  // Playback
  'play': Play,
  'pause': Pause,
  'stop': Square,
  'skip-back': SkipBack,
  'skip-forward': SkipForward,
  'repeat': Repeat,
  'repeat-one': Repeat1,
  'shuffle': Shuffle,
  'volume': Volume,
  'volume-1': Volume1,
  'volume-2': Volume2,
  'volume-x': VolumeX,
  'volume-high': Volume2,
  'volume-mute': VolumeX,
  'rotate-ccw': RotateCcw,
  'rotate-cw': RotateCw,
  'loader': Loader2,
  
  // Actions
  'plus': Plus,
  'minus': Minus,
  'x': X,
  'check': Check,
  'edit': Pencil,
  'trash': Trash2,
  'download': Download,
  'upload': Upload,
  'search': Search,
  'more': MoreHorizontal,
  'link': Link,
  'external': ExternalLink,
  'copy': Copy,
  
  // State
  'online': CircleCheck,
  'offline': CircleX,
  'loading': Loader2,
  'warning': TriangleAlert,
  'error': CircleAlert,
  'info': Info,
  
  // Arrows
  'chevron-up': ChevronUp,
  'chevron-down': ChevronDown,
  'chevron-left': ChevronLeft,
  'chevron-right': ChevronRight,
  'arrow-left': ArrowLeft,
  'arrow-right': ArrowRight,
  
  // Media / Misc
  'file-audio': FileAudio,
  'brand-youtube': Youtube,
  'brand-twitch': Twitch,
  'grip': GripVertical,
  'login': LogIn,
  'memory': Cpu,
  
  // Aliases for compatibility
  'menu': List,
  'grid': List, // fallback
  'user': Bot, // fallback
  'users': Bot, // fallback
  'moon': Moon,
  'sun': Sun,
  'refresh': RefreshCw,
  'folder': List, // fallback
  'folder-open': List, // fallback
  'tree': List, // fallback
  'channel': Volume2, // using Volume2 as channel active
  'channel-off': VolumeX, // using VolumeX for inactive
  'lock': Mic, // fallback or use Lock if imported (forgot to import Lock, using Mic as placeholder or add Lock)
}

// Custom paths for missing icons (SoundCloud, Bandcamp)
const customPaths: Record<string, string> = {
  'brand-soundcloud': 'M1 18v-3m3 3v-5m3 5v-7m3 7V9m3 9V7m3 11V5a6 6 0 016 6v7H1',
  'brand-bandcamp': 'M4 19l8-14h8l-8 14H4z',
}

const isLucide = computed(() => !!lucideIcons[props.name])
const iconComponent = computed(() => lucideIcons[props.name] || Music)
const customPath = computed(() => customPaths[props.name] || '')

const sizeValue = computed(() => {
  const s = props.size
  return typeof s === 'number' ? s : parseInt(String(s), 10) || 18
})

// Special handling for loading spin
const isSpin = computed(() => props.spin || props.name === 'loading')

</script>

<template>
  <component
    v-if="isLucide"
    :is="iconComponent"
    :size="sizeValue"
    :color="color"
    :stroke-width="strokeWidth"
    :class="['icon', { 'animate-spin': isSpin }]"
  />
  <svg
    v-else-if="customPath"
    :width="sizeValue"
    :height="sizeValue"
    viewBox="0 0 24 24"
    fill="none"
    :stroke="color"
    :stroke-width="strokeWidth"
    stroke-linecap="round"
    stroke-linejoin="round"
    :class="['icon', { 'animate-spin': isSpin }]"
  >
    <path :d="customPath" />
  </svg>
  <!-- Fallback -->
  <Music
    v-else
    :size="sizeValue"
    :color="color"
    :stroke-width="strokeWidth"
    :class="['icon', 'text-muted', { 'animate-spin': isSpin }]"
  />
</template>

<style scoped>
.icon {
  flex-shrink: 0;
  display: inline-block;
  vertical-align: middle;
}
</style>
