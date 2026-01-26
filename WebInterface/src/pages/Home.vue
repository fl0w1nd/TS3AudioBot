<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { Card, Button, Icon, Badge, Input } from '@/components/ui'
import { cmd, jmerge, isError, getErrorMessage, api } from '@/api/client'
import { ApiAuth } from '@/api/auth'
import { useToast } from '@/composables/useToast'
import type { CmdVersion, CmdBotInfo } from '@/api/types'

const router = useRouter()
const toast = useToast()

const version = ref<CmdVersion | null>(null)
const bots = ref<CmdBotInfo[]>([])
const loading = ref(true)

// Auth state
const authUid = ref('')
const authToken = ref('')
const isLoggedIn = ref(false)
const authLoading = ref(false)

// Compute full auth string
const authStr = computed({
  get: () => `${authUid.value}:${authToken.value}`,
  set: (val: string) => {
    if (!val.includes(':')) {
      authUid.value = val
      authToken.value = ''
    } else {
      const parts = val.split(':')
      authUid.value = parts[0].replace(/:/g, '')
      authToken.value = parts.slice(1).join(':')
    }
  }
})

// Handle paste of full auth string
function handleAuthInput(val: string) {
  if (val.includes(':')) {
    authStr.value = val
  } else {
    authUid.value = val
  }
}

// Verify auth and save - using jmerge to verify auth works
async function verifyAuth() {
  authLoading.value = true
  
  // Create and set auth
  const newAuth = new ApiAuth(authUid.value, authToken.value)
  api.auth = newAuth
  
  // Test auth with jmerge (empty call tests auth without specific commands)
  // Use bot list which requires auth to verify credentials
  const res = await jmerge().fetch()
  
  if (isError(res)) {
    toast.error('Authentication failed')
    api.auth = ApiAuth.Anonymous
    isLoggedIn.value = false
  } else {
    // Save to localStorage
    localStorage.setItem('api_auth', newAuth.getFullAuth())
    isLoggedIn.value = true
    toast.success('Logged in successfully')
    await loadData()
  }
  
  authLoading.value = false
}

// Watch auth changes
watch([authUid, authToken], async () => {
  if (authUid.value && authToken.value) {
    await verifyAuth()
  }
})

// Logout
function logout() {
  api.auth = ApiAuth.Anonymous
  localStorage.removeItem('api_auth')
  authUid.value = ''
  authToken.value = ''
  isLoggedIn.value = false
  toast.info('Logged out')
}

async function loadData() {
  loading.value = true
  
  // Fetch version
  const versionRes = await cmd<CmdVersion>('version').fetch()
  if (!isError(versionRes)) {
    version.value = versionRes
  }
  
  // Fetch bots
  const botsRes = await cmd<CmdBotInfo[]>('bot', 'list').fetch()
  if (isError(botsRes)) {
    if (isLoggedIn.value) {
      toast.error(getErrorMessage(botsRes))
    }
  } else {
    bots.value = botsRes
  }
  
  loading.value = false
}

onMounted(async () => {
  // Check if already logged in
  const savedAuth = localStorage.getItem('api_auth')
  if (savedAuth) {
    try {
      const auth = ApiAuth.fromString(savedAuth)
      authUid.value = auth.userUid
      authToken.value = auth.token
      api.auth = auth
      isLoggedIn.value = true
    } catch {
      localStorage.removeItem('api_auth')
    }
  }
  await loadData()
})

const features = [
  { icon: 'music', title: 'Multi-Platform Playback', description: 'Stream from YouTube, SoundCloud, Twitch, and local files' },
  { icon: 'bot', title: 'Multi-Bot Support', description: 'Run multiple bots across different servers simultaneously' },
  { icon: 'playlist', title: 'Playlist Management', description: 'Create, edit, and manage playlists with ease' },
  { icon: 'mic', title: 'Voice Recording', description: 'Record channel audio for later playback' },
]
</script>

