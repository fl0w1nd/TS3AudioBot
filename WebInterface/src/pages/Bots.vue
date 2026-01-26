<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { Card, Button, Icon, Badge, Input, Modal } from '@/components/ui'
import { cmd, bot, isError, getErrorMessage } from '@/api/client'
import { useToast } from '@/composables/useToast'
import { BotStatus, type CmdBotInfo } from '@/api/types'

const router = useRouter()
const toast = useToast()

const bots = ref<CmdBotInfo[]>([])
const loading = ref(true)
const searchQuery = ref('')

// Filter state
const showState = ref<string[]>(['Connected', 'Connecting', 'Offline'])

// Auto-refresh timer
let refreshTimer: ReturnType<typeof setInterval> | null = null
const hasConnectingBots = ref(false)

// Modals
const showCreateModal = ref(false)
const showDeleteModal = ref(false)
const showQuickConnectModal = ref(false)
const showEditServerModal = ref(false)
const newBotName = ref('')
const deleteBotName = ref('')
const quickConnectAddress = ref('')
const editServerBotName = ref('')
const editServerAddress = ref('')
const modalLoading = ref(false)

const filteredBots = computed(() => {
  let result = bots.value
  
  // Filter by state
  result = result.filter(bot => {
    const statusName = BotStatus[bot.Status]
    return showState.value.includes(statusName)
  })
  
  // Filter by search
  if (searchQuery.value) {
    const query = searchQuery.value.toLowerCase()
    result = result.filter(bot =>
      bot.Name?.toLowerCase().includes(query) ||
      bot.Server?.toLowerCase().includes(query)
    )
  }
  
  return result
})

async function loadBots() {
  loading.value = true
  
  // Ensure minimum loading time
  const minTime = new Promise(resolve => setTimeout(resolve, 500))
  
  const [res] = await Promise.all([
    cmd<CmdBotInfo[]>('bot', 'list').fetch(),
    minTime
  ])
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    bots.value = res
    
    // Check for connecting bots
    hasConnectingBots.value = res.some(b => b.Status === BotStatus.Connecting)
    
    // Start/stop auto-refresh based on connecting bots
    if (hasConnectingBots.value && !refreshTimer) {
      refreshTimer = setInterval(loadBots, 2000)
    } else if (!hasConnectingBots.value && refreshTimer) {
      clearInterval(refreshTimer)
      refreshTimer = null
    }
  }
  
  loading.value = false
}

function getStatusColor(status: BotStatus) {
  switch (status) {
    case BotStatus.Connected: return 'success'
    case BotStatus.Connecting: return 'warning'
    default: return 'neutral'
  }
}

function getStatusLabel(status: BotStatus) {
  switch (status) {
    case BotStatus.Connected: return 'Connected'
    case BotStatus.Connecting: return 'Connecting'
    default: return 'Offline'
  }
}

function openBot(bot: CmdBotInfo) {
  if (bot.Status === BotStatus.Offline || bot.Id === null) {
    router.push(`/bot_offline/${bot.Name}/settings`)
  } else {
    router.push(`/bot/${bot.Id}/server`)
  }
}

function toggleStateFilter(state: string) {
  const index = showState.value.indexOf(state)
  if (index === -1) {
    showState.value.push(state)
  } else {
    showState.value.splice(index, 1)
  }
}

function clearFilter() {
  showState.value = ['Connected', 'Connecting', 'Offline']
  searchQuery.value = ''
}

// Bot actions
async function startBot(name: string) {
  const res = await cmd<CmdBotInfo>('bot', 'connect', 'template', name).fetch()
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Bot started')
    await loadBots()
  }
}

async function stopBot(id: number) {
  const res = await bot(cmd<void>('bot', 'disconnect'), id).fetch()
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Bot stopped')
    await loadBots()
  }
}

// Create bot
async function createBot() {
  if (!newBotName.value.trim()) return
  
  modalLoading.value = true
  const res = await cmd<void>('settings', 'create', newBotName.value.trim()).fetch()
  modalLoading.value = false
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Bot created')
    showCreateModal.value = false
    newBotName.value = ''
    await loadBots()
  }
}

// Delete bot
function askDeleteBot(name: string) {
  deleteBotName.value = name
  showDeleteModal.value = true
}

