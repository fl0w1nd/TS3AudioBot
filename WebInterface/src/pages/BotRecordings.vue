<script setup lang="ts">
import { ref, computed, inject, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { Card, Button, Icon, Input, Table } from '@/components/ui'
import { cmd, bot, isError, getErrorMessage, api } from '@/api/client'
import { useToast } from '@/composables/useToast'
import type { CmdRecordingInfo, CmdRecordingStatus, CmdRecordingParticipant } from '@/api/types'
import { formatBytes } from '@/api/utils'

const botId = inject<{ value: number }>('botId')!
const toast = useToast()

const recordings = ref<CmdRecordingInfo[]>([])
const status = ref<CmdRecordingStatus | null>(null)
const loading = ref(true)
const participants = ref<CmdRecordingParticipant[]>([])
const userQuery = ref('')
const selectedUids = ref<string[]>([])
const todayStr = formatLocalDate(new Date())
const filterDate = ref(todayStr)
const playUrl = ref('')
const isPlaying = ref(false)
const currentTime = ref(0)
const duration = ref(0)
const audioRef = ref<HTMLAudioElement | null>(null)
const showCalendar = ref(false)
const calendarMonth = ref(new Date())
const datePickerRef = ref<HTMLElement | null>(null)
const filterMode = ref<'today' | 'range' | 'month' | 'year' | 'date' | 'all'>('today')
const rangeDays = ref(7)
const filterMonth = ref(todayStr.slice(0, 7))
const filterYear = ref(todayStr.slice(0, 4))
const showUserDropdown = ref(false)
const userPickerRef = ref<HTMLElement | null>(null)
const pageSize = 10
const currentPage = ref(1)
const totalPages = computed(() => Math.max(1, Math.ceil(displayRecordings.value.length / pageSize)))

// Filtered recordings based on date
const filteredRecordings = computed(() => {
  const all = recordings.value
  if (filterMode.value === 'all') return all

  if (filterMode.value === 'today') {
    const today = todayStr
    return all.filter(r => getLocalDateFromValue(r.Start) === today)
  }

  if (filterMode.value === 'date') {
    if (!filterDate.value) return all
    return all.filter(r => getLocalDateFromValue(r.Start) === filterDate.value)
  }

  if (filterMode.value === 'range') {
    const now = new Date()
    const start = new Date(now.getFullYear(), now.getMonth(), now.getDate())
    start.setDate(start.getDate() - (rangeDays.value - 1))
    return all.filter(r => {
      const ts = parseDate(r.Start)?.getTime()
      return ts !== undefined && !Number.isNaN(ts) && ts >= start.getTime()
    })
  }

  if (filterMode.value === 'month') {
    return all.filter(r => getLocalMonthFromValue(r.Start) === filterMonth.value)
  }

  if (filterMode.value === 'year') {
    return all.filter(r => getLocalYearFromValue(r.Start) === filterYear.value)
  }

  return all
})

const displayRecordings = computed(() => {
  return filteredRecordings.value
})

const pagedRecordings = computed(() => {
  const start = (currentPage.value - 1) * pageSize
  return displayRecordings.value.slice(start, start + pageSize)
})

const recordingDates = computed(() => {
  const dates = new Set<string>()
  for (const rec of recordings.value) {
    const date = getLocalDateFromValue(rec.Start)
    if (date) dates.add(date)
  }
  return Array.from(dates).sort()
})

const recordingMonths = computed(() => {
  const months = new Set<string>()
  for (const rec of recordings.value) {
    const month = getLocalMonthFromValue(rec.Start)
    if (month) months.add(month)
  }
  return Array.from(months).sort()
})

const recordingYears = computed(() => {
  const years = new Set<string>()
  for (const rec of recordings.value) {
    const year = getLocalYearFromValue(rec.Start)
    if (year) years.add(year)
  }
  return Array.from(years).sort()
})

const filteredUsers = computed(() => {
  const q = userQuery.value.trim().toLowerCase()
  if (!q) return participants.value
  return participants.value.filter(p => {
    const name = (p.Name || '').toLowerCase()
    const uid = (p.Uid || '').toLowerCase()
    return name.includes(q) || uid.includes(q)
  })
})

const selectedUsers = computed(() => {
  const set = new Set(selectedUids.value)
  return participants.value.filter(p => set.has(p.Uid))
})

const calendarDays = computed(() => {
  const month = new Date(calendarMonth.value)
  const year = month.getFullYear()
  const m = month.getMonth()
  const first = new Date(year, m, 1)
  const last = new Date(year, m + 1, 0)
  const startWeekday = first.getDay()
  const daysInMonth = last.getDate()
  const days: Array<{ date: string; day: number; inMonth: boolean }> = []
  for (let i = 0; i < startWeekday; i++) {
    days.push({ date: '', day: 0, inMonth: false })
  }
  for (let d = 1; d <= daysInMonth; d++) {
    const date = new Date(year, m, d)
    const dateStr = formatLocalDate(date)
    days.push({ date: dateStr, day: d, inMonth: true })
  }
  return days
})

async function loadRecordings() {
  loading.value = true
  
  // Load status
  const statusRes = await bot(cmd<CmdRecordingStatus>('recording', 'status'), botId.value).fetch()
  if (!isError(statusRes)) {
    status.value = statusRes
  }
  
  // Load recordings list
  // Ensure minimum loading time
  const minTime = new Promise(resolve => setTimeout(resolve, 500))

  const listArgs: (string | number)[] = ['recording', 'list']
  const fromArg = filterFromArg()
  const toArg = filterToArg()
  const uidArg = filterUidArg()
  const nameArg = filterNameArg()
  if (fromArg) listArgs.push(fromArg)
  if (toArg) listArgs.push(toArg)
  if (uidArg || nameArg) {
    listArgs.push(uidArg ?? '-')
    if (nameArg) listArgs.push(nameArg)
  }
  const listCommand = cmd<CmdRecordingInfo[]>(...listArgs)
  const [res] = await Promise.all([
    bot(listCommand, botId.value).fetch(),
    minTime
  ])
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    recordings.value = res
    const open = recordings.value.find(r => r.IsOpen)
    if (status.value?.Current && open && open.Id === status.value.Current.Id) {
      status.value.Current.Size = open.Size
      status.value.Current.Duration = open.Duration
      status.value.Current.End = open.End
    }
  }
  
  loading.value = false
}

