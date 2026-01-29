<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { Card, Button, Icon, Input, Slider } from '@/components/ui'
import { cmd, bot, isError, getErrorMessage } from '@/api/client'
import { useToast } from '@/composables/useToast'

interface Props {
  online?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  online: true,
})

const route = useRoute()
const toast = useToast()

const loading = ref(true)

// Settings filter
const filterText = ref('')
const filterLevel = ref<number>(Number(localStorage.getItem('filter_level') ?? 0))

// Client versions
const clientVersions = ref<{ build: string; platform: string; sign: string }[]>([])

// Settings model
const model = ref<any>({
  run: false,
  generate_status_avatar: false,
  set_status_description: false,
  language: 'en',
  connect: {
    name: '',
    address: '',
    server_password: { pw: '', hashed: false, autohash: false },
    channel_password: { pw: '', hashed: false, autohash: false },
    channel: '',
    client_version: null,
  },
  audio: {
    volume: {
      default: 50,
      min: 0,
      max: 100,
    },
    max_user_volume: 100,
    bitrate: 48,
  },
  commands: {
    matcher: 'ic3',
    long_message: 0,
    long_message_split_limit: 5,
    command_complexity: 64,
    color: true,
  },
  recording: {
    enabled: true,
    path: '',
    max_total_size: '5G',
    stop_delay: '30s',
    min_duration: '5s',
    bitrate: 48,
  },
})

const recordingMaxSizeValue = ref(5)
const recordingMaxSizeUnit = ref<'K' | 'M' | 'G' | 'T'>('G')
const recordingStopDelaySeconds = ref(30)
const recordingMinDurationSeconds = ref(60)

const botId = computed(() => {
  if (props.online) {
    return Number(route.params.id)
  }
  return route.params.name as string
})

// Level labels
const SettLevel = {
  Beginner: 0,
  Advanced: 1,
  Expert: 2,
}

// Language options (matching old version)
const languages: Record<string, string> = {
  cs: 'Czech',
  da: 'Danish',
  en: 'English',
  fr: 'French',
  de: 'German [Deutsch]',
  hu: 'Hungarian',
  pl: 'Polish',
  ru: 'Russian [Русский]',
  es: 'Spanish',
  'es-ar': 'Spanish (Argentina)',
  th: 'Thai',
}

// Long message options
const longMessageOptions = [
  { value: 0, label: 'Split (Message will be split up into multiple messages)' },
  { value: 2, label: 'Drop (Message will not be sent)' },
]

async function loadSettings() {
  loading.value = true
  
  // Ensure minimum loading time
  const minTime = new Promise(resolve => setTimeout(resolve, 500))

  let res
  if (props.online) {
    [res] = await Promise.all([
      bot(cmd<any>('settings', 'get'), botId.value as number).fetch(),
      minTime
    ])
  } else {
    [res] = await Promise.all([
      cmd<any>('settings', 'bot', 'get', botId.value as string).fetch(),
      minTime
    ])
  }
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    // Deep merge with defaults
    model.value = {
      ...model.value,
      ...res,
      connect: { ...model.value.connect, ...res.connect },
      audio: { 
        ...model.value.audio, 
        ...res.audio,
        volume: { ...model.value.audio.volume, ...res.audio?.volume }
      },
      commands: { ...model.value.commands, ...res.commands },
      recording: { ...model.value.recording, ...res.recording },
    }
  }

  const sizeMatch = String(model.value.recording?.max_total_size ?? '').trim().match(/^(\d+(?:\.\d+)?)([KMGT])?$/i)
  if (sizeMatch) {
    recordingMaxSizeValue.value = Number(sizeMatch[1])
    recordingMaxSizeUnit.value = (sizeMatch[2] || 'G').toUpperCase() as 'K' | 'M' | 'G' | 'T'
  }

  recordingStopDelaySeconds.value = parseTimeSpanSeconds(model.value.recording?.stop_delay, 30)
  recordingMinDurationSeconds.value = parseTimeSpanSeconds(model.value.recording?.min_duration, 60)
  
  // Load client versions
  try {
    const versionsRes = await fetch('https://raw.githubusercontent.com/ReSpeak/tsdeclarations/master/Versions.csv')
    const csv = await versionsRes.text()
    clientVersions.value = csv
      .split(/\n/gm)
      .slice(1)
      .map(line => line.split(/,/g))
      .map(parts => ({
        build: parts[0],
        platform: parts[1],
        sign: parts[2]
      }))
      .filter(ver => {
        const buildM = ver.build.match(/\[Build: (\d+)\]/)
        if (!buildM) return true
        return Number(buildM[1]) > 1513163251 // > 3.1.7 required
      })
  } catch (e) {
    console.error('Failed to load client versions', e)
  }
  
  loading.value = false
}

