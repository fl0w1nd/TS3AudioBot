<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { Card, Button, Icon, Badge } from '@/components/ui'
import { cmd, isError, getErrorMessage } from '@/api/client'
import { useToast } from '@/composables/useToast'
import { BotStatus, type CmdBotInfo, type CmdVersion } from '@/api/types'
import { formatSecondsToTime } from '@/api/utils'

const router = useRouter()
const toast = useToast()

const version = ref<CmdVersion | null>(null)
const bots = ref<CmdBotInfo[]>([])
const loading = ref(true)

// System info
const systemInfo = ref<{
  memory: number[]
  cpu: number[]
  starttime: string
} | null>(null)

// Graph data
const cpuData = ref<number[]>([])
const memoryData = ref<number[]>([])
const graphLen = 60

// Timer
let refreshTimer: ReturnType<typeof setInterval> | null = null

// Uptime computed
const uptime = computed(() => {
  if (!systemInfo.value?.starttime) return '-'
  const startTime = new Date(systemInfo.value.starttime).getTime()
  const now = Date.now()
  const seconds = Math.floor((now - startTime) / 1000)
  return formatSecondsToTime(seconds)
})

async function loadData() {
  loading.value = true
  
  // Ensure minimum loading time for visual feedback
  const minTime = new Promise(resolve => setTimeout(resolve, 500))
  
  const [versionRes, botsRes] = await Promise.all([
    cmd<CmdVersion>('version').fetch(),
    cmd<CmdBotInfo[]>('bot', 'list').fetch(),
    minTime
  ])
  
  if (!isError(versionRes)) {
    version.value = versionRes
  }
  
  if (!isError(botsRes)) {
    bots.value = botsRes
  } else {
    toast.error(getErrorMessage(botsRes))
  }
  
  loading.value = false
}

async function loadSystemInfo() {
  const res = await cmd<{
    memory: number[]
    cpu: number[]
    starttime: string
  }>('system', 'info').fetch()
  
  if (isError(res)) {
    stopRefresh()
    return
  }
  
  systemInfo.value = res
  
  // Pad arrays
  cpuData.value = padArray(res.cpu, graphLen, 0)
  memoryData.value = padArray(res.memory, graphLen, 0)
}

function padArray<T>(arr: T[], count: number, val: T): T[] {
  if (arr.length < count) {
    return Array<T>(count - arr.length).fill(val).concat(arr)
  }
  return arr.slice(-count)
}

function startRefresh() {
  if (!refreshTimer) {
    refreshTimer = setInterval(loadSystemInfo, 1000)
  }
}

function stopRefresh() {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
}

// Get graph path
function getGraphPath(data: number[], width: number, height: number, maxValue = 100): string {
  if (data.length === 0) return ''
  
  const step = width / (data.length - 1)
  let path = ''
  
  for (let i = 0; i < data.length; i++) {
    const x = i * step
    const y = height - (data[i] / maxValue) * height
    
    if (i === 0) {
      path = `M ${x} ${y}`
    } else {
      path += ` L ${x} ${y}`
    }
  }
  
  return path
}

// Get graph fill path
function getGraphFillPath(data: number[], width: number, height: number, maxValue = 100): string {
  const linePath = getGraphPath(data, width, height, maxValue)
  if (!linePath) return ''
  
  const step = width / (data.length - 1)
  return linePath + ` L ${(data.length - 1) * step} ${height} L 0 ${height} Z`
}

// Get current CPU percentage
const currentCpu = computed(() => {
  if (cpuData.value.length === 0) return 0
  return cpuData.value[cpuData.value.length - 1]?.toFixed(1) ?? 0
})

// Get current memory in MB
const currentMemory = computed(() => {
  if (memoryData.value.length === 0) return 0
  return (memoryData.value[memoryData.value.length - 1] / 1024 / 1024).toFixed(1)
})

const connectedCount = computed(() => bots.value.filter(b => b.Status === BotStatus.Connected).length)
const connectingCount = computed(() => bots.value.filter(b => b.Status === BotStatus.Connecting).length)
const offlineCount = computed(() => bots.value.filter(b => b.Status === BotStatus.Offline).length)

