import { ref, readonly } from 'vue'

export interface ToastItem {
  id: string
  message: string
  type: 'info' | 'success' | 'warning' | 'error'
  duration: number
}

const toasts = ref<ToastItem[]>([])

let idCounter = 0

function generateId(): string {
  return `toast-${++idCounter}-${Date.now()}`
}

function show(message: string, options: Partial<Omit<ToastItem, 'id' | 'message'>> = {}) {
  const toast: ToastItem = {
    id: generateId(),
    message,
    type: options.type ?? 'info',
    duration: options.duration ?? 4000,
  }
  
  toasts.value.push(toast)
  return toast.id
}

function remove(id: string) {
  const index = toasts.value.findIndex(t => t.id === id)
  if (index !== -1) {
    toasts.value.splice(index, 1)
  }
}

function success(message: string, duration?: number) {
  return show(message, { type: 'success', duration })
}

function error(message: string, duration?: number) {
  return show(message, { type: 'error', duration })
}

function warning(message: string, duration?: number) {
  return show(message, { type: 'warning', duration })
}

function info(message: string, duration?: number) {
  return show(message, { type: 'info', duration })
}

export function useToast() {
  return {
    toasts: readonly(toasts),
    show,
    remove,
    success,
    error,
    warning,
    info,
  }
}