async function loadParticipants() {
  const listArgs: (string | number)[] = ['recording', 'users']
  const fromArg = filterFromArg()
  const toArg = filterToArg()
  if (fromArg) listArgs.push(fromArg)
  if (toArg) listArgs.push(toArg)

  const res = await bot(cmd<CmdRecordingParticipant[]>(...listArgs), botId.value).fetch()
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    participants.value = res
  }
}

async function applyFilters() {
  showUserDropdown.value = false
  await Promise.all([loadParticipants(), loadRecordings()])
  currentPage.value = 1
}

// Toggle recording enabled state using 'recording enable 0/1'
async function toggleEnabled() {
  const newEnabled = !(status.value?.Enabled ?? false)
  const enabledArg = newEnabled ? 'true' : 'false'
  const res = await bot(cmd<CmdRecordingStatus>('recording', 'enable', enabledArg), botId.value).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    status.value = res
    toast.success(res.Enabled ? 'Recording enabled' : 'Recording disabled')
  }
}

async function deleteRecording(id: string) {
  const res = await bot(cmd<boolean>('recording', 'delete', id), botId.value).fetch()
  
  if (isError(res)) {
    toast.error(getErrorMessage(res))
  } else {
    toast.success('Recording deleted')
    // Remove locally immediately to update UI
    recordings.value = recordings.value.filter(r => r.Id !== id)
    await loadRecordings()
    currentPage.value = Math.min(currentPage.value, totalPages.value)
  }
}

function getDownloadUrl(id: string): string {
  return api.endpoint + bot(cmd('recording', 'get', id), botId.value).toString()
}

function playRecording(rec: CmdRecordingInfo) {
  if (rec.IsOpen) return
  playUrl.value = getDownloadUrl(rec.Id)
  duration.value = parseDurationString(rec.Duration) ?? 0
  currentTime.value = 0
  nextTick(() => {
    const audio = audioRef.value
    if (!audio) return
    audio.load()
    audio.play().catch(() => {})
  })
}

function formatDate(value: string): string {
  const d = parseDate(value)
  if (!d) return value.replace('T', ' ')
  return `${formatLocalDate(d)} ${formatLocalTime(d)}`
}

function parseEndFromId(id: string): string | null {
  const m = id.match(/(\d{4}-\d{2}-\d{2})\/(\d{2}-\d{2}-\d{2})__(\d{2}-\d{2}-\d{2})/)
  if (!m) return null
  const date = m[1]
  const endTime = m[3].replace(/-/g, ':')
  return `${date}T${endTime}`
}

