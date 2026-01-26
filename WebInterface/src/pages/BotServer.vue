<script setup lang="ts">
import { ref, inject, onMounted, onUnmounted } from 'vue'
import { Card, Button, Icon, Input, Modal } from '@/components/ui'
import { cmd, bot, jmerge, isError, getErrorMessage } from '@/api/client'
import { useToast } from '@/composables/useToast'
import { type CmdServerTree, type CmdServerTreeChannel, type CmdServerTreeUser, type CmdWhisperList, TargetSendMode } from '@/api/types'
import TreeNode from '@/components/bot/TreeNode.vue'

interface ChannelNode {
  own: CmdServerTreeChannel
  after?: ChannelNode
  children: ChannelNode[]
  users: CmdServerTreeUser[]
}

interface TreeMeta {
  tree: CmdServerTree
  whisperList: CmdWhisperList
}

const botId = inject<{ value: number }>('botId')!
const toast = useToast()

const loading = ref(true)
const rootNode = ref<ChannelNode | null>(null)
const meta = ref<TreeMeta | null>(null)
const expandedChannels = ref<Set<number>>(new Set())

// Refresh timer (5 seconds)
let refreshTimer: ReturnType<typeof setInterval> | null = null

// Password modal
const showPasswordModal = ref(false)
const passwordChannelId = ref<number | null>(null)
const passwordChannelName = ref('')
const channelPassword = ref('')

async function loadTree() {
  loading.value = true
  // Ensure minimum loading time
  const minTime = new Promise(resolve => setTimeout(resolve, 500))

  const [res] = await Promise.all([
    bot(
      jmerge(
        cmd<CmdServerTree>('server', 'tree'),
        cmd<CmdWhisperList>('whisper', 'list')
      ),
      botId.value
    ).fetch(),
    minTime
  ])
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    stopRefresh()
    loading.value = false
    return
  }
  
  const [tree, whisperList] = res
  buildTree(tree)
  meta.value = { tree, whisperList }
  loading.value = false
}

function buildTree(tree: CmdServerTree) {
  const nodes: Record<number, ChannelNode> = {}
  
  nodes[0] = {
    own: { Id: 0, Name: tree.Server.Name, Parent: -1, Order: 0, HasPassword: false, Subscribed: true },
    children: [],
    users: []
  }
  
  for (const chan of Object.values(tree.Channels)) {
    nodes[chan.Id] = { own: chan, children: [], users: [] }
    expandedChannels.value.add(chan.Id)
  }
  
  for (const chan of Object.values(tree.Channels)) {
    const parent = nodes[chan.Parent]
    if (parent) parent.children.push(nodes[chan.Id])
    const orderNode = nodes[chan.Order]
    if (orderNode) orderNode.after = nodes[chan.Id]
  }
  
  for (const node of Object.values(nodes)) {
    if (node.children.length === 0) continue
    const sorted: ChannelNode[] = []
    let current = node.children.find(n => n.own.Order === 0)
    while (current) {
      sorted.push(current)
      current = current.after
    }
    if (sorted.length === node.children.length) node.children = sorted
  }
  
  for (const client of Object.values(tree.Clients)) {
    const channel = nodes[client.Channel]
    if (channel) channel.users.push(client)
  }
  
  rootNode.value = nodes[0]
}

function startRefresh() {
  if (!refreshTimer) refreshTimer = setInterval(loadTree, 5000)
}

function stopRefresh() {
  if (refreshTimer) { clearInterval(refreshTimer); refreshTimer = null }
}

function toggleExpand(channelId: number) {
  if (expandedChannels.value.has(channelId)) {
    expandedChannels.value.delete(channelId)
  } else {
    expandedChannels.value.add(channelId)
  }
}

async function moveToChannel(channelId: number, password?: string) {
  const args = password ? ['bot', 'move', String(channelId), password] : ['bot', 'move', String(channelId)]
  const res = await bot(cmd<void>(...args), botId.value).fetch()
  if (isError(res)) { toast.error(getErrorMessage(res)); return }
  toast.success('Moved to channel')
  await loadTree()
}

function handleChannelClick(channel: CmdServerTreeChannel) {
  if (channel.Id === 0) return
  if (meta.value && channel.Id === meta.value.tree.Clients[meta.value.tree.OwnClient]?.Channel) return
  
  if (channel.HasPassword) {
    passwordChannelId.value = channel.Id
    passwordChannelName.value = channel.Name
    channelPassword.value = ''
    showPasswordModal.value = true
  } else {
    moveToChannel(channel.Id)
  }
}