onMounted(async () => {
  await loadData()
  await loadSystemInfo()
  startRefresh()
})

onUnmounted(() => {
  stopRefresh()
})
</script>

<template>
  <div class="overview-page">
    <div class="page-header">
      <h1 class="page-title">Overview</h1>
      <Button variant="ghost" color="neutral" size="sm" @click="loadData">
        <Icon name="refresh" :size="14" :spin="loading" />
        Refresh
      </Button>
    </div>

    <!-- About Section -->
    <section class="section">
      <h2 class="section-title">About</h2>
      <Card padding="lg" class="about-card">
        <div class="about-grid">
          <div class="about-item">
            <span class="about-label">Version</span>
            <span class="about-value">{{ version?.Version ?? '-' }}</span>
          </div>
          <div class="about-item">
            <span class="about-label">Branch</span>
            <span class="about-value">{{ version?.Branch ?? '-' }}</span>
          </div>
          <div class="about-item">
            <span class="about-label">Commit</span>
            <span class="about-value commit-hash">{{ version?.CommitSha?.slice(0, 8) ?? '-' }}</span>
          </div>
          <div class="about-item">
            <span class="about-label">Build</span>
            <span class="about-value">{{ version?.BuildConfiguration ?? '-' }}</span>
          </div>
          <div class="about-item">
            <span class="about-label">Uptime</span>
            <span class="about-value">{{ uptime }}</span>
          </div>
        </div>
      </Card>
    </section>

    <!-- System Graphs -->
    <section class="section">
      <h2 class="section-title">System</h2>
      <div class="graphs-grid">
        <!-- CPU Graph -->
        <Card padding="lg" class="graph-card">
          <div class="graph-header">
            <div class="graph-title">
              <Icon name="server" :size="16" />
              <span>CPU</span>
            </div>
            <div class="graph-value cpu-value">{{ currentCpu }}%</div>
          </div>
          <div class="graph-container">
            <svg class="graph-svg" viewBox="0 0 300 100" preserveAspectRatio="none">
              <defs>
                <linearGradient id="cpuGradient" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stop-color="oklch(0.65 0.20 30)" stop-opacity="0.4" />
                  <stop offset="100%" stop-color="oklch(0.65 0.20 30)" stop-opacity="0" />
                </linearGradient>
              </defs>
              <path
                :d="getGraphFillPath(cpuData, 300, 100)"
                fill="url(#cpuGradient)"
              />
              <path
                :d="getGraphPath(cpuData, 300, 100)"
                fill="none"
                stroke="oklch(0.65 0.20 30)"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
            </svg>
          </div>
        </Card>

        <!-- Memory Graph -->
        <Card padding="lg" class="graph-card">
          <div class="graph-header">
            <div class="graph-title">
              <Icon name="memory" :size="16" />
              <span>Memory</span>
            </div>
            <div class="graph-value memory-value">{{ currentMemory }} MB</div>
          </div>
          <div class="graph-container">
            <svg class="graph-svg" viewBox="0 0 300 100" preserveAspectRatio="none">
              <defs>
                <linearGradient id="memGradient" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stop-color="oklch(0.60 0.20 250)" stop-opacity="0.4" />
                  <stop offset="100%" stop-color="oklch(0.60 0.20 250)" stop-opacity="0" />
                </linearGradient>
              </defs>
              <path
                :d="getGraphFillPath(memoryData, 300, 100, Math.max(...memoryData) * 1.2 || 100)"
                fill="url(#memGradient)"
              />
              <path
                :d="getGraphPath(memoryData, 300, 100, Math.max(...memoryData) * 1.2 || 100)"
                fill="none"
                stroke="oklch(0.60 0.20 250)"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
            </svg>
          </div>
        </Card>
      </div>
    </section>

    <!-- Bots Overview -->
    <section class="section">
      <h2 class="section-title">Bots Status</h2>
      <div class="stats-grid">
        <Card padding="md" class="stat-card">
          <div class="stat-icon">
            <Icon name="bot" :size="20" />
          </div>
          <div class="stat-content">
            <div class="stat-value">{{ bots.length }}</div>
            <div class="stat-label">Total Bots</div>
          </div>
        </Card>
        
        <Card padding="md" class="stat-card">
          <div class="stat-icon stat-icon-success">
            <Icon name="online" :size="20" />
          </div>
          <div class="stat-content">
            <div class="stat-value">{{ connectedCount }}</div>
            <div class="stat-label">Connected</div>
          </div>
        </Card>
        
        <Card padding="md" class="stat-card">
          <div class="stat-icon stat-icon-warning">
            <Icon name="loading" :size="20" />
          </div>
          <div class="stat-content">
            <div class="stat-value">{{ connectingCount }}</div>
            <div class="stat-label">Connecting</div>
          </div>
        </Card>
        
        <Card padding="md" class="stat-card">
          <div class="stat-icon stat-icon-error">
            <Icon name="offline" :size="20" />
          </div>
          <div class="stat-content">
            <div class="stat-value">{{ offlineCount }}</div>
            <div class="stat-label">Offline</div>
          </div>
        </Card>
      </div>
    </section>

    <!-- Active Bots List -->
    <section v-if="bots.filter(b => b.Status === BotStatus.Connected).length > 0" class="section">
      <h2 class="section-title">Active Bots</h2>
      <div class="bots-list">
        <Card
          v-for="bot in bots.filter(b => b.Status === BotStatus.Connected)"
          :key="bot.Id ?? 'unknown'"
          clickable
          padding="md"
          class="bot-item"
          @click="router.push(`/bot/${bot.Id}/server`)"
        >
          <div class="bot-info">
            <div class="bot-name">{{ bot.Name }}</div>
            <div class="bot-server">{{ bot.Server }}</div>
          </div>
          <Badge color="success" dot size="sm">Connected</Badge>
        </Card>
      </div>
    </section>
  </div>
