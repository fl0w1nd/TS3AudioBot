<script setup lang="ts">
import { ref, computed, inject, onMounted, watch } from 'vue'
import { Card, Button, Icon, Badge, Input, Modal } from '@/components/ui'
import { cmd, bot, isError, getErrorMessage } from '@/api/client'
import { useToast } from '@/composables/useToast'
import type { CmdPlaylistInfo, CmdPlaylist } from '@/api/types'
import { getAudioTypeIcon, getAudioTypeColor, generatePlaylistImage, findDropLink } from '@/api/utils'

const botId = inject<{ value: number }>('botId')!
const toast = useToast()

const playlists = ref<CmdPlaylistInfo[]>([])
const loading = ref(true)
const filter = ref('')

// Selected playlist
const selectedPlaylistId = ref<string | null>(null)
const playlistData = ref<CmdPlaylist | null>(null)
const playlistLoading = ref(false)

// Pagination
const perPage = 20
const currentPage = ref(1)

// Modals
const showCreateModal = ref(false)
const showDeletePlaylistModal = ref(false)
const showRenamePlaylistModal = ref(false)
const showRenameItemModal = ref(false)
const newPlaylistId = ref('')
const newPlaylistTitle = ref('')
const deletePlaylistTarget = ref<CmdPlaylistInfo | null>(null)
const renamePlaylistTitle = ref('')
const renameItemIndex = ref<number | null>(null)
const renameItemTitle = ref('')
const modalLoading = ref(false)

// Add song
const addSongLink = ref('')

// Drag state
const draggingIndex = ref<number | null>(null)

const filteredPlaylists = computed(() => {
  if (!filter.value) return playlists.value
  const lc = filter.value.toLowerCase()
  return playlists.value.filter(p => 
    p.Id.toLowerCase().includes(lc) || 
    p.Title?.toLowerCase().includes(lc)
  )
})

const isEditing = computed(() => selectedPlaylistId.value !== null)

async function loadPlaylists() {
  loading.value = true
  
  // Ensure minimum loading time
  const minTime = new Promise(resolve => setTimeout(resolve, 500))

  const [res] = await Promise.all([
    bot(cmd<CmdPlaylistInfo[]>('list', 'list'), botId.value).fetch(),
    minTime
  ])
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    playlists.value = res
  }
  
  loading.value = false
}

async function loadPlaylist(playlistId: string) {
  playlistLoading.value = true
  
  const offset = (currentPage.value - 1) * perPage
  const res = await bot(
    cmd<CmdPlaylist>('list', 'show', playlistId, String(offset), String(perPage)),
    botId.value
  ).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    playlistData.value = null
  } else {
    playlistData.value = res
    currentPage.value = Math.floor(res.DisplayOffset / perPage) + 1
  }
  
  playlistLoading.value = false
}

function selectPlaylist(playlistId: string) {
  selectedPlaylistId.value = playlistId
  currentPage.value = 1
  loadPlaylist(playlistId)
}

function backToList() {
  selectedPlaylistId.value = null
  playlistData.value = null
  loadPlaylists()
}

// Create playlist
async function createPlaylist() {
  if (!newPlaylistId.value.trim()) return
  
  const createdId = newPlaylistId.value.trim()
  
  modalLoading.value = true
  const res = await bot(
    cmd<void>('list', 'create', createdId, newPlaylistTitle.value.trim() || createdId),
    botId.value
  ).fetch()
  modalLoading.value = false
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Playlist created')
    showCreateModal.value = false
    newPlaylistId.value = ''
    newPlaylistTitle.value = ''
    await loadPlaylists()
    selectPlaylist(createdId)
  }
}

// Delete playlist
function askDeletePlaylist(playlist: CmdPlaylistInfo) {
  deletePlaylistTarget.value = playlist
  showDeletePlaylistModal.value = true
}

async function deletePlaylist() {
  if (!deletePlaylistTarget.value) return
  
  modalLoading.value = true
  const res = await bot(cmd<void>('list', 'delete', deletePlaylistTarget.value.Id), botId.value).fetch()
  modalLoading.value = false
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Playlist deleted')
    showDeletePlaylistModal.value = false
    
    if (selectedPlaylistId.value === deletePlaylistTarget.value.Id) {
      backToList()
    } else {
      await loadPlaylists()
    }
    
    deletePlaylistTarget.value = null
  }
}