async function deleteBot() {
  if (!deleteBotName.value) return
  
  modalLoading.value = true
  const res = await cmd<void>('settings', 'delete', deleteBotName.value).fetch()
  modalLoading.value = false
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Bot deleted')
    showDeleteModal.value = false
    deleteBotName.value = ''
    await loadBots()
  }
}

// Quick connect
async function quickConnect() {
  if (!quickConnectAddress.value.trim()) return
  
  modalLoading.value = true
  const res = await cmd<CmdBotInfo>('bot', 'connect', 'to', quickConnectAddress.value.trim()).fetch()
  modalLoading.value = false
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Bot connecting')
    showQuickConnectModal.value = false
    quickConnectAddress.value = ''
    await loadBots()
  }
}

// Edit server address (for offline bots)
function openEditServerModal(botItem: CmdBotInfo) {
  if (!botItem.Name) return
  editServerBotName.value = botItem.Name
  editServerAddress.value = botItem.Server || ''
  showEditServerModal.value = true
}

async function saveServerAddress() {
  if (!editServerBotName.value) return
  
  modalLoading.value = true
  const res = await cmd<void>(
    'settings', 'bot', 'set', editServerBotName.value, 'connect.address', editServerAddress.value.trim()
  ).fetch()
  modalLoading.value = false
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Server address updated')
    showEditServerModal.value = false
    editServerBotName.value = ''
    editServerAddress.value = ''
    await loadBots()
  }
}

onMounted(loadBots)

onUnmounted(() => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
  }
})
</script>