<template>
  <div class="home">
    <!-- Hero Section -->
    <section class="hero">
      <div class="hero-content">
        <div class="hero-badge">
          <Badge v-if="version" variant="outline" color="accent">
            {{ version.Version }}
          </Badge>
        </div>
        <h1 class="hero-title">
          TS3AudioBot
          <span class="hero-title-accent">Control Center</span>
        </h1>
        <p class="hero-description">
          A powerful TeamSpeak 3 music bot with web interface.
          Stream music from multiple sources, manage playlists, and control bots with ease.
        </p>
      </div>

      <!-- Login Card -->
      <Card padding="lg" class="login-card animate-slide-up">
        <h3 class="login-title">
          <Icon name="user" :size="18" />
          {{ isLoggedIn ? 'Logged In' : 'Login' }}
        </h3>
        
        <template v-if="!isLoggedIn">
          <p class="login-hint">
            Enter your Client UID and Auth Token to access bot controls.
            You can paste the full token (uid:token format).
          </p>
          <div class="login-form">
            <Input
              :model-value="authUid"
              @update:model-value="handleAuthInput"
              placeholder="Client UID"
              size="md"
            >
              <template #prefix>
                <Icon name="user" :size="14" />
              </template>
            </Input>
            
            <div v-if="authUid" class="login-token-row">
              <span class="login-separator">:</span>
              <Input
                v-model="authToken"
                type="password"
                placeholder="Auth Token"
                size="md"
                @keydown.enter="verifyAuth"
              >
                <template #prefix>
                  <Icon name="lock" :size="14" />
                </template>
              </Input>
            </div>
          </div>
          
          <Button 
            v-if="authUid && authToken"
            :loading="authLoading"
            full-width
            @click="verifyAuth"
          >
            <Icon name="check" :size="14" />
            Verify & Login
          </Button>
        </template>
        
        <template v-else>
          <div class="logged-in-info">
            <Badge color="success" dot>Connected</Badge>
            <span class="auth-uid">{{ authUid }}</span>
          </div>
          <div class="logged-in-actions">
            <Button variant="soft" color="neutral" size="sm" @click="logout">
              <Icon name="x" :size="14" />
              Logout
            </Button>
          </div>
        </template>
      </Card>

      <!-- Quick Links (only when logged in) -->
      <div v-if="isLoggedIn" class="quick-links animate-slide-up stagger-1">
        <Card clickable class="quick-link-card" @click="router.push('/bots')">
          <Icon name="bot" :size="24" class="text-accent" />
          <span>Bots Overview</span>
          <Icon name="chevron-right" :size="16" class="text-subtle" />
        </Card>
        <Card clickable class="quick-link-card" @click="router.push('/overview')">
          <Icon name="grid" :size="24" class="text-accent" />
          <span>Dashboard</span>
          <Icon name="chevron-right" :size="16" class="text-subtle" />
        </Card>
      </div>

      <!-- Stats Cards -->
      <div v-if="isLoggedIn" class="stats-grid">
        <Card class="stat-card animate-slide-up stagger-2">
          <div class="stat-icon">
            <Icon name="bot" :size="20" />
          </div>
          <div class="stat-content">
            <div class="stat-value">{{ bots.length }}</div>
            <div class="stat-label">Active Bots</div>
          </div>
        </Card>
        
        <Card class="stat-card animate-slide-up stagger-3">
          <div class="stat-icon stat-icon-success">
            <Icon name="online" :size="20" />
          </div>
          <div class="stat-content">
            <div class="stat-value">{{ bots.filter(b => b.Status === 2).length }}</div>
            <div class="stat-label">Connected</div>
          </div>
        </Card>
        
        <Card class="stat-card animate-slide-up stagger-4">
          <div class="stat-icon stat-icon-info">
            <Icon name="server" :size="20" />
          </div>
          <div class="stat-content">
            <div class="stat-value">{{ version?.BuildConfiguration ?? '-' }}</div>
            <div class="stat-label">Build</div>
          </div>
        </Card>
      </div>
    </section>

    <!-- Features Section -->
    <section class="features">
      <h2 class="section-title">Features</h2>
      <div class="features-grid">
        <Card
          v-for="(feature, index) in features"
          :key="feature.title"
          class="feature-card animate-slide-up"
          :style="{ animationDelay: `${index * 0.05}s` }"
        >
          <div class="feature-icon">
            <Icon :name="feature.icon" :size="20" />
          </div>
          <h3 class="feature-title">{{ feature.title }}</h3>
          <p class="feature-description">{{ feature.description }}</p>
        </Card>
      </div>
    </section>

    <!-- Quick Access (when logged in and has bots) -->
    <section v-if="isLoggedIn && bots.length > 0" class="quick-access">
      <h2 class="section-title">Quick Access</h2>
      <div class="bots-quick-grid">
        <Card
          v-for="bot in bots.slice(0, 4)"
          :key="bot.Id ?? bot.Name ?? 'unknown'"
          clickable
          class="bot-quick-card"
          @click="router.push(`/bot/${bot.Id}/server`)"
        >
          <div class="bot-quick-info">
            <div class="bot-quick-name">{{ bot.Name }}</div>
            <div class="bot-quick-server">{{ bot.Server || 'Not connected' }}</div>
          </div>
          <Badge
            :color="bot.Status === 2 ? 'success' : bot.Status === 1 ? 'warning' : 'neutral'"
            dot
          >
            {{ bot.Status === 2 ? 'Connected' : bot.Status === 1 ? 'Connecting' : 'Offline' }}
          </Badge>
        </Card>
      </div>
    </section>
  </div>
