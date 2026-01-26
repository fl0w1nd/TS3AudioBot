<script setup lang="ts">
import { useToast } from '@/composables/useToast'
import Toast from '@/components/ui/Toast.vue'

const { toasts, remove } = useToast()
</script>

<template>
  <Teleport to="body">
    <div class="toast-container">
      <TransitionGroup name="toast">
        <Toast
          v-for="toast in toasts"
          :key="toast.id"
          :id="toast.id"
          :message="toast.message"
          :type="toast.type"
          :duration="toast.duration"
          @close="remove"
        />
      </TransitionGroup>
    </div>
  </Teleport>
</template>

<style scoped>
.toast-container {
  position: fixed;
  top: 1rem;
  right: 1rem;
  z-index: 9999;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  pointer-events: none;
}

.toast-container > * {
  pointer-events: auto;
}

.toast-enter-active,
.toast-leave-active {
  transition: all 0.25s ease-out;
}

.toast-enter-from {
  opacity: 0;
  transform: translateX(100%);
}

.toast-leave-to {
  opacity: 0;
  transform: translateX(100%);
}
</style>