async function saveValue(path: string, value: any) {
  if (typeof value === 'object') {
    value = JSON.stringify(value)
  }
  
  let res
  if (props.online) {
    res = await bot(cmd<void>('settings', 'set', path, value), botId.value as number).fetch()
  } else {
    res = await cmd<void>('settings', 'bot', 'set', botId.value as string, path, value).fetch()
  }
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    return false
  }
  
  toast.success('Saved')
  return true
}

function parseTimeSpanSeconds(value: any, fallback: number): number {
  if (value === null || value === undefined) return fallback
  if (typeof value === 'number' && Number.isFinite(value)) return Math.max(0, Math.round(value))
  const raw = String(value).trim()
  if (!raw) return fallback
  if (raw.includes(':')) {
    const parts = raw.split(':').map(p => Number.parseFloat(p))
    if (parts.some(n => Number.isNaN(n))) return fallback
    if (parts.length === 3) return Math.round(parts[0] * 3600 + parts[1] * 60 + parts[2])
    if (parts.length === 2) return Math.round(parts[0] * 60 + parts[1])
  }
  const match = raw.match(/^(\d+(?:\.\d+)?)(ms|s|m|h|d)?$/i)
  if (!match) return fallback
  const num = Number(match[1])
  const unit = (match[2] || 's').toLowerCase()
  if (!Number.isFinite(num)) return fallback
  switch (unit) {
    case 'ms': return Math.max(0, Math.round(num / 1000))
    case 'm': return Math.max(0, Math.round(num * 60))
    case 'h': return Math.max(0, Math.round(num * 3600))
    case 'd': return Math.max(0, Math.round(num * 86400))
    default: return Math.max(0, Math.round(num))
  }
}

async function saveRecordingMaxSize() {
  const value = Number(recordingMaxSizeValue.value)
  if (!Number.isFinite(value) || value <= 0) {
    toast.error('Max total size must be a positive number')
    return
  }
  const unit = recordingMaxSizeUnit.value
  const size = `${value}${unit}`
  model.value.recording.max_total_size = size
  await saveValue('recording.max_total_size', size)
}

async function saveRecordingStopDelay() {
  const seconds = Math.max(0, Math.round(Number(recordingStopDelaySeconds.value)))
  recordingStopDelaySeconds.value = seconds
  const value = `${seconds}s`
  model.value.recording.stop_delay = value
  await saveValue('recording.stop_delay', value)
}

async function saveRecordingMinDuration() {
  const seconds = Math.max(0, Math.round(Number(recordingMinDurationSeconds.value)))
  recordingMinDurationSeconds.value = seconds
  const value = `${seconds}s`
  model.value.recording.min_duration = value
  await saveValue('recording.min_duration', value)
}

// Apply bot name to running bot
async function applyBotName() {
  if (!props.online) return
  
  const res = await bot(cmd<void>('bot', 'name', model.value.connect.name), botId.value as number).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Bot name applied')
  }
}

// Handle password input change - reset hashed/autohash flags when password text changes
function onPasswordChange(passwordObj: { pw: string; hashed: boolean; autohash: boolean }) {
  passwordObj.hashed = false
  passwordObj.autohash = false
}

// Watch filter level
watch(filterLevel, (val) => {
  localStorage.setItem('filter_level', String(val))
})

// Check if setting should be visible based on filter
// Searches both path (key) and label (human-readable) like the old version
function isVisible(path: string, level: number = 0, label?: string): boolean {
  if (level > filterLevel.value) return false
  if (!filterText.value) return true
  const query = filterText.value.toLowerCase()
  if (path.toLowerCase().includes(query)) return true
  if (label && label.toLowerCase().includes(query)) return true
  return false
}