function getEffectiveEnd(rec: CmdRecordingInfo): string | null {
  if (rec.End && rec.End.trim() !== '') return rec.End
  return parseEndFromId(rec.Id)
}

function formatEnd(rec: CmdRecordingInfo): string {
  const effectiveEnd = getEffectiveEnd(rec)
  if (effectiveEnd) return formatDate(effectiveEnd)
  return rec.IsOpen ? 'Recording...' : '-'
}

function formatRecDuration(rec: CmdRecordingInfo): string {
  const normalized = formatDurationValue(rec.Duration)
  if (normalized) return normalized
  const effectiveEnd = getEffectiveEnd(rec)
  if (!effectiveEnd) return '-'
  const start = Date.parse(rec.Start)
  const end = Date.parse(effectiveEnd)
  if (isNaN(start) || isNaN(end) || end <= start) return '-'
  const seconds = Math.floor((end - start) / 1000)
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  const hh = h > 0 ? h.toString() + ':' : ''
  const mm = ('00' + m.toString()).slice(-2)
  const ss = ('00' + s.toString()).slice(-2)
  return hh + mm + ':' + ss
}

function formatSeconds(sec: number): string {
  if (!isFinite(sec) || sec < 0) return '00:00'
  const s = Math.floor(sec)
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const r = s % 60
  const hh = h > 0 ? h.toString() + ':' : ''
  const mm = ('00' + m.toString()).slice(-2)
  const ss = ('00' + r.toString()).slice(-2)
  return hh + mm + ':' + ss
}

function getIsoStartOfDay(dateStr: string): string | undefined {
  const d = parseDate(dateStr)
  if (!d) return undefined
  d.setHours(0, 0, 0, 0)
  return d.toISOString()
}

function getIsoEndOfDay(dateStr: string): string | undefined {
  const d = parseDate(dateStr)
  if (!d) return undefined
  d.setHours(23, 59, 59, 999)
  return d.toISOString()
}

function formatLocalDate(date: Date): string {
  const y = date.getFullYear()
  const m = ('0' + (date.getMonth() + 1)).slice(-2)
  const d = ('0' + date.getDate()).slice(-2)
  return `${y}-${m}-${d}`
}

function formatLocalTime(date: Date): string {
  const h = ('0' + date.getHours()).slice(-2)
  const m = ('0' + date.getMinutes()).slice(-2)
  const s = ('0' + date.getSeconds()).slice(-2)
  return `${h}:${m}:${s}`
}

function parseDate(value: string): Date | null {
  if (!value) return null
  // Handle YYYY-MM-DD explicitly as local time to prevent UTC shift
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    const parts = value.split('-').map(Number)
    return new Date(parts[0], parts[1] - 1, parts[2])
  }
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? null : d
}

function getLocalDateFromValue(value: string): string | null {
  const d = parseDate(value)
  return d ? formatLocalDate(d) : null
}

function getLocalMonthFromValue(value: string): string | null {
  const d = parseDate(value)
  return d ? `${d.getFullYear()}-${('0' + (d.getMonth() + 1)).slice(-2)}` : null
}

function getLocalYearFromValue(value: string): string | null {
  const d = parseDate(value)
  return d ? d.getFullYear().toString() : null
}

function parseDurationString(value: string | number | null): number | null {
  if (value === null || value === undefined) return null
  if (typeof value === 'number') return value
  const raw = value.trim()
  if (!raw) return null
  if (!raw.includes(':')) {
    const secs = Number.parseFloat(raw)
    return Number.isNaN(secs) ? null : secs
  }
  const parts = raw.split(':')
  if (parts.length === 3) {
    const h = parseInt(parts[0], 10)
    const m = parseInt(parts[1], 10)
    const s = parseInt(parts[2].split('.')[0], 10)
    if ([h, m, s].some(n => Number.isNaN(n))) return null
    return h * 3600 + m * 60 + s
  }
  if (parts.length === 2) {
    const m = parseInt(parts[0], 10)
    const s = parseInt(parts[1].split('.')[0], 10)
    if ([m, s].some(n => Number.isNaN(n))) return null
    return m * 60 + s
  }
  return null
}