// Rename playlist
function openRenamePlaylistModal() {
  if (!playlistData.value) return
  renamePlaylistTitle.value = playlistData.value.Title
  showRenamePlaylistModal.value = true
}

async function renamePlaylist() {
  if (!selectedPlaylistId.value || !renamePlaylistTitle.value.trim()) return
  
  modalLoading.value = true
  const res = await bot(
    cmd<void>('list', 'name', selectedPlaylistId.value, renamePlaylistTitle.value.trim()),
    botId.value
  ).fetch()
  modalLoading.value = false
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Playlist renamed')
    showRenamePlaylistModal.value = false
    if (playlistData.value) {
      playlistData.value.Title = renamePlaylistTitle.value.trim()
    }
    await loadPlaylists()
  }
}

// Play playlist
async function playPlaylist(playlistId: string, index?: number) {
  const args = index !== undefined 
    ? ['list', 'play', playlistId, String(index)]
    : ['list', 'play', playlistId]
  
  const res = await bot(cmd<void>(...args), botId.value).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Playing playlist')
  }
}

// Add song
async function addSong() {
  if (!selectedPlaylistId.value || !addSongLink.value.trim()) return
  
  playlistLoading.value = true
  const res = await bot(
    cmd<void>('list', 'add', selectedPlaylistId.value, addSongLink.value.trim()),
    botId.value
  ).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    playlistLoading.value = false
  } else {
    toast.success('Song added')
    addSongLink.value = ''
    await loadPlaylist(selectedPlaylistId.value)
  }
}

// Remove song - use 'list item delete'
async function removeSong(index: number) {
  if (!selectedPlaylistId.value) return
  
  playlistLoading.value = true
  const res = await bot(
    cmd<void>('list', 'item', 'delete', selectedPlaylistId.value, index),
    botId.value
  ).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    playlistLoading.value = false
  } else {
    toast.success('Song removed')
    await loadPlaylist(selectedPlaylistId.value)
  }
}

// Rename song item
function openRenameItemModal(index: number, currentTitle: string) {
  renameItemIndex.value = index
  renameItemTitle.value = currentTitle
  showRenameItemModal.value = true
}

async function renameItem() {
  if (!selectedPlaylistId.value || renameItemIndex.value === null || !renameItemTitle.value.trim()) return
  
  modalLoading.value = true
  const res = await bot(
    cmd<void>('list', 'item', 'name', selectedPlaylistId.value, renameItemIndex.value, renameItemTitle.value.trim()),
    botId.value
  ).fetch()
  modalLoading.value = false
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Item renamed')
    showRenameItemModal.value = false
    await loadPlaylist(selectedPlaylistId.value)
  }
}

// Move song (drag & drop)
async function moveItem(fromIndex: number, toIndex: number) {
  if (!selectedPlaylistId.value || fromIndex === toIndex) return
  
  playlistLoading.value = true
  const res = await bot(
    cmd<void>('list', 'item', 'move', selectedPlaylistId.value, fromIndex, toIndex),
    botId.value
  ).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    playlistLoading.value = false
  } else {
    await loadPlaylist(selectedPlaylistId.value)
  }
}

// Insert song at specific position (for drag-drop URL)
async function insertItem(link: string, indexTo: number) {
  if (!selectedPlaylistId.value || !link.trim()) return
  
  playlistLoading.value = true
  const res = await bot(
    cmd<void>('list', 'insert', selectedPlaylistId.value, indexTo, link.trim()),
    botId.value
  ).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
    playlistLoading.value = false
  } else {
    toast.success('Song added')
    await loadPlaylist(selectedPlaylistId.value)
  }
}

// Drag handlers
function onDragStart(index: number, event: DragEvent) {
  draggingIndex.value = index
  event.dataTransfer?.setData('text/plain', String(index))
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'copyMove'
  }
}

function onDragOver(event: DragEvent) {
  event.preventDefault()
  if (event.dataTransfer) {
    event.dataTransfer.dropEffect = draggingIndex.value !== null ? 'move' : 'copy'
  }
}

function onDrop(toIndex: number, event: DragEvent) {
  event.preventDefault()
  
  if (draggingIndex.value !== null && playlistData.value) {
    // Internal drag: reorder items
    const fromIndex = playlistData.value.DisplayOffset + draggingIndex.value
    const targetIndex = playlistData.value.DisplayOffset + toIndex
    moveItem(fromIndex, targetIndex)
  } else if (event.dataTransfer && playlistData.value) {
    // External drop: try to find a URL
    const link = findDropLink(event.dataTransfer)
    if (link) {
      const targetIndex = playlistData.value.DisplayOffset + toIndex
      insertItem(link, targetIndex)
    } else {
      toast.error('Could not find any link')
    }
  }
  
  draggingIndex.value = null
}