onMounted(loadSettings)
</script>

<template>
  <div class="settings-page">
    <div class="page-header">
      <h2 class="page-title">Settings</h2>
      <Button variant="ghost" color="neutral" size="sm" @click="loadSettings">
        <Icon name="refresh" :size="14" :spin="loading" />
        Reload
      </Button>
    </div>

    <!-- Filter -->
    <div class="filter-bar">
      <Input
        v-model="filterText"
        placeholder="Filter settings..."
        size="sm"
        class="filter-input"
      >
        <template #prefix>
          <Icon name="search" :size="14" />
        </template>
      </Input>
      
      <div class="level-filters">
        <button
          :class="['level-btn', { 'level-active': filterLevel >= SettLevel.Beginner }]"
          @click="filterLevel = SettLevel.Beginner"
        >
          Simple
        </button>
        <button
          :class="['level-btn', 'level-advanced', { 'level-active': filterLevel >= SettLevel.Advanced }]"
          @click="filterLevel = SettLevel.Advanced"
        >
          Advanced
        </button>
        <button
          :class="['level-btn', 'level-expert', { 'level-active': filterLevel >= SettLevel.Expert }]"
          @click="filterLevel = SettLevel.Expert"
        >
          Expert
        </button>
      </div>
    </div>

    <div v-if="loading" class="loading-state">
      <Icon name="loading" :size="24" class="animate-spin text-muted" />
      <span class="text-muted">Loading settings...</span>
    </div>

    <div v-else class="settings-sections">
      <!-- General -->
      <Card padding="lg" class="settings-section">
        <h3 class="section-title">General</h3>
        
        <div v-if="isVisible('run', 0, 'Auto-start Connect when TS3AudioBot starts')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Auto-start</label>
            <p class="setting-description">Connect when TS3AudioBot starts</p>
          </div>
          <label class="toggle">
            <input type="checkbox" v-model="model.run" @change="saveValue('run', model.run)" />
            <span class="toggle-slider"></span>
          </label>
        </div>
        
        <div v-if="isVisible('generate_status_avatar', 0, 'Song cover as avatar Load song cover as bot avatar')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Song cover as avatar</label>
            <p class="setting-description">Load song cover as bot avatar</p>
          </div>
          <label class="toggle">
            <input type="checkbox" v-model="model.generate_status_avatar" @change="saveValue('generate_status_avatar', model.generate_status_avatar)" />
            <span class="toggle-slider"></span>
          </label>
        </div>
        
        <div v-if="isVisible('set_status_description', 0, 'Song in description Show song in bot description')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Song in description</label>
            <p class="setting-description">Show song in bot description</p>
          </div>
          <label class="toggle">
            <input type="checkbox" v-model="model.set_status_description" @change="saveValue('set_status_description', model.set_status_description)" />
            <span class="toggle-slider"></span>
          </label>
        </div>
        
        <div v-if="isVisible('language', 0, 'Language Bot language')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Language</label>
            <p class="setting-description">Bot language</p>
          </div>
          <select v-model="model.language" @change="saveValue('language', model.language)" class="setting-select">
            <option v-for="(label, code) in languages" :key="code" :value="code">{{ label }}</option>
          </select>
        </div>
      </Card>

      <!-- Connection -->
      <Card padding="lg" class="settings-section">
        <h3 class="section-title">Connection</h3>
        
        <div v-if="isVisible('connect.name', 0, 'Bot name Display name in TeamSpeak')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Bot name</label>
            <p class="setting-description">Display name in TeamSpeak</p>
          </div>
          <div class="setting-input-group">
            <Input
              v-model="model.connect.name"
              placeholder="Bot name"
              size="sm"
              @blur="saveValue('connect.name', model.connect.name)"
            />
            <Button v-if="online" size="sm" variant="soft" @click="applyBotName">Apply</Button>
          </div>
        </div>
        
        <div v-if="isVisible('connect.address', 0, 'Server address TeamSpeak server to connect to')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Server address</label>
            <p class="setting-description">TeamSpeak server to connect to</p>
          </div>
          <Input
            v-model="model.connect.address"
            placeholder="ts3server://example.com"
            size="sm"
            @blur="saveValue('connect.address', model.connect.address)"
          />
        </div>
        
        <div v-if="isVisible('connect.server_password', SettLevel.Advanced, 'Server password Password for the server')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Server password</label>
            <p class="setting-description">Password for the server</p>
          </div>
          <div class="password-field">
            <Input
              v-model="model.connect.server_password.pw"
              type="password"
              placeholder="Password"
              size="sm"
              @input="onPasswordChange(model.connect.server_password)"
              @blur="saveValue('connect.server_password', model.connect.server_password)"
            />
            <div v-if="isVisible('connect.server_password', SettLevel.Expert, 'Server password hashed autohash')" class="password-options">
              <label class="checkbox-label">
                <input type="checkbox" v-model="model.connect.server_password.hashed" @change="saveValue('connect.server_password', model.connect.server_password)" />
                <span>Hashed</span>
              </label>
              <label class="checkbox-label">
                <input type="checkbox" v-model="model.connect.server_password.autohash" @change="saveValue('connect.server_password', model.connect.server_password)" />
                <span>Autohash</span>
              </label>
            </div>
          </div>
        </div>
        
        <div v-if="isVisible('connect.channel', SettLevel.Advanced, 'Default channel Channel to join on connect')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Default channel</label>
            <p class="setting-description">Channel to join on connect</p>
          </div>
          <Input
            v-model="model.connect.channel"
            placeholder="Channel path or ID"
            size="sm"
            @blur="saveValue('connect.channel', model.connect.channel)"
          />
        </div>
        
        <div v-if="isVisible('connect.channel_password', SettLevel.Advanced, 'Channel password Password for the default channel')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Channel password</label>
            <p class="setting-description">Password for the default channel</p>
          </div>
          <div class="password-field">
            <Input
              v-model="model.connect.channel_password.pw"
              type="password"
              placeholder="Password"
              size="sm"
              @input="onPasswordChange(model.connect.channel_password)"
              @blur="saveValue('connect.channel_password', model.connect.channel_password)"
            />
            <div v-if="isVisible('connect.channel_password', SettLevel.Expert, 'Channel password hashed autohash')" class="password-options">
              <label class="checkbox-label">
                <input type="checkbox" v-model="model.connect.channel_password.hashed" @change="saveValue('connect.channel_password', model.connect.channel_password)" />
                <span>Hashed</span>
              </label>
              <label class="checkbox-label">
                <input type="checkbox" v-model="model.connect.channel_password.autohash" @change="saveValue('connect.channel_password', model.connect.channel_password)" />
                <span>Autohash</span>
              </label>
            </div>
          </div>
        </div>
        
        <div v-if="isVisible('connect.client_version', SettLevel.Advanced, 'Emulated client version TeamSpeak client version to emulate') && clientVersions.length > 0" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Emulated client version</label>
            <p class="setting-description">TeamSpeak client version to emulate</p>
          </div>
          <select v-model="model.connect.client_version" @change="saveValue('connect.client_version', model.connect.client_version)" class="setting-select">
            <option :value="null">Default</option>
            <option v-for="ver in clientVersions" :key="ver.build + ver.platform" :value="ver">
              {{ ver.build }} : {{ ver.platform }}
            </option>
          </select>
        </div>
      </Card>

      <!-- Audio -->
      <Card padding="lg" class="settings-section">
        <h3 class="section-title">Audio</h3>
        
        <div v-if="isVisible('audio.volume.default', 0, 'Default volume Initial volume when bot starts')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Default volume</label>
            <p class="setting-description">Initial volume when bot starts</p>
          </div>
          <div class="setting-slider">
            <Slider
              v-model="model.audio.volume.default"
              :min="0"
              :max="100"
              show-value
              @change="saveValue('audio.volume.default', model.audio.volume.default)"
            />
          </div>
        </div>
        
        <div v-if="isVisible('audio.volume.min', SettLevel.Advanced, 'Volume reset range Min max volume for new songs')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Volume reset range</label>
            <p class="setting-description">Min/max volume for new songs</p>
          </div>
          <div class="dual-slider">
            <div class="slider-label">Min: {{ model.audio?.volume?.min ?? 0 }}</div>
            <Slider
              v-model="model.audio.volume.min"
              :min="0"
              :max="100"
              @change="saveValue('audio.volume.min', model.audio.volume.min)"
            />
            <div class="slider-label">Max: {{ model.audio?.volume?.max ?? 100 }}</div>
            <Slider
              v-model="model.audio.volume.max"
              :min="0"
              :max="100"
              @change="saveValue('audio.volume.max', model.audio.volume.max)"
            />
          </div>
        </div>
        
        <div v-if="isVisible('audio.max_user_volume', SettLevel.Advanced, 'Max user volume Maximum volume users can set')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Max user volume</label>
            <p class="setting-description">Maximum volume users can set</p>
          </div>
          <div class="setting-slider">
            <Slider
              v-model="model.audio.max_user_volume"
              :min="0"
              :max="100"
              show-value
              @change="saveValue('audio.max_user_volume', model.audio.max_user_volume)"
            />
          </div>
        </div>
        
        <div v-if="isVisible('audio.bitrate', SettLevel.Advanced, 'Bitrate Audio quality kbps')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Bitrate</label>
            <p class="setting-description">Audio quality (kbps)</p>
          </div>
          <div class="bitrate-control">
            <div class="bitrate-options">
              <button
                v-for="br in [16, 24, 32, 48, 64, 96]"
                :key="br"
                :class="['bitrate-btn', { 'bitrate-active': model.audio.bitrate === br }]"
                @click="model.audio.bitrate = br; saveValue('audio.bitrate', br)"
              >
                {{ br }}
              </button>
            </div>
            <div v-if="isVisible('audio.bitrate', SettLevel.Expert, 'Bitrate slider')" class="bitrate-slider">
              <Slider
                v-model="model.audio.bitrate"
                :min="2"
                :max="128"
                :step="2"
                show-value
                @change="saveValue('audio.bitrate', model.audio.bitrate)"
              />
            </div>
          </div>
        </div>
      </Card>

      <!-- Recording -->
      <Card padding="lg" class="settings-section">
        <h3 class="section-title">Recording</h3>

        <div v-if="isVisible('recording.enabled', 0, 'Recording enabled')"
             class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Enable recording</label>
            <p class="setting-description">Enable or disable channel recording</p>
          </div>
          <label class="toggle">
            <input type="checkbox" v-model="model.recording.enabled" @change="saveValue('recording.enabled', model.recording.enabled)" />
            <span class="toggle-slider"></span>
          </label>
        </div>

        <div v-if="isVisible('recording.path', SettLevel.Advanced, 'Recording path')"
             class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Recording path</label>
            <p class="setting-description">Relative or absolute path for recordings</p>
          </div>
          <Input
            v-model="model.recording.path"
            placeholder="recordings"
            size="sm"
            @blur="saveValue('recording.path', model.recording.path)"
          />
        </div>

        <div v-if="isVisible('recording.max_total_size', SettLevel.Advanced, 'Recording max total size')"
             class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Max total size</label>
            <p class="setting-description">e.g. 5G, 500M</p>
          </div>
          <div class="setting-input-group">
            <Input
              v-model.number="recordingMaxSizeValue"
              type="number"
              min="0"
              step="0.1"
              placeholder="5"
              size="sm"
              @blur="saveRecordingMaxSize"
            />
            <select v-model="recordingMaxSizeUnit" @change="saveRecordingMaxSize" class="setting-select setting-select--compact">
              <option value="K">KB</option>
              <option value="M">MB</option>
              <option value="G">GB</option>
              <option value="T">TB</option>
            </select>
          </div>
        </div>

        <div v-if="isVisible('recording.stop_delay', SettLevel.Advanced, 'Recording stop delay')"
             class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Stop delay</label>
            <p class="setting-description">Delay before stopping after channel is empty (e.g. 30s)</p>
          </div>
          <Input
            v-model.number="recordingStopDelaySeconds"
            type="number"
            min="0"
            step="1"
            placeholder="30"
            size="sm"
            @blur="saveRecordingStopDelay"
          />
        </div>

        <div v-if="isVisible('recording.min_duration', SettLevel.Advanced, 'Recording minimum duration')"
             class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Minimum duration</label>
            <p class="setting-description">Discard recordings shorter than this (e.g. 60s)</p>
          </div>
          <Input
            v-model.number="recordingMinDurationSeconds"
            type="number"
            min="0"
            step="1"
            placeholder="60"
            size="sm"
            @blur="saveRecordingMinDuration"
          />
        </div>

        <div v-if="isVisible('recording.bitrate', SettLevel.Advanced, 'Recording bitrate')"
             class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Recording bitrate</label>
            <p class="setting-description">Opus bitrate in kbps</p>
          </div>
          <div class="bitrate-control">
            <div class="bitrate-options">
              <button
                v-for="br in [16, 24, 32, 48, 64, 96]"
                :key="br"
                :class="['bitrate-btn', { 'bitrate-active': model.recording.bitrate === br }]"
                @click="model.recording.bitrate = br; saveValue('recording.bitrate', br)"
              >
                {{ br }}
              </button>
            </div>
            <div v-if="isVisible('recording.bitrate', SettLevel.Expert, 'Recording bitrate slider')" class="bitrate-slider">
              <Slider
                v-model="model.recording.bitrate"
                :min="2"
                :max="128"
                :step="2"
                show-value
                @change="saveValue('recording.bitrate', model.recording.bitrate)"
              />
            </div>
          </div>
        </div>
      </Card>

      <!-- Commands -->
      <Card v-if="filterLevel >= SettLevel.Advanced" padding="lg" class="settings-section">
        <h3 class="section-title">Commands</h3>
        
        <div v-if="isVisible('commands.matcher', SettLevel.Expert, 'Matcher Command matching algorithm')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Matcher</label>
            <p class="setting-description">Command matching algorithm</p>
          </div>
          <select v-model="model.commands.matcher" @change="saveValue('commands.matcher', model.commands.matcher)" class="setting-select">
            <option value="ic3">IC3</option>
            <option value="exact">Exact</option>
            <option value="substring">Substring</option>
          </select>
        </div>
        
        <div v-if="isVisible('commands.long_message', SettLevel.Advanced, 'Long messages How to handle messages that are too long')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Long messages</label>
            <p class="setting-description">How to handle messages that are too long</p>
          </div>
          <select v-model="model.commands.long_message" @change="saveValue('commands.long_message', model.commands.long_message)" class="setting-select">
            <option v-for="opt in longMessageOptions" :key="opt.value" :value="opt.value">
              {{ opt.label }}
            </option>
          </select>
        </div>
        
        <div v-if="isVisible('commands.long_message_split_limit', SettLevel.Advanced, 'Split limit Max messages when splitting') && model.commands.long_message === 0" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Split limit</label>
            <p class="setting-description">Max messages when splitting</p>
          </div>
          <Input
            v-model.number="model.commands.long_message_split_limit"
            type="number"
            size="sm"
            @blur="saveValue('commands.long_message_split_limit', model.commands.long_message_split_limit)"
          />
        </div>
        
        <div v-if="isVisible('commands.color', SettLevel.Advanced, 'Colored messages Use colors in chat messages')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Colored messages</label>
            <p class="setting-description">Use colors in chat messages</p>
          </div>
          <label class="toggle">
            <input type="checkbox" v-model="model.commands.color" @change="saveValue('commands.color', model.commands.color)" />
            <span class="toggle-slider"></span>
          </label>
        </div>
        
        <div v-if="isVisible('commands.command_complexity', SettLevel.Expert, 'Max complexity Maximum command complexity')" class="setting-item">
          <div class="setting-info">
            <label class="setting-label">Max complexity</label>
            <p class="setting-description">Maximum command complexity</p>
          </div>
          <Input
            v-model.number="model.commands.command_complexity"
            type="number"
            size="sm"
            @blur="saveValue('commands.command_complexity', model.commands.command_complexity)"
          />
        </div>
      </Card>
    </div>

    <div v-if="!online" class="offline-notice">
      <Icon name="info" :size="16" />
      <span>Some settings are only available when the bot is online.</span>
    </div>
  </div>