<template>
  <div class="bots-page">
    <!-- Header -->
    <div class="page-header">
      <div class="page-header-content">
        <h1 class="page-title">Bots</h1>
        <p class="page-description">Manage your TS3AudioBot instances</p>
      </div>
      <div class="page-header-actions">
        <Button variant="ghost" color="neutral" size="sm" @click="loadBots">
          <Icon name="refresh" :size="14" :spin="loading" />
          Refresh
        </Button>
        <Button size="sm" variant="soft" color="neutral" @click="showQuickConnectModal = true">
          <Icon name="external" :size="14" />
          Quick Connect
        </Button>
        <Button size="sm" @click="showCreateModal = true">
          <Icon name="plus" :size="14" />
          Create Bot
        </Button>
      </div>
    </div>

    <!-- Filters -->
    <div class="filters-bar">
      <Input
        v-model="searchQuery"
        placeholder="Search by name or server..."
        size="sm"
        class="search-input"
      >
        <template #prefix>
          <Icon name="search" :size="14" />
        </template>
      </Input>
      
      <div class="state-filters">
        <button
          :class="['state-btn', 'state-connected', { 'state-active': showState.includes('Connected') }]"
          @click="toggleStateFilter('Connected')"
        >
          Connected
        </button>
        <button
          :class="['state-btn', 'state-connecting', { 'state-active': showState.includes('Connecting') }]"
          @click="toggleStateFilter('Connecting')"
        >
          Connecting
        </button>
        <button
          :class="['state-btn', 'state-offline', { 'state-active': showState.includes('Offline') }]"
          @click="toggleStateFilter('Offline')"
        >
          Offline
        </button>
        <Button variant="ghost" color="neutral" size="sm" @click="clearFilter">
          <Icon name="x" :size="12" />
          Clear
        </Button>
      </div>
    </div>

    <!-- Bots Grid -->
    <div class="bots-grid">
      <Card
        v-for="botItem in filteredBots"
        :key="botItem.Id ?? botItem.Name ?? 'unknown'"
        class="bot-card animate-slide-up"
      >
        <div class="bot-card-header">
          <div class="bot-avatar">
            <Icon name="bot" :size="20" />
          </div>
          <Badge :color="getStatusColor(botItem.Status)" size="sm" dot>
            {{ getStatusLabel(botItem.Status) }}
          </Badge>
        </div>
        
        <div class="bot-card-content" @click="openBot(botItem)">
          <h3 class="bot-name">{{ botItem.Name || `Bot #${botItem.Id}` }}</h3>
          <div class="bot-server-row">
            <span class="bot-server">{{ botItem.Server || 'Not connected' }}</span>
            <!-- Edit server address for offline bots -->
            <button
              v-if="botItem.Name && botItem.Status === BotStatus.Offline"
              class="edit-server-btn"
              title="Edit server address"
              @click.stop="openEditServerModal(botItem)"
            >
              <Icon name="edit" :size="12" />
            </button>
          </div>
        </div>
        
        <div class="bot-card-footer">
          <span class="bot-id">ID: {{ botItem.Id ?? 'N/A' }}</span>
          <div class="bot-actions">
            <!-- Start/Stop -->
            <Button
              v-if="botItem.Id === null && botItem.Name"
              variant="ghost"
              color="success"
              size="sm"
              icon-only
              title="Start"
              @click.stop="startBot(botItem.Name!)"
            >
              <Icon name="play" :size="14" />
            </Button>
            <Button
              v-else-if="botItem.Id !== null"
              variant="ghost"
              color="error"
              size="sm"
              icon-only
              title="Stop"
              @click.stop="stopBot(botItem.Id)"
            >
              <Icon name="stop" :size="14" />
            </Button>
            
            <!-- Server view -->
            <Button
              variant="ghost"
              color="neutral"
              size="sm"
              icon-only
              title="Server"
              :disabled="botItem.Id === null"
              @click.stop="router.push(`/bot/${botItem.Id}/server`)"
            >
              <Icon name="tree" :size="14" />
            </Button>
            
            <!-- Settings -->
            <Button
              variant="ghost"
              color="neutral"
              size="sm"
              icon-only
              title="Settings"
              @click.stop="openBot(botItem)"
            >
              <Icon name="settings" :size="14" />
            </Button>
            
            <!-- Delete -->
            <Button
              v-if="botItem.Name"
              variant="ghost"
              color="error"
              size="sm"
              icon-only
              title="Delete"
              @click.stop="askDeleteBot(botItem.Name)"
            >
              <Icon name="trash" :size="14" />
            </Button>
          </div>
        </div>
      </Card>

      <!-- Empty state -->
      <div v-if="!loading && filteredBots.length === 0" class="empty-state">
        <Icon name="bot" :size="48" class="text-subtle" />
        <p v-if="searchQuery || showState.length < 3">No bots matching filters</p>
        <p v-else>No bots available</p>
      </div>
    </div>

    <!-- Loading state -->
    <div v-if="loading && bots.length === 0" class="loading-grid">
      <Card v-for="i in 4" :key="i" class="bot-card-skeleton">
        <div class="skeleton-header">
          <div class="skeleton-avatar animate-shimmer" />
          <div class="skeleton-badge animate-shimmer" />
        </div>
        <div class="skeleton-content">
          <div class="skeleton-title animate-shimmer" />
          <div class="skeleton-text animate-shimmer" />
        </div>
      </Card>
    </div>

    <!-- Create Modal -->
    <Modal v-model:open="showCreateModal" title="Create Bot" size="sm">
      <p class="modal-description">Enter a name for the new bot template.</p>
      <Input
        v-model="newBotName"
        placeholder="Bot name"
        @keydown.enter="createBot"
      />
      <template #footer>
        <Button variant="ghost" color="neutral" @click="showCreateModal = false">Cancel</Button>
        <Button :loading="modalLoading" :disabled="!newBotName.trim()" @click="createBot">Create</Button>
      </template>
    </Modal>

    <!-- Delete Modal -->
    <Modal v-model:open="showDeleteModal" title="Delete Bot" size="sm">
      <p class="modal-description">
        Are you sure you want to delete <strong>{{ deleteBotName }}</strong>? 
        This action cannot be undone.
      </p>
      <template #footer>
        <Button variant="ghost" color="neutral" @click="showDeleteModal = false">Cancel</Button>
        <Button color="error" :loading="modalLoading" @click="deleteBot">Delete</Button>
      </template>
    </Modal>

    <!-- Quick Connect Modal -->
    <Modal v-model:open="showQuickConnectModal" title="Quick Connect" size="sm">
      <p class="modal-description">Enter a server address to connect a new bot.</p>
      <Input
        v-model="quickConnectAddress"
        placeholder="ts3server://example.com"
        @keydown.enter="quickConnect"
      />
      <template #footer>
        <Button variant="ghost" color="neutral" @click="showQuickConnectModal = false">Cancel</Button>
        <Button :loading="modalLoading" :disabled="!quickConnectAddress.trim()" @click="quickConnect">Connect</Button>
      </template>
    </Modal>

    <!-- Edit Server Modal -->
    <Modal v-model:open="showEditServerModal" title="Edit Server Address" size="sm">
      <p class="modal-description">
        Edit server address for <strong>{{ editServerBotName }}</strong>
      </p>
      <Input
        v-model="editServerAddress"
        placeholder="ts3server://example.com"
        @keydown.enter="saveServerAddress"
      />
      <template #footer>
        <Button variant="ghost" color="neutral" @click="showEditServerModal = false">Cancel</Button>
        <Button :loading="modalLoading" @click="saveServerAddress">Save</Button>
      </template>
    </Modal>
  </div>
