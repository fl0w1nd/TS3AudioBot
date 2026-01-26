<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import Icon from './Icon.vue'

interface Props {
  id: string
  message: string
  type?: 'info' | 'success' | 'warning' | 'error'
  duration?: number
}

const props = withDefaults(defineProps<Props>(), {
  type: 'info',
  duration: 4000,
})

const emit = defineEmits<{
  close: [id: string]
}>()

const isVisible = ref(false)
const isLeaving = ref(false)
let timeout: ReturnType<typeof setTimeout>

const icons = {
  info: 'info',
  success: 'check',
  warning: 'warning',
  error: 'error',
}

function close() {
  isLeaving.value = true
  setTimeout(() => {
    emit('close', props.id)
  }, 200)
}

onMounted(() => {
  requestAnimationFrame(() => {
    isVisible.value = true
  })
  if (props.duration > 0) {
    timeout = setTimeout(close, props.duration)
  }
})

onUnmounted(() => {
  if (timeout) clearTimeout(timeout)
})
</script>

<template>
  <div
    :class="[
      'toast',
      `toast-${type}`,
      { 'toast-visible': isVisible, 'toast-leaving': isLeaving }
    ]"
    @click="close"
  >
    <Icon :name="icons[type]" :size="16" />
    <span class="toast-message">{{ message }}</span>
  </div>
</template>

<style scoped>
.toast {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 0.75rem 1rem;
  border-radius: var(--radius-lg);
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
  box-shadow: var(--shadow-lg);
  transform: translateX(100%);
  opacity: 0;
  transition: transform 0.25s ease-out, opacity 0.25s ease-out;
}

.toast-visible {
  transform: translateX(0);
  opacity: 1;
}

.toast-leaving {
  transform: translateX(100%);
  opacity: 0;
}

.toast-info {
  background: var(--color-bg-elevated);
  color: var(--color-fg);
}

.toast-success {
  background: var(--color-success);
  color: white;
}

.toast-warning {
  background: var(--color-warning);
  color: oklch(0.2 0.01 70);
}

.toast-error {
  background: var(--color-error);
  color: white;
}

.toast-message {
  flex: 1;
}
</style>
