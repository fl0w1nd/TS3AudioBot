<script setup lang="ts">
import { useTheme } from '@/composables/useTheme'
import Sidebar from '@/components/layout/Sidebar.vue'
import ToastContainer from '@/components/layout/ToastContainer.vue'

// Initialize theme
useTheme()
</script>

<template>
  <div class="app">
    <Sidebar />
    <main class="main-content">
      <router-view v-slot="{ Component }">
        <transition name="page" mode="out-in">
          <component :is="Component" />
        </transition>
      </router-view>
    </main>
    <ToastContainer />
  </div>
</template>

<style scoped>
.app {
  min-height: 100vh;
  display: flex;
  flex-direction: row;
}

.main-content {
  flex: 1;
  min-width: 0;
  padding: 2rem;
  max-width: 1600px;
  margin: 0 auto;
}

/* Page transition */
.page-enter-active,
.page-leave-active {
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

.page-enter-from {
  opacity: 0;
  transform: translateY(10px);
}

.page-leave-to {
  opacity: 0;
  transform: translateY(-10px);
}
</style>