function formatDurationValue(value: string | number | null): string | null {
  if (value === null || value === undefined) return null
  if (typeof value === 'number') return formatSeconds(value)
  const raw = value.trim()
  if (!raw) return null
  if (raw.includes(':')) {
    const parts = raw.split(':')
    const last = parts[parts.length - 1]
    const cleanLast = last.split('.')[0]
    parts[parts.length - 1] = cleanLast.padStart(2, '0')
    if (parts.length === 2) parts[0] = parts[0].padStart(2, '0')
    return parts.join(':')
  }
  const secs = Number.parseFloat(raw)
  return Number.isNaN(secs) ? null : formatSeconds(secs)
}

function togglePlayback() {
  const audio = audioRef.value
  if (!audio) return
  if (audio.paused) {
    audio.play().catch(() => {})
  } else {
    audio.pause()
  }
}

function onTimeUpdate() {
  const audio = audioRef.value
  if (!audio) return
  currentTime.value = audio.currentTime || 0
  duration.value = audio.duration || 0
}

function onLoadedMeta() {
  const audio = audioRef.value
  if (!audio) return
  if (isFinite(audio.duration) && audio.duration > 0) {
    duration.value = audio.duration
  }
}

function onPlay() {
  isPlaying.value = true
}

function onPause() {
  isPlaying.value = false
}

function seek(event: Event) {
  const audio = audioRef.value
  if (!audio) return
  const target = event.target as HTMLInputElement
  const value = parseFloat(target.value)
  if (!isNaN(value)) audio.currentTime = value
}

function stopPlayback() {
  const audio = audioRef.value
  if (audio) {
    audio.pause()
    audio.currentTime = 0
  }
}

function closePlayer() {
  stopPlayback()
  playUrl.value = ''
  duration.value = 0
  currentTime.value = 0
}

function setFilterDate(date: string) {
  filterDate.value = date
}

function openCalendar() {
  showCalendar.value = true
  filterMode.value = 'date'
  if (filterDate.value) {
    const parts = filterDate.value.split('-').map(p => parseInt(p, 10))
    if (parts.length === 3 && !parts.some(Number.isNaN)) {
      calendarMonth.value = new Date(parts[0], parts[1] - 1, 1)
      return
    }
  }
  calendarMonth.value = new Date()
}

function prevMonth() {
  const d = new Date(calendarMonth.value)
  d.setMonth(d.getMonth() - 1)
  calendarMonth.value = d
}

function nextMonth() {
  const d = new Date(calendarMonth.value)
  d.setMonth(d.getMonth() + 1)
  calendarMonth.value = d
}

function selectDate(date: string) {
  if (!date) return
  setFilterDate(date)
  filterMode.value = 'date'
  showCalendar.value = false
}

function setToday() {
  filterMode.value = 'today'
  filterDate.value = todayStr
}

function setRange(days: number) {
  rangeDays.value = days
  filterMode.value = 'range'
}

function setMonth(month: string) {
  filterMonth.value = month
  filterMode.value = 'month'
}

function setYear(year: string) {
  filterYear.value = year
  filterMode.value = 'year'
}

function filterFromArg(): string | undefined {
  if (filterMode.value === 'range') {
    const now = new Date()
    const start = new Date(now.getFullYear(), now.getMonth(), now.getDate())
    start.setDate(start.getDate() - (rangeDays.value - 1))
    start.setHours(0, 0, 0, 0)
    return start.toISOString()
  }
  
  if (filterMode.value === 'date' && filterDate.value) {
    return getIsoStartOfDay(filterDate.value)
  }
  
  if (filterMode.value === 'today') {
    return getIsoStartOfDay(todayStr)
  }

  if (filterMode.value === 'month' && filterMonth.value) {
    return getIsoStartOfDay(`${filterMonth.value}-01`)
  }

  if (filterMode.value === 'year' && filterYear.value) {
    return getIsoStartOfDay(`${filterYear.value}-01-01`)
  }
  return undefined
}

function filterToArg(): string | undefined {
  if (filterMode.value === 'range') {
    return getIsoEndOfDay(todayStr)
  }
  
  if (filterMode.value === 'date' && filterDate.value) {
    return getIsoEndOfDay(filterDate.value)
  }

  if (filterMode.value === 'today') {
    return getIsoEndOfDay(todayStr)
  }

  if (filterMode.value === 'month' && filterMonth.value) {
    const [y, m] = filterMonth.value.split('-').map(Number)
    const lastDay = new Date(y, m, 0).getDate()
    return getIsoEndOfDay(`${filterMonth.value}-${lastDay}`)
  }

  if (filterMode.value === 'year' && filterYear.value) {
    return getIsoEndOfDay(`${filterYear.value}-12-31`)
  }
  return undefined
}

