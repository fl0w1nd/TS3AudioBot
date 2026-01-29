<script setup lang="ts">
import { ref, computed, onMounted, provide, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Card, Button, Icon, Badge, Input } from '@/components/ui'
import { cmd, bot, jmerge, isError, getErrorMessage } from '@/api/client'
import { useToast } from '@/composables/useToast'
import { 
  BotStatus, 
  RepeatKind, 
  Empty,
  type CmdBotInfo, 
  type CmdSong, 
  type CmdQueueInfo,
  type BotInfoSync,
  createBotInfoSync 
} from '@/api/types'
import { formatSecondsToTime, getAudioTypeIcon, getAudioTypeColor } from '@/api/utils'
import { api } from '@/api/client'
import PlayControls from '@/components/bot/PlayControls.vue'

interface Props {
  botId?: number
  botName?: string
  online?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  online: true,
})

const route = useRoute()
const router = useRouter()
const toast = useToast()

const info = ref<BotInfoSync>(createBotInfoSync())
const loadSongUrl = ref('')
const loading = ref(false)

const currentBotId = computed(() => props.botId ?? Number(route.params.id))

// Provide bot info to child components
provide('botInfo', info)
provide('botId', currentBotId)
provide('refreshBot', refresh)

const navItems = computed(() => [
  { name: 'bot-server', label: 'Server', icon: 'tree', disabled: !props.online },
  { name: 'bot-playlists', label: 'Playlists', icon: 'playlist', disabled: !props.online },
  { name: 'bot-recordings', label: 'Recordings', icon: 'mic', disabled: !props.online },
  { name: props.online ? 'bot-settings' : 'bot-settings-offline', label: 'Settings', icon: 'settings' },
])

async function refresh() {
  if (!props.online) {
    info.value.botInfo.Id = null
    info.value.botInfo.Name = props.botName ?? null
    info.value.botInfo.Status = BotStatus.Offline
    return
  }

  loading.value = true

  const res = await bot(
    jmerge(
      cmd<CmdBotInfo>('bot', 'info'),
      cmd<CmdQueueInfo>('info', '@-1', '5'),
      cmd<CmdSong | null>('song'),
      cmd<RepeatKind>('repeat'),
      cmd<boolean>('random'),
      cmd<number>('volume')
    ),
    currentBotId.value
  ).fetch()

  loading.value = false

  if (isError(res)) {
    toast.error(getErrorMessage(res))
    return
  }

  info.value.botInfo = res[0] ?? Empty.CmdBotInfo()
  info.value.nowPlaying = res[1] ?? Empty.CmdQueueInfo()
  info.value.song = res[2]
  info.value.repeat = res[3]
  info.value.shuffle = res[4]
  info.value.volume = Math.floor(res[5])
}

async function newSong(action: 'play' | 'add') {
  const song = loadSongUrl.value.trim()
  if (!song) return
  
  loadSongUrl.value = ''
  
  const res = await bot(cmd(action, song), currentBotId.value).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    return
  }
  
  toast.success(action === 'play' ? 'Playing song' : 'Added to queue')
  await refresh()
}

// Get cover URL
function getCoverUrl(): string {
  return `${api.endpoint}/bot/use/${currentBotId.value}/(/data/song/cover/get)`
}

function navigateTo(name: string) {
  if (props.online) {
    router.push({ name, params: { id: currentBotId.value } })
  } else {
    router.push({ name, params: { name: props.botName } })
  }
}

onMounted(refresh)

watch(() => route.params.id, () => {
  if (route.params.id) refresh()
})
</script>

