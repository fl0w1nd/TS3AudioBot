<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import Icon from './Icon.vue'

interface Props {
  open?: boolean
  title?: string
  size?: 'sm' | 'md' | 'lg'
  closable?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  open: false,
  size: 'md',
  closable: true,
})

const emit = defineEmits<{
  'update:open': [value: boolean]
  'close': []
}>()

const isVisible = ref(false)
const isAnimating = ref(false)

function close() {
  if (!props.closable) return
  emit('update:open', false)
  emit('close')
}

function handleBackdropClick(e: MouseEvent) {
  if (e.target === e.currentTarget) {
    close()
  }
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && props.closable) {
    close()
  }
}

watch(() => props.open, (value) => {
  if (value) {
    isAnimating.value = true
    requestAnimationFrame(() => {
      isVisible.value = true
    })
    document.body.style.overflow = 'hidden'
  } else {
    isVisible.value = false
    setTimeout(() => {
      isAnimating.value = false
    }, 200)
    document.body.style.overflow = ''
  }
}, { immediate: true })

onMounted(() => {
  document.addEventListener('keydown', handleKeydown)
})

onUnmounted(() => {
  document.removeEventListener('keydown', handleKeydown)
  document.body.style.overflow = ''
})
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open || isAnimating"
      :class="['modal-backdrop', { 'modal-visible': isVisible }]"
      @click="handleBackdropClick"
    >
      <div
        :class="['modal-container', `modal-${size}`, { 'modal-visible': isVisible }]"
        role="dialog"
        aria-modal="true"
      >
        <div v-if="title || closable" class="modal-header">
          <h2 v-if="title" class="modal-title">{{ title }}</h2>
          <button
            v-if="closable"
            class="modal-close"
            @click="close"
            aria-label="Close"
          >
            <Icon name="x" :size="16" />
          </button>
        </div>
        
        <div class="modal-body">
          <slot />
        </div>
        
        <div v-if="$slots.footer" class="modal-footer">
          <slot name="footer" />
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
  background: oklch(0 0 0 / 0.5);
  opacity: 0;
  transition: opacity 0.2s ease-out;
}

.modal-backdrop.modal-visible {
  opacity: 1;
}

.modal-container {
  position: relative;
  max-height: calc(100vh - 2rem);
  background: var(--color-bg-elevated);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-hero);
  display: flex;
  flex-direction: column;
  transform: scale(0.95) translateY(8px);
  opacity: 0;
  transition: transform 0.2s ease-out, opacity 0.2s ease-out;
}

.modal-container.modal-visible {
  transform: scale(1) translateY(0);
  opacity: 1;
}

/* Sizes */
.modal-sm { width: 100%; max-width: 360px; }
.modal-md { width: 100%; max-width: 480px; }
.modal-lg { width: 100%; max-width: 640px; }

.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--color-border);
}

.modal-title {
  font-size: 1rem;
  font-weight: 600;
  margin: 0;
}

.modal-close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border: none;
  border-radius: var(--radius-md);
  background: transparent;
  color: var(--color-fg-muted);
  cursor: pointer;
  transition: all 0.1s ease-out;
}

.modal-close:hover {
  background: var(--color-bg-inset);
  color: var(--color-fg);
}

.modal-body {
  flex: 1;
  padding: 1.25rem;
  overflow-y: auto;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  padding: 1rem 1.25rem;
  border-top: 1px solid var(--color-border);
}
</style>