function filterUidArg(): string | undefined {
  const tokens = new Set(selectedUids.value.map(v => v.trim()).filter(Boolean))
  const q = userQuery.value.trim()
  if (isLikelyUid(q)) tokens.add(q)
  return tokens.size ? Array.from(tokens).join(',') : undefined
}

function filterNameArg(): string | undefined {
  const q = userQuery.value.trim()
  if (!q || isLikelyUid(q)) return undefined
  return q
}

function isLikelyUid(value: string): boolean {
  return /^[A-Za-z0-9+/=]{20,}$/.test(value)
}

function resetUserFilters() {
  userQuery.value = ''
  selectedUids.value = []
  applyFilters()
}

function getInitials(name: string): string {
  const trimmed = name.trim()
  if (!trimmed) return '?'
  const parts = trimmed.split(/\s+/)
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[1][0]).toUpperCase()
}

function onDocumentClick(e: MouseEvent) {
  const el = datePickerRef.value
  const userEl = userPickerRef.value
  const target = e.target as Node
  if (el && !el.contains(target)) showCalendar.value = false
  if (userEl && !userEl.contains(target)) showUserDropdown.value = false
}

function toggleUser(uid: string) {
  const idx = selectedUids.value.indexOf(uid)
  if (idx >= 0) {
    selectedUids.value.splice(idx, 1)
  } else {
    selectedUids.value.push(uid)
  }
}

function removeUser(uid: string) {
  const idx = selectedUids.value.indexOf(uid)
  if (idx >= 0) selectedUids.value.splice(idx, 1)
}

function isUserSelected(uid: string) {
  return selectedUids.value.includes(uid)
}

function openUserDropdown() {
  showUserDropdown.value = true
}

onMounted(() => {
  document.addEventListener('click', onDocumentClick)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', onDocumentClick)
  closePlayer()
})

const columns = [
  { key: 'Start', label: 'Start' },
  { key: 'End', label: 'End' },
  { key: 'Duration', label: 'Duration', width: '100px' },
  { key: 'Size', label: 'Size', width: '100px' },
  { key: 'Participants', label: 'Users', width: '120px' },
  { key: 'actions', label: '', width: '120px', align: 'right' as const },
]

onMounted(() => {
  applyFilters()
})
</script>