<template>
  <div class="bot-page">
    <!-- Header -->
    <div class="bot-header">
      <div class="breadcrumb">
        <router-link to="/bots" class="breadcrumb-link">
          <Icon name="bot" :size="14" />
          <span>Bots</span>
        </router-link>
        <Icon name="arrow-right" :size="10" class="breadcrumb-separator" />
        <span class="breadcrumb-current">{{ info.botInfo.Name || `Bot #${currentBotId}` }}</span>
      </div>
      
      <div class="bot-title-row">
        <h1 class="bot-title">{{ info.botInfo.Name || `Bot #${currentBotId}` }}</h1>
        <Badge
          :color="info.botInfo.Status === BotStatus.Connected ? 'success' : info.botInfo.Status === BotStatus.Connecting ? 'warning' : 'neutral'"
          dot
        >
          {{ info.botInfo.Status === BotStatus.Connected ? 'Connected' : info.botInfo.Status === BotStatus.Connecting ? 'Connecting' : 'Offline' }}
        </Badge>
        <span v-if="online && info.botInfo.Id !== null" class="bot-meta">
          <span class="bot-meta-item">ID: {{ info.botInfo.Id }}</span>
          <span v-if="info.botInfo.Server" class="bot-meta-divider">|</span>
          <span v-if="info.botInfo.Server" class="bot-meta-item">{{ info.botInfo.Server }}</span>
        </span>
      </div>
    </div>

    <div class="bot-layout">
      <!-- Main Content -->
      <div class="bot-main">
        <!-- Navigation Tabs -->
        <div class="bot-tabs">
          <button
            v-for="item in navItems"
            :key="item.name"
            :class="[
              'bot-tab',
              { 
                'bot-tab-active': route.name === item.name,
                'bot-tab-disabled': item.disabled 
              }
            ]"
            :disabled="item.disabled"
            @click="navigateTo(item.name)"
          >
            <Icon :name="item.icon" :size="14" />
            <span>{{ item.label }}</span>
          </button>
        </div>

        <!-- Tab Content -->
        <div class="bot-content">
          <router-view @request-refresh="refresh" />
        </div>
      </div>

      <!-- Sidebar -->
      <aside v-if="online" class="bot-sidebar">
        <!-- Quick Play Card -->
        <Card padding="md" class="sidebar-card">
          <h3 class="sidebar-title">Quick Play</h3>
          <div class="quick-play">
            <Input
              v-model="loadSongUrl"
              placeholder="Paste song link..."
              size="sm"
              @keydown.enter="newSong('play')"
            >
              <template #prefix>
                <Icon name="link" :size="14" />
              </template>
            </Input>
            <div class="quick-play-actions">
              <Button size="sm" @click="newSong('play')" :disabled="!loadSongUrl.trim()">
                <Icon name="play" :size="12" />
                Play
              </Button>
              <Button size="sm" variant="soft" @click="newSong('add')" :disabled="!loadSongUrl.trim()">
                <Icon name="plus" :size="12" />
                Queue
              </Button>
            </div>
          </div>
        </Card>

        <!-- Now Playing Card -->
        <Card v-if="info.song" padding="md" class="sidebar-card now-playing-card">
          <h3 class="sidebar-title">Now Playing</h3>
          <div class="now-playing">
            <div class="now-playing-cover">
              <img :src="getCoverUrl()" alt="Cover" @error="($event.target as HTMLImageElement).style.display = 'none'" />
              <div class="now-playing-icon-fallback">
                <Icon :name="getAudioTypeIcon(info.song.AudioType)" :size="28" :color="getAudioTypeColor(info.song.AudioType)" />
              </div>
            </div>
            <div class="now-playing-info">
              <a :href="info.song.Link" target="_blank" class="now-playing-title">
                {{ info.song.Title }}
              </a>
              <div class="now-playing-source">{{ info.song.Source }}</div>
              <div class="now-playing-meta">
                {{ formatSecondsToTime(info.song.Position) }} / {{ formatSecondsToTime(info.song.Length) }}
              </div>
            </div>
          </div>
        </Card>

        <!-- Queue Card -->
        <Card v-if="info.nowPlaying.Items.length > 0" padding="none" class="sidebar-card queue-card">
          <div class="queue-header">
            <h3 class="sidebar-title">Up Next</h3>
            <Badge size="sm">{{ info.nowPlaying.SongCount }}</Badge>
          </div>
          <div class="queue-list">
            <div
              v-for="(item, index) in info.nowPlaying.Items"
              :key="info.nowPlaying.DisplayOffset + index"
              :class="[
                'queue-item',
                { 
                  'queue-item-current': (info.nowPlaying.DisplayOffset + index) === info.nowPlaying.PlaybackIndex,
                  'queue-item-played': (info.nowPlaying.DisplayOffset + index) < info.nowPlaying.PlaybackIndex
                }
              ]"
            >
              <span class="queue-item-index">{{ info.nowPlaying.DisplayOffset + index + 1 }}</span>
              <Icon :name="getAudioTypeIcon(item.AudioType)" :size="14" :color="getAudioTypeColor(item.AudioType)" />
              <span class="queue-item-title">{{ item.Title }}</span>
            </div>
          </div>
        </Card>
      </aside>
    </div>

    <!-- Play Controls -->
    <PlayControls
      v-if="online"
      :bot-id="currentBotId"
      :info="info"
      @request-refresh="refresh"
    />
  </div>
</template>

<style scoped>
.bot-page {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  padding-bottom: 5rem; /* Space for play controls */
}