</template>

<style scoped>
.bots-page {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

/* Page Header */
.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.page-title {
  font-size: 1.75rem;
  font-weight: 700;
  margin: 0;
  letter-spacing: -0.02em;
}

.page-description {
  font-size: 14px;
  color: var(--color-fg-muted);
  margin: 0.25rem 0 0;
}

.page-header-actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

/* Filters */
.filters-bar {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
}

.search-input {
  width: 280px;
}

.state-filters {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.state-btn {
  padding: 0.375rem 0.75rem;
  border: none;
  border-radius: var(--radius-md);
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s ease-out;
  opacity: 0.5;
  background: var(--color-bg-inset);
  color: var(--color-fg);
}

.state-btn.state-active {
  opacity: 1;
}

.state-connected.state-active {
  background: var(--color-success-muted);
  color: var(--color-success);
}

.state-connecting.state-active {
  background: var(--color-warning-muted);
  color: var(--color-warning);
}

.state-offline.state-active {
  background: var(--color-error-muted);
  color: var(--color-error);
}

/* Bots Grid */
.bots-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1rem;
}

.bot-card {
  display: flex;
  flex-direction: column;
  padding: 1.25rem;
}

.bot-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1rem;
}

.bot-avatar {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border-radius: var(--radius-md);
  background: var(--color-accent-muted);
  color: var(--color-accent);
}

.bot-card-content {
  flex: 1;
  cursor: pointer;
}

.bot-card-content:hover .bot-name {
  color: var(--color-accent);
}

.bot-name {
  font-size: 15px;
  font-weight: 600;
  margin: 0;
  transition: color 0.1s ease-out;
}

.bot-server-row {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  margin-top: 0.25rem;
}

.bot-server {
  font-size: 13px;
  color: var(--color-fg-muted);
}

.edit-server-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  padding: 0;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--color-fg-subtle);
  cursor: pointer;
  opacity: 0;
  transition: all 0.1s ease-out;
}

.bot-card:hover .edit-server-btn {
  opacity: 1;
}

.edit-server-btn:hover {
  background: var(--color-bg-inset);
  color: var(--color-fg);
}

.bot-card-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 1rem;
  padding-top: 0.75rem;
  border-top: 1px solid var(--color-border);
}

.bot-id {
  font-size: 11px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  color: var(--color-fg-subtle);
}

.bot-actions {
  display: flex;
  gap: 0.125rem;
}

/* Empty State */
.empty-state {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  padding: 3rem;
  text-align: center;
  color: var(--color-fg-muted);
}

/* Loading */
.loading-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1rem;
}

.bot-card-skeleton {
  padding: 1.25rem;
}

.skeleton-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1rem;
}

.skeleton-avatar {
  width: 40px;
  height: 40px;
  border-radius: var(--radius-md);
}

.skeleton-badge {
  width: 70px;
  height: 18px;
  border-radius: var(--radius-md);
}

.skeleton-content {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.skeleton-title {
  width: 60%;
  height: 18px;
  border-radius: var(--radius-sm);
}

.skeleton-text {
  width: 80%;
  height: 14px;
  border-radius: var(--radius-sm);
}

/* Modal */
.modal-description {
  font-size: 14px;
  color: var(--color-fg-muted);
  margin: 0 0 1rem;
}
</style>