function onDragEnd() {
  draggingIndex.value = null
}

// Pagination
function onPageChange(page: number) {
  currentPage.value = page
  if (selectedPlaylistId.value) {
    loadPlaylist(selectedPlaylistId.value)
  }
}

const totalPages = computed(() => {
  if (!playlistData.value) return 1
  return Math.ceil(playlistData.value.SongCount / perPage)
})

// Watch for playlist selection
watch(selectedPlaylistId, (newId) => {
  if (newId) {
    loadPlaylist(newId)
  }
})

onMounted(loadPlaylists)
</script>

<template>
  <div class="playlists-page">
    <!-- Playlist List View -->
    <template v-if="!isEditing">
      <div class="page-header">
        <h2 class="page-title">Playlists</h2>
        <div class="page-actions">
          <Button variant="ghost" color="neutral" size="sm" @click="loadPlaylists">
            <Icon name="refresh" :size="14" :spin="loading" />
            Refresh
          </Button>
          <Button size="sm" @click="showCreateModal = true">
            <Icon name="plus" :size="14" />
            Create
          </Button>
        </div>
      </div>

      <Input
        v-model="filter"
        placeholder="Filter playlists..."
        size="sm"
        class="filter-input"
      >
        <template #prefix>
          <Icon name="search" :size="14" />
        </template>
      </Input>

      <div class="playlists-grid">
        <!-- Create card -->
        <Card clickable class="playlist-card create-card" @click="showCreateModal = true">
          <div class="playlist-cover create-cover">
            <Icon name="plus" :size="32" />
          </div>
          <div class="playlist-info">
            <div class="playlist-title">Create New</div>
          </div>
        </Card>

        <!-- Playlist cards -->
        <Card 
          v-for="playlist in filteredPlaylists" 
          :key="playlist.Id"
          clickable 
          class="playlist-card"
          @click="selectPlaylist(playlist.Id)"
        >
          <div class="playlist-cover">
            <canvas 
              :ref="el => el && generatePlaylistImage(playlist.Id, el as HTMLCanvasElement)"
              width="120"
              height="120"
            />
            <button class="play-overlay" @click.stop="playPlaylist(playlist.Id)">
              <Icon name="play" :size="24" />
            </button>
          </div>
          <div class="playlist-info">
            <div class="playlist-title">{{ playlist.Title || playlist.Id }}</div>
            <div class="playlist-meta">{{ playlist.SongCount }} songs</div>
          </div>
        </Card>
      </div>

      <div v-if="loading" class="loading-state">
        <Icon name="loading" :size="24" class="animate-spin text-muted" />
      </div>
    </template>

    <!-- Playlist Editor View -->
    <template v-else>
      <div class="page-header">
        <div class="page-header-left">
          <Button variant="ghost" color="neutral" size="sm" @click="backToList">
            <Icon name="chevron-left" :size="14" />
            Back
          </Button>
          <div class="playlist-header-info" v-if="playlistData">
            <h2 class="page-title">{{ playlistData.Title }}</h2>
            <Badge color="neutral" size="sm">{{ playlistData.SongCount }} songs</Badge>
          </div>
        </div>
        <div class="page-actions">
          <Button variant="ghost" color="neutral" size="sm" @click="openRenamePlaylistModal">
            <Icon name="edit" :size="14" />
            Rename
          </Button>
          <Button variant="ghost" color="neutral" size="sm" @click="playPlaylist(selectedPlaylistId!)">
            <Icon name="play" :size="14" />
            Play
          </Button>
          <Button variant="ghost" color="error" size="sm" @click="askDeletePlaylist({ Id: selectedPlaylistId!, Title: playlistData?.Title || '', SongCount: playlistData?.SongCount || 0, DisplayOffset: 0 })">
            <Icon name="trash" :size="14" />
            Delete
          </Button>
        </div>
      </div>

      <!-- Add song -->
      <div class="add-song-bar">
        <Input
          v-model="addSongLink"
          placeholder="Enter song URL to add..."
          size="sm"
          @keydown.enter="addSong"
        >
          <template #prefix>
            <Icon name="music" :size="14" />
          </template>
        </Input>
        <Button size="sm" :disabled="!addSongLink.trim()" @click="addSong">
          <Icon name="plus" :size="14" />
          Add
        </Button>
      </div>

      <!-- Songs list -->
      <Card padding="none">
        <div v-if="playlistLoading" class="loading-state">
          <Icon name="loading" :size="24" class="animate-spin text-muted" />
        </div>
        
        <div v-else-if="playlistData && playlistData.Items.length > 0" class="songs-list">
          <div
            v-for="(item, index) in playlistData.Items"
            :key="playlistData.DisplayOffset + index"
            class="song-item"
            :class="{ 'dragging': draggingIndex === index }"
            draggable="true"
            @dragstart="onDragStart(index, $event)"
            @dragover="onDragOver"
            @drop="onDrop(index, $event)"
            @dragend="onDragEnd"
          >
            <div class="song-drag-handle">
              <Icon name="grip" :size="14" />
            </div>
            <div class="song-index">{{ playlistData.DisplayOffset + index + 1 }}</div>
            <div class="song-icon">
              <Icon :name="getAudioTypeIcon(item.AudioType)" :size="16" :color="getAudioTypeColor(item.AudioType)" />
            </div>
            <div class="song-info">
              <div class="song-title">{{ item.Title }}</div>
              <a v-if="item.Link" :href="item.Link" target="_blank" class="song-link">
                {{ item.Link }}
              </a>
            </div>
            <div class="song-actions">
              <Button 
                variant="ghost" 
                color="neutral" 
                size="sm" 
                icon-only
                title="Play from here"
                @click="playPlaylist(selectedPlaylistId!, playlistData.DisplayOffset + index)"
              >
                <Icon name="play" :size="14" />
              </Button>
              <Button 
                variant="ghost" 
                color="neutral" 
                size="sm" 
                icon-only
                title="Rename"
                @click="openRenameItemModal(playlistData.DisplayOffset + index, item.Title)"
              >
                <Icon name="edit" :size="14" />
              </Button>
              <Button 
                variant="ghost" 
                color="error" 
                size="sm" 
                icon-only
                title="Remove"
                @click="removeSong(playlistData.DisplayOffset + index)"
              >
                <Icon name="x" :size="14" />
              </Button>
            </div>
          </div>
        </div>

        <div v-else class="empty-state">
          <Icon name="playlist" :size="48" class="text-subtle" />
          <p>Playlist is empty</p>
          <p class="text-muted">Add songs using the input above</p>
        </div>
      </Card>

      <!-- Pagination -->
      <div v-if="playlistData && totalPages > 1" class="pagination">
        <Button 
          variant="ghost" 
          color="neutral" 
          size="sm"
          :disabled="currentPage === 1"
          @click="onPageChange(currentPage - 1)"
        >
          <Icon name="chevron-left" :size="14" />
        </Button>
        <span class="pagination-info">Page {{ currentPage }} of {{ totalPages }}</span>
        <Button 
          variant="ghost" 
          color="neutral" 
          size="sm"
          :disabled="currentPage === totalPages"
          @click="onPageChange(currentPage + 1)"
        >
          <Icon name="chevron-right" :size="14" />
        </Button>
      </div>
    </template>

    <!-- Create Playlist Modal -->
    <Modal v-model:open="showCreateModal" title="Create Playlist" size="sm">
      <div class="modal-form">
        <div class="form-field">
          <label class="form-label">File ID</label>
          <Input v-model="newPlaylistId" placeholder="my-playlist" />
          <p class="form-hint">Used as filename, no spaces or special characters</p>
        </div>
        <div class="form-field">
          <label class="form-label">Title</label>
          <Input v-model="newPlaylistTitle" placeholder="My Playlist" />
        </div>
      </div>
      <template #footer>
        <Button variant="ghost" color="neutral" @click="showCreateModal = false">Cancel</Button>
        <Button :loading="modalLoading" :disabled="!newPlaylistId.trim()" @click="createPlaylist">Create</Button>
      </template>
    </Modal>

    <!-- Delete Playlist Modal -->
    <Modal v-model:open="showDeletePlaylistModal" title="Delete Playlist" size="sm">
      <p class="modal-description">
        Are you sure you want to delete <strong>{{ deletePlaylistTarget?.Title || deletePlaylistTarget?.Id }}</strong>?
        This action cannot be undone.
      </p>
      <template #footer>
        <Button variant="ghost" color="neutral" @click="showDeletePlaylistModal = false">Cancel</Button>
        <Button color="error" :loading="modalLoading" @click="deletePlaylist">Delete</Button>
      </template>
    </Modal>

    <!-- Rename Playlist Modal -->
    <Modal v-model:open="showRenamePlaylistModal" title="Rename Playlist" size="sm">
      <Input v-model="renamePlaylistTitle" placeholder="New title" @keydown.enter="renamePlaylist" />
      <template #footer>
        <Button variant="ghost" color="neutral" @click="showRenamePlaylistModal = false">Cancel</Button>
        <Button :loading="modalLoading" :disabled="!renamePlaylistTitle.trim()" @click="renamePlaylist">Rename</Button>
      </template>
    </Modal>

    <!-- Rename Item Modal -->
    <Modal v-model:open="showRenameItemModal" title="Rename Song" size="sm">
      <Input v-model="renameItemTitle" placeholder="New title" @keydown.enter="renameItem" />
      <template #footer>
        <Button variant="ghost" color="neutral" @click="showRenameItemModal = false">Cancel</Button>
        <Button :loading="modalLoading" :disabled="!renameItemTitle.trim()" @click="renameItem">Rename</Button>
      </template>
    </Modal>
  </div>