</template>

<style scoped>
.overview-page {
  display: flex;
  flex-direction: column;
  gap: 2rem;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.page-title {
  font-size: 1.75rem;
  font-weight: 700;
  margin: 0;
  letter-spacing: -0.02em;
}

.section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.section-title {
  font-size: 1rem;
  font-weight: 600;
  margin: 0;
}

/* About */
.about-card {
  background: var(--color-bg-elevated);
}

.about-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 1.5rem;
}

.about-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.about-label {
  font-size: 12px;
  color: var(--color-fg-muted);
}

.about-value {
  font-size: 14px;
  font-weight: 600;
}

.commit-hash {
  font-family: var(--font-mono);
  font-size: 13px;
}

/* Graphs */
.graphs-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 1rem;
}

.graph-card {
  overflow: hidden;
}

.graph-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.75rem;
}

.graph-title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 13px;
  font-weight: 500;
  color: var(--color-fg-muted);
}

.graph-value {
  font-size: 1.25rem;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}

.cpu-value {
  color: oklch(0.65 0.20 30);
}

.memory-value {
  color: oklch(0.60 0.20 250);
}

.graph-container {
  height: 100px;
  margin: -0.5rem -1.25rem -1.25rem;
}

.graph-svg {
  width: 100%;
  height: 100%;
}

/* Stats Grid */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 1rem;
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.stat-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: var(--radius-md);
  background: var(--color-accent-muted);
  color: var(--color-accent);
}

.stat-icon-success {
  background: var(--color-success-muted);
  color: var(--color-success);
}

.stat-icon-warning {
  background: var(--color-warning-muted);
  color: var(--color-warning);
}

.stat-icon-error {
  background: var(--color-error-muted);
  color: var(--color-error);
}

.stat-value {
  font-size: 1.5rem;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  line-height: 1;
}

.stat-label {
  font-size: 12px;
  color: var(--color-fg-muted);
  margin-top: 0.25rem;
}

/* Bots List */
.bots-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.bot-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.bot-name {
  font-weight: 600;
  font-size: 14px;
}

.bot-server {
  font-size: 12px;
  color: var(--color-fg-muted);
  margin-top: 0.125rem;
}
</style>