<template>
  <div class="recordings-page">
    <div class="page-header">
      <div>
        <h2 class="page-title">Recordings</h2>
        <p class="page-subtitle">
          <template v-if="status">
            Recording is {{ status.Enabled ? 'enabled' : 'disabled' }}
          </template>
        </p>
      </div>
      <div class="page-actions">
        <Button variant="ghost" color="neutral" size="sm" @click="applyFilters">
          <Icon name="refresh" :size="14" :spin="loading" />
          Refresh
        </Button>
        <!-- Toggle enabled/disabled -->
        <Button 
          :variant="status?.Enabled ? 'soft' : 'outline'"
          :color="status?.Enabled ? 'success' : 'neutral'"
          size="sm"
          @click="toggleEnabled"
        >
          <Icon :name="status?.Enabled ? 'check' : 'x'" :size="14" />
          {{ status?.Enabled ? 'Enabled' : 'Disabled' }}
        </Button>
      </div>
    </div>

    <!-- Filter -->
    <div class="filter-bar">
      <div ref="datePickerRef" class="date-picker">
        <Input
          v-model="filterDate"
          type="text"
          placeholder="Filter by date"
          size="sm"
          readonly
          @click="openCalendar"
        >
          <template #prefix>
            <Icon name="search" :size="14" />
          </template>
        </Input>
        <div v-if="showCalendar" class="calendar">
          <div class="calendar-header">
            <button class="cal-btn" @click="prevMonth">
              <Icon name="chevron-left" :size="14" />
            </button>
            <span class="cal-title">
              {{ calendarMonth.getFullYear() }}-{{ ('0' + (calendarMonth.getMonth() + 1)).slice(-2) }}
            </span>
            <button class="cal-btn" @click="nextMonth">
              <Icon name="chevron-right" :size="14" />
            </button>
          </div>
          <div class="calendar-quick">
            <button
              class="cal-quick-btn"
              :class="{ active: filterMode === 'today' }"
              @click="setToday"
            >
              Today
            </button>
            <button
              class="cal-quick-btn"
              :class="{ active: filterMode === 'range' && rangeDays === 7 }"
              @click="setRange(7)"
            >
              Last 7 days
            </button>
            <button
              class="cal-quick-btn"
              :class="{ active: filterMode === 'range' && rangeDays === 30 }"
              @click="setRange(30)"
            >
              Last 30 days
            </button>
          </div>
          <div class="calendar-selects">
            <select class="select" :value="filterMonth" @change="setMonth(($event.target as HTMLSelectElement).value)">
              <option value="" disabled>Month</option>
              <option v-for="m in recordingMonths" :key="m" :value="m">
                {{ m }}
              </option>
            </select>
            <select class="select" :value="filterYear" @change="setYear(($event.target as HTMLSelectElement).value)">
              <option value="" disabled>Year</option>
              <option v-for="y in recordingYears" :key="y" :value="y">
                {{ y }}
              </option>
            </select>
            <button class="cal-quick-btn reset" @click="setToday">
              Reset
            </button>
          </div>
          <div class="calendar-grid">
            <span class="cal-week">Su</span>
            <span class="cal-week">Mo</span>
            <span class="cal-week">Tu</span>
            <span class="cal-week">We</span>
            <span class="cal-week">Th</span>
            <span class="cal-week">Fr</span>
            <span class="cal-week">Sa</span>
            <button
              v-for="(day, idx) in calendarDays"
              :key="idx"
              class="cal-day"
              :class="{
                empty: !day.inMonth,
                active: day.date && filterDate === day.date,
                marked: day.date && recordingDates.includes(day.date),
              }"
              :disabled="!day.inMonth"
              @click="selectDate(day.date)"
            >
              {{ day.day || '' }}
            </button>
          </div>
        </div>
      </div>
      <div class="user-filter">
        <div ref="userPickerRef" class="user-picker" @click="openUserDropdown">
          <span v-for="u in selectedUsers" :key="u.Uid" class="user-chip">
            {{ u.Name || u.Uid }}
            <button class="chip-remove" @click.stop="removeUser(u.Uid)">×</button>
          </span>
          <input
            v-model="userQuery"
            class="user-input"
            placeholder="Filter user (type to search, click to select)"
            @focus="openUserDropdown"
          />
        </div>
        <div v-if="showUserDropdown" class="user-dropdown">
          <button
            v-for="u in filteredUsers"
            :key="u.Uid"
            class="user-option"
            :class="{ selected: isUserSelected(u.Uid) }"
            @click.stop="toggleUser(u.Uid)"
          >
            <span class="user-option-name">{{ u.Name || u.Uid }}</span>
            <span class="user-option-uid">{{ u.Uid }}</span>
          </button>
        </div>
        <Button variant="soft" color="accent" size="sm" @click="applyFilters">
          Apply filters
        </Button>
        <Button variant="ghost" color="neutral" size="sm" @click="resetUserFilters">
          Clear user filters
        </Button>
      </div>
    </div>

    <!-- Current Recording -->
    <Card v-if="status?.Active && status?.Current" padding="md" class="current-recording">
      <div class="recording-indicator">
        <span class="recording-dot animate-pulse" />
        <span>Recording in progress</span>
      </div>
      <div class="recording-info">
        <div class="recording-stat">
          <span class="stat-label">Started</span>
          <span class="stat-value">{{ formatDate(status.Current.Start) }}</span>
        </div>
        <div class="recording-stat">
          <span class="stat-label">Size</span>
          <span class="stat-value">{{ formatBytes(status.Current.Size) }}</span>
        </div>
      </div>
    </Card>

    <!-- Recordings List -->
    <Card padding="none">
      <Table
        :data="pagedRecordings"
        :columns="columns"
        :loading="loading"
        empty-text="No recordings available"
      >
        <template #Start="{ row }">
          {{ formatDate(row.Start) }}
        </template>
        
        <template #End="{ row }">
          {{ formatEnd(row) }}
        </template>
        
        <template #Duration="{ row }">
          {{ formatRecDuration(row) }}
        </template>
        
        <template #Size="{ row }">
          {{ formatBytes(row.Size) }}
        </template>

        <template #Participants="{ row }">
          <div class="participants">
            <span
              v-for="p in (row.Participants || [])"
              :key="p.Uid"
              class="participant"
              :title="p.Name"
            >
              {{ getInitials(p.Name) }}
            </span>
          </div>
        </template>
        
        <template #actions="{ row }">
          <div class="row-actions">
            <button 
              class="action-btn"
              title="Play"
              @click="playRecording(row)"
            >
              <Icon name="play" :size="14" />
            </button>
            <a 
              :href="getDownloadUrl(row.Id)" 
              target="_blank"
              class="action-btn"
              title="Download"
            >
              <Icon name="download" :size="14" />
            </a>
            <button 
              class="action-btn action-btn-danger"
              title="Delete"
              :disabled="row.IsOpen"
              @click="deleteRecording(row.Id)"
            >
              <Icon name="trash" :size="14" />
            </button>
          </div>
        </template>
      </Table>
    </Card>

    <div class="pagination">
      <Button variant="ghost" color="neutral" size="sm" :disabled="currentPage <= 1" @click="currentPage = Math.max(1, currentPage - 1)">
        Prev
      </Button>
      <span class="pagination-info">Page {{ currentPage }} / {{ totalPages }}</span>
      <Button variant="ghost" color="neutral" size="sm" :disabled="currentPage >= totalPages" @click="currentPage = Math.min(totalPages, currentPage + 1)">
        Next
      </Button>
    </div>

    <!-- Audio Player -->
    <Card v-if="playUrl" padding="md" class="audio-player">
      <div class="player-header">
        <span>
          Playback
        </span>
        <Button variant="ghost" color="neutral" size="sm" icon-only @click="closePlayer">
          <Icon name="x" :size="14" />
        </Button>
      </div>
      <div class="player-controls">
        <button class="play-btn" @click="togglePlayback">
          <Icon :name="isPlaying ? 'pause' : 'play'" :size="16" />
        </button>
        <div class="time">
          <span>{{ formatSeconds(currentTime) }}</span>
          <span class="sep">/</span>
          <span>{{ formatSeconds(duration) }}</span>
        </div>
      </div>
      <input
        class="progress"
        type="range"
        min="0"
        :max="duration || 0"
        :value="currentTime"
        :disabled="!duration"
        @input="seek"
      />
      <audio
        ref="audioRef"
        :src="playUrl"
        preload="none"
        class="audio-element"
        @timeupdate="onTimeUpdate"
        @loadedmetadata="onLoadedMeta"
        @play="onPlay"
        @pause="onPause"
      ></audio>
    </Card>
  </div>