</template>

<style scoped>
.playlists-page {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 1rem;
}

.page-header-left {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.playlist-header-info {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.page-title {
  font-size: 1rem;
  font-weight: 600;
  margin: 0;
}

.page-actions {
  display: flex;
  gap: 0.5rem;
}

.filter-input {
  max-width: 300px;
}

/* Playlists Grid */
.playlists-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: 1rem;
}

.playlist-card {
  padding: 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.playlist-cover {
  position: relative;
  aspect-ratio: 1;
  border-radius: var(--radius-md);
  overflow: hidden;
  background: var(--color-bg-inset);
}

.playlist-cover canvas {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.play-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.5);
  color: white;
  opacity: 0;
  transition: opacity 0.15s ease-out;
  border: none;
  cursor: pointer;
}

.playlist-card:hover .play-overlay {
  opacity: 1;
}

.create-card .playlist-cover {
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--color-fg-muted);
}

.create-cover {
  display: flex;
  align-items: center;
  justify-content: center;
}

.playlist-info {
  text-align: center;
}

.playlist-title {
  font-size: 13px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.playlist-meta {
  font-size: 11px;
  color: var(--color-fg-muted);
  margin-top: 0.125rem;
}

/* Add song bar */
.add-song-bar {
  display: flex;
  gap: 0.5rem;
}

.add-song-bar > :first-child {
  flex: 1;
}

/* Songs list */
.songs-list {
  display: flex;
  flex-direction: column;
}

.song-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--color-border);
  transition: background 0.1s ease-out;
}