</template>

<style scoped>
.settings-page {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.page-title {
  font-size: 1rem;
  font-weight: 600;
  margin: 0;
}

/* Filter */
.filter-bar {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
}

.filter-input {
  width: 240px;
}

.level-filters {
  display: flex;
  gap: 0.25rem;
}

.level-btn {
  padding: 0.375rem 0.75rem;
  border: none;
  border-radius: var(--radius-md);
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  background: var(--color-bg-inset);
  color: var(--color-fg-muted);
  transition: all 0.15s ease-out;
}

.level-btn.level-active {
  background: var(--color-success-muted);
  color: var(--color-success);
}

.level-btn.level-advanced.level-active {
  background: var(--color-warning-muted);
  color: var(--color-warning);
}

.level-btn.level-expert.level-active {
  background: var(--color-error-muted);
  color: var(--color-error);
}

/* Loading */
.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  padding: 3rem;
}

/* Sections */
.settings-sections {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.settings-section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.section-title {
  font-size: 13px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--color-fg-muted);
  margin: 0;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid var(--color-border);
}

/* Setting Item */
.setting-item {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 2rem;
  padding: 0.5rem 0;
}

.setting-info {
  flex: 1;
  min-width: 0;
}

.setting-label {
  font-size: 14px;
  font-weight: 500;
  display: block;
}