</template>

<style scoped>
.recordings-page {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
}

.page-title {
  font-size: 1rem;
  font-weight: 600;
  margin: 0;
}

.page-subtitle {
  font-size: 13px;
  color: var(--color-fg-muted);
  margin: 0.25rem 0 0;
}

.page-actions {
  display: flex;
  gap: 0.5rem;
}

/* Filter */
.filter-bar {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  flex-wrap: wrap;
}

.filter-bar > :first-child {
  max-width: 220px;
}

.pagination {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  margin-top: -0.5rem;
}

.pagination-info {
  font-size: 12px;
  color: var(--color-fg-muted);
}

.date-picker {
  position: relative;
  width: 100%;
}

.select {
  appearance: none;
  border: 1px solid var(--color-border);
  background: var(--color-bg);
  color: var(--color-fg);
  font-size: 12px;
  padding: 6px 8px;
  border-radius: var(--radius-sm);
}

.calendar-quick {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  padding: 0 4px 6px;
}

.calendar-selects {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  padding: 0 4px 6px;
}

.cal-quick-btn {
  border: 1px solid var(--color-border);
  background: var(--color-bg);
  color: var(--color-fg);
  font-size: 11px;
  padding: 4px 6px;
  border-radius: 999px;
  cursor: pointer;
  transition: all 0.1s ease-out;
}

.cal-quick-btn:hover {
  border-color: var(--color-success);
  color: var(--color-success);
}

.user-filter {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
  position: relative;
}

.user-picker {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  flex-wrap: wrap;
  min-width: 260px;
  padding: 4px 6px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-bg);
  cursor: text;
}

.user-input {
  border: none;
  outline: none;
  background: transparent;
  font-size: 12px;
  color: var(--color-fg);
  min-width: 140px;
}

.user-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 6px;
  border-radius: 999px;
  background: var(--color-bg-muted);
  color: var(--color-fg);
  font-size: 11px;
}