</template>

<style scoped>
.home {
  display: flex;
  flex-direction: column;
  gap: 3rem;
}

/* Hero */
.hero {
  display: grid;
  gap: 1.5rem;
}

.hero-content {
  text-align: center;
  max-width: 640px;
  margin: 0 auto;
}

.hero-badge {
  margin-bottom: 1rem;
}

.hero-title {
  font-size: 2.5rem;
  font-weight: 700;
  letter-spacing: -0.03em;
  line-height: 1.1;
  margin: 0 0 1rem;
}

.hero-title-accent {
  display: block;
  background: linear-gradient(135deg, var(--color-accent), oklch(0.65 0.20 240));
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.hero-description {
  font-size: 1rem;
  color: var(--color-fg-muted);
  line-height: 1.6;
  margin: 0;
}

/* Login Card */
.login-card {
  max-width: 400px;
  margin: 0 auto;
}

.login-title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 1rem;
  font-weight: 600;
  margin: 0 0 0.75rem;
}

.login-hint {
  font-size: 12px;
  color: var(--color-fg-muted);
  margin: 0 0 1rem;
  line-height: 1.5;
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.login-token-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.login-token-row > :last-child {
  flex: 1;
}

.login-separator {
  color: var(--color-fg-muted);
  font-weight: 600;
}

.logged-in-info {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.auth-uid {
  font-size: 12px;
  font-family: var(--font-mono);
  color: var(--color-fg-muted);
}

.logged-in-actions {
  display: flex;
  gap: 0.5rem;
}

/* Quick Links */
.quick-links {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 0.75rem;
  max-width: 500px;
  margin: 0 auto;
}

.quick-link-card {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem 1.25rem;
  font-weight: 500;
}

.quick-link-card > :last-child {
  margin-left: auto;
}

/* Stats */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 1rem;
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1rem 1.25rem;
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

.stat-icon-info {
  background: var(--color-info-muted);
  color: var(--color-info);
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

/* Section Title */
.section-title {
  font-size: 1.25rem;
  font-weight: 600;
  margin: 0 0 1rem;
}

/* Features */
.features-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 1rem;
}

.feature-card {
  padding: 1.25rem;
}

.feature-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border-radius: var(--radius-md);
  background: var(--color-accent-muted);
  color: var(--color-accent);
  margin-bottom: 0.75rem;
}

.feature-title {
  font-size: 14px;
  font-weight: 600;
  margin: 0 0 0.375rem;
}

.feature-description {
  font-size: 13px;
  color: var(--color-fg-muted);
  line-height: 1.5;
  margin: 0;
}

/* Quick Access */
.bots-quick-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 0.75rem;
}

.bot-quick-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
}

.bot-quick-name {
  font-weight: 600;
  font-size: 14px;
}

.bot-quick-server {
  font-size: 12px;
  color: var(--color-fg-muted);
  margin-top: 0.125rem;
}

@media (max-width: 640px) {
  .hero-title {
    font-size: 1.75rem;
  }
}
</style>