/* Header */
.bot-header {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.breadcrumb {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 13px;
}

.breadcrumb-link {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  color: var(--color-fg-muted);
  text-decoration: none;
  transition: color 0.15s ease-out;
}

.breadcrumb-link:hover {
  color: var(--color-fg);
}

.breadcrumb-separator {
  color: var(--color-fg-subtle);
}

.breadcrumb-current {
  color: var(--color-fg-subtle);
  font-weight: 500;
}

.bot-title-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.bot-title {
  font-size: 1.75rem;
  font-weight: 700;
  margin: 0;
  letter-spacing: -0.02em;
}

.bot-meta {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 13px;
  color: var(--color-fg-muted);
  padding-left: 0.5rem;
  border-left: 1px solid var(--color-border);
  margin-left: 0.25rem;
}

.bot-meta-item {
  font-variant-numeric: tabular-nums;
}

.bot-meta-divider {
  color: var(--color-fg-subtle);
}

/* Layout */
.bot-layout {
  display: grid;
  grid-template-columns: 1fr var(--bot-sidebar-width);
  gap: 1.5rem;
}

/* Large screens: full layout */
@media (max-width: 1280px) {
  .bot-layout {
    grid-template-columns: 1fr 280px;
  }
}

/* Tablet: hide bot sidebar */
@media (max-width: 1024px) {
  .bot-layout {
    grid-template-columns: 1fr;
  }
  
  .bot-sidebar {
    display: none;
  }
}

/* Mobile */
@media (max-width: 768px) {
  .bot-page {
    gap: 1rem;
  }
  
  .bot-title {
    font-size: 1.5rem;
  }
  
  .bot-meta {
    display: none;
  }
}

/* Tabs */
.bot-tabs {
  display: flex;
  gap: 0.25rem;
  padding: 0.25rem;
  background: var(--color-bg-inset);
  border-radius: var(--radius-lg);
  margin-bottom: 1rem;
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

.bot-tab {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  flex: 1;
  padding: 0.625rem 0.875rem;
  border: none;
  border-radius: var(--radius-md);
  background: transparent;
  font-size: 13px;
  font-weight: 500;
  color: var(--color-fg-muted);
  cursor: pointer;
  transition: all 0.15s ease-out;
  justify-content: center;
  white-space: nowrap;
}

.bot-tab:hover:not(:disabled) {
  color: var(--color-fg);
}

.bot-tab-active {
  background: var(--color-bg-elevated);
  color: var(--color-fg);
  box-shadow: var(--shadow-sm);
}

.bot-tab-disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

@media (max-width: 480px) {
  .bot-tab {
    flex: 0 0 auto;
    padding: 0.5rem 0.75rem;
  }
  
  .bot-tab span {
    display: none;
  }
}

/* Sidebar */
.bot-sidebar {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.sidebar-card {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.sidebar-title {
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--color-fg-muted);
  margin: 0;
}

/* Info Grid */
.info-grid {
  display: grid;
  gap: 0.5rem;
}

.info-item {
  display: flex;
  justify-content: space-between;
  font-size: 13px;
}

.info-label {
  color: var(--color-fg-muted);
}

.info-value {
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

/* Quick Play */
.quick-play {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.quick-play-actions {
  display: flex;
  gap: 0.5rem;
}

/* Now Playing */
.now-playing {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.now-playing-cover {
  position: relative;
  width: 56px;
  height: 56px;
  flex-shrink: 0;
  border-radius: var(--radius-md);
  background: var(--color-bg-inset);
  overflow: hidden;
}

.now-playing-cover img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.now-playing-icon-fallback {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}

.now-playing-cover img + .now-playing-icon-fallback {
  display: none;
}

.now-playing-info {
  min-width: 0;
  flex: 1;
}

.now-playing-title {
  display: block;
  font-size: 14px;
  font-weight: 600;
  color: var(--color-fg);
  text-decoration: none;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.now-playing-title:hover {
  color: var(--color-accent);
}

.now-playing-source {
  font-size: 12px;
  color: var(--color-fg-muted);
  margin-top: 0.25rem;
}

.now-playing-meta {
  font-size: 12px;
  color: var(--color-fg-subtle);
  font-variant-numeric: tabular-nums;
  margin-top: 0.25rem;
}

/* Queue */
.queue-card {
  max-height: 350px;
  overflow-y: auto;
}

.queue-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--color-border);
  position: sticky;
  top: 0;
  background: var(--color-bg-elevated);
  z-index: 1;
}

.queue-list {
  padding: 0.25rem 0;
}

.queue-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  font-size: 12px;
  transition: background 0.1s ease-out;
}

.queue-item:hover {
  background: var(--color-bg-inset);
}

.queue-item-index {
  width: 24px;
  font-size: 11px;
  font-variant-numeric: tabular-nums;
  color: var(--color-fg-subtle);
  text-align: right;
}

.queue-item-title {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.queue-item-current {
  background: var(--color-accent-muted);
  font-weight: 500;
}

.queue-item-current:hover {
  background: var(--color-accent-muted);
}

.queue-item-played {
  opacity: 0.5;
}
</style>