.setting-description {
  font-size: 12px;
  color: var(--color-fg-muted);
  margin: 0.125rem 0 0;
}

.setting-input-group {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

.setting-slider {
  width: 200px;
}

.setting-select {
  padding: 0.5rem 0.75rem;
  border: none;
  border-radius: var(--radius-md);
  background: var(--color-bg-inset);
  color: var(--color-fg);
  font-size: 13px;
  box-shadow: var(--shadow-sm);
  min-width: 180px;
}

.setting-select--compact {
  min-width: 0;
  width: auto;
  padding: 0.35rem 0.5rem;
}

/* Password field */
.password-field {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.password-options {
  display: flex;
  gap: 1rem;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 12px;
  color: var(--color-fg-muted);
  cursor: pointer;
}

.checkbox-label input {
  cursor: pointer;
}

/* Toggle */
.toggle {
  position: relative;
  display: inline-block;
  width: 40px;
  height: 22px;
  flex-shrink: 0;
}

.toggle input {
  opacity: 0;
  width: 0;
  height: 0;
}

.toggle-slider {
  position: absolute;
  cursor: pointer;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: var(--color-bg-inset);
  border-radius: 11px;
  transition: all 0.2s ease-out;
}

.toggle-slider::before {
  position: absolute;
  content: "";
  height: 18px;
  width: 18px;
  left: 2px;
  bottom: 2px;
  background: var(--color-bg-elevated);
  border-radius: 50%;
  transition: all 0.2s ease-out;
  box-shadow: var(--shadow-sm);
}

.toggle input:checked + .toggle-slider {
  background: var(--color-accent);
}

.toggle input:checked + .toggle-slider::before {
  transform: translateX(18px);
}

/* Dual Slider */
.dual-slider {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  width: 200px;
}

.slider-label {
  font-size: 11px;
  color: var(--color-fg-muted);
}

/* Bitrate */
.bitrate-control {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.bitrate-options {
  display: flex;
  gap: 0.25rem;
}

.bitrate-btn {
  padding: 0.375rem 0.625rem;
  border: none;
  border-radius: var(--radius-md);
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  background: var(--color-bg-inset);
  color: var(--color-fg-muted);
  transition: all 0.15s ease-out;
}

.bitrate-btn.bitrate-active {
  background: var(--color-accent-muted);
  color: var(--color-accent);
}

.bitrate-slider {
  width: 200px;
}

/* Offline Notice */
.offline-notice {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem 1rem;
  background: var(--color-warning-muted);
  color: var(--color-warning);
  border-radius: var(--radius-md);
  font-size: 13px;
}
</style>