.chip-remove {
  border: none;
  background: transparent;
  color: var(--color-fg-muted);
  cursor: pointer;
}

.user-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  margin-top: 6px;
  min-width: 260px;
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.08);
  padding: 6px;
  max-height: 220px;
  overflow: auto;
  z-index: 12;
}

.user-option {
  width: 100%;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  border: none;
  background: transparent;
  color: var(--color-fg);
  font-size: 12px;
  text-align: left;
  border-radius: 6px;
  cursor: pointer;
}

.user-option.selected {
  background: var(--color-success-muted);
  color: var(--color-success);
}

.user-option-uid {
  font-size: 11px;
  color: var(--color-fg-muted);
}


.cal-quick-btn.active,
.cal-quick-btn.reset {
  background: var(--color-success-muted);
  border-color: var(--color-success);
  color: var(--color-success);
}

.calendar {
  position: absolute;
  top: 36px;
  left: 0;
  z-index: 10;
  width: 240px;
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 8px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.08);
}

.calendar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 4px 8px;
}

.cal-title {
  font-size: 12px;
  font-weight: 600;
  color: var(--color-fg);
}

.cal-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border-radius: 6px;
  border: none;
  background: transparent;
  color: var(--color-fg-muted);
  cursor: pointer;
}

.cal-btn:hover {
  background: var(--color-bg-inset);
  color: var(--color-fg);
}

.calendar-grid {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  gap: 4px;
}

.participants {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

.participant {
  width: 22px;
  height: 22px;
  border-radius: 999px;
  background: var(--color-bg-inset);
  color: var(--color-fg);
  font-size: 10px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-weight: 600;
}

.cal-week {
  text-align: center;
  font-size: 10px;
  color: var(--color-fg-muted);
  padding: 2px 0;
}

.cal-day {
  border: 1px solid transparent;
  background: transparent;
  border-radius: 6px;
  height: 26px;
  font-size: 12px;
  color: var(--color-fg);
  cursor: pointer;
}

.cal-day:hover {
  border-color: var(--color-success);
  color: var(--color-success);
}

.cal-day.empty {
  cursor: default;
  color: transparent;
}

.cal-day.marked {
  background: var(--color-success-muted);
  border-color: var(--color-success);
  color: var(--color-success);
}

.cal-day.active {
  background: var(--color-success);
  border-color: var(--color-success);
  color: #fff;
}

/* Current Recording */
.current-recording {
  background: var(--color-success-muted);
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.recording-indicator {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 13px;
  font-weight: 500;
  color: var(--color-success);
}

.recording-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--color-success);
}

.recording-info {
  display: flex;
  gap: 1.5rem;
}

.recording-stat {
  display: flex;
  flex-direction: column;
  gap: 0.125rem;
  text-align: right;
}

.stat-label {
  font-size: 11px;
  color: var(--color-fg-muted);
}

.stat-value {
  font-size: 13px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
}

/* Table */
.row-actions {
  display: flex;
  gap: 0.25rem;
  justify-content: flex-end;
}

.action-btn {
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
  text-decoration: none;
  transition: all 0.1s ease-out;
}

.action-btn:hover {
  background: var(--color-bg-inset);
  color: var(--color-fg);
}

.action-btn-danger:hover {
  background: var(--color-error-muted);
  color: var(--color-error);
}

.action-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

/* Audio Player */
.audio-player {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.player-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 13px;
  font-weight: 500;
}

.live-pill {
  margin-left: 6px;
  padding: 2px 6px;
  border-radius: 999px;
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.4px;
  color: var(--color-success);
  background: var(--color-success-muted);
}

.player-controls {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.play-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 36px;
  height: 36px;
  border: none;
  border-radius: 999px;
  background: var(--color-bg-inset);
  color: var(--color-fg);
  cursor: pointer;
  transition: transform 0.1s ease-out, background 0.1s ease-out;
}

.play-btn:hover {
  transform: translateY(-1px);
  background: var(--color-bg);
}

.time {
  font-size: 12px;
  color: var(--color-fg-muted);
  font-variant-numeric: tabular-nums;
}

.sep {
  margin: 0 6px;
  opacity: 0.6;
}

.progress {
  width: 100%;
  accent-color: var(--color-success);
}

.audio-element {
  position: absolute;
  width: 1px;
  height: 1px;
  opacity: 0;
  pointer-events: none;
}
</style>