.song-item:last-child {
  border-bottom: none;
}

.song-item:hover {
  background: var(--color-bg-inset);
}

.song-item.dragging {
  opacity: 0.5;
}

.song-drag-handle {
  color: var(--color-fg-subtle);
  cursor: grab;
}

.song-drag-handle:active {
  cursor: grabbing;
}

.song-index {
  width: 32px;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  color: var(--color-fg-muted);
  text-align: right;
}

.song-icon {
  flex-shrink: 0;
}

.song-info {
  flex: 1;
  min-width: 0;
}

.song-title {
  font-size: 13px;
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.song-link {
  font-size: 11px;
  color: var(--color-fg-muted);
  text-decoration: none;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: block;
}

.song-link:hover {
  color: var(--color-accent);
  text-decoration: underline;
}

.song-actions {
  display: flex;
  gap: 0.125rem;
  opacity: 0;
  transition: opacity 0.1s ease-out;
}

.song-item:hover .song-actions {
  opacity: 1;
}

/* Pagination */
.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
}

.pagination-info {
  font-size: 13px;
  color: var(--color-fg-muted);
}

/* Loading & Empty */
.loading-state {
  display: flex;
  justify-content: center;
  padding: 3rem;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  padding: 3rem;
  text-align: center;
  color: var(--color-fg-muted);
}

/* Modal */
.modal-description {
  font-size: 14px;
  color: var(--color-fg-muted);
  margin: 0;
}

.modal-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.form-label {
  font-size: 13px;
  font-weight: 500;
}

.form-hint {
  font-size: 11px;
  color: var(--color-fg-muted);
  margin: 0;
}
</style>