async function submitPassword() {
  if (passwordChannelId.value === null) return
  await moveToChannel(passwordChannelId.value, channelPassword.value)
  showPasswordModal.value = false
  channelPassword.value = ''
}

async function whisperToChannel(channelId: number) {
  if (!meta.value) return
  
  if (meta.value.whisperList.SendMode !== TargetSendMode.Whisper) {
    const whisperRes = await bot(cmd<void>('whisper', 'subscription'), botId.value).fetch()
    if (isError(whisperRes)) { toast.error(getErrorMessage(whisperRes)); return }
  }
  
  const res = await bot(cmd<void>('subscribe', 'channel', String(channelId)), botId.value).fetch()
  if (isError(res)) { toast.error(getErrorMessage(res)); return }
  toast.success('Whispering to channel')
  await loadTree()
}

async function stopWhisperToChannel(channelId: number) {
  const res = await bot(cmd<void>('unsubscribe', 'channel', String(channelId)), botId.value).fetch()
  if (isError(res)) { toast.error(getErrorMessage(res)); return }
  toast.success('Stopped whispering')
  await loadTree()
}

async function voiceHere() {
  const res = await bot(cmd<void>('whisper', 'off'), botId.value).fetch()
  if (isError(res)) { toast.error(getErrorMessage(res)); return }
  toast.success('Voice mode enabled')
  await loadTree()
}

async function renameBot(name: string) {
  const res = await bot(cmd<void>('bot', 'name', name), botId.value).fetch()
  if (isError(res)) { toast.error(getErrorMessage(res)); return }
  toast.success('Bot renamed')
  await loadTree()
}

onMounted(() => { loadTree(); startRefresh() })
onUnmounted(() => { stopRefresh() })
</script>

<template>
  <div class="server-tree-page">
    <div class="page-header">
      <h2 class="page-title">Server Tree</h2>
      <Button variant="ghost" color="neutral" size="sm" @click="loadTree">
        <Icon name="refresh" :size="14" :spin="loading" />
        Refresh
      </Button>
    </div>

    <Card padding="none" class="tree-card">
      <div v-if="loading && !rootNode" class="loading-state">
        <Icon name="loading" :size="24" class="animate-spin text-muted" />
        <span class="text-muted">Loading server tree...</span>
      </div>
      
      <div v-else-if="rootNode && meta" class="tree-container">
        <div class="tree-server">
          <Icon name="server" :size="16" />
          <span class="server-name">{{ rootNode.own.Name }}</span>
        </div>
        
        <ul class="tree-list root-list">
          <TreeNode
            v-for="node in rootNode.children"
            :key="node.own.Id"
            :node="node"
            :meta="meta"
            :expanded-channels="expandedChannels"
            @toggle="toggleExpand"
            @move="handleChannelClick"
            @whisper="whisperToChannel"
            @stop-whisper="stopWhisperToChannel"
            @voice-here="voiceHere"
            @rename-bot="renameBot"
          />
        </ul>
      </div>
    </Card>

    <Modal v-model:open="showPasswordModal" title="Channel Password" size="sm">
      <p class="modal-description">Enter password for <strong>{{ passwordChannelName }}</strong></p>
      <Input v-model="channelPassword" type="password" placeholder="Password" @keydown.enter="submitPassword" />
      <template #footer>
        <Button variant="ghost" color="neutral" @click="showPasswordModal = false">Cancel</Button>
        <Button @click="submitPassword">Join</Button>
      </template>
    </Modal>
  </div>
</template>

<style scoped>
.server-tree-page {
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

.tree-card {
  overflow: hidden;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  padding: 3rem;
}

.tree-container {
  padding: 1rem;
}

.tree-server {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 0;
  font-weight: 600;
  font-size: 14px;
  border-bottom: 1px solid var(--color-border);
  margin-bottom: 0.5rem;
}

.tree-list {
  list-style: none;
  margin: 0;
  padding: 0 0 0 1rem;
}

.root-list {
  padding-left: 0;
}

.modal-description {
  font-size: 14px;
  color: var(--color-fg-muted);
  margin: 0 0 1rem;
}
</style>
