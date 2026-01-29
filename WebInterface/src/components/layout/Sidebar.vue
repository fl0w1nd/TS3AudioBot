<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useTheme } from '@/composables/useTheme'
import { Icon, Button, Modal, Input } from '@/components/ui'
import { api } from '@/api/client'

const { isDark, toggleTheme } = useTheme()

// Mobile menu state
const isMobileMenuOpen = ref(false)
const isMobile = ref(false)

function checkMobile() {
  isMobile.value = window.innerWidth < 768
  if (!isMobile.value) {
    isMobileMenuOpen.value = false
  }
}

function toggleMobileMenu() {
  isMobileMenuOpen.value = !isMobileMenuOpen.value
}

function closeMobileMenu() {
  isMobileMenuOpen.value = false
}

onMounted(() => {
  checkMobile()
  window.addEventListener('resize', checkMobile)
})

onUnmounted(() => {
  window.removeEventListener('resize', checkMobile)
})

// Site settings modal
const showSiteSettings = ref(false)
const apiEndpoint = ref(api.endpoint)

function saveSiteSettings() {
  api.endpoint = apiEndpoint.value
  localStorage.setItem('api_endpoint', apiEndpoint.value)
  showSiteSettings.value = false
}

// Load saved endpoint
const savedEndpoint = localStorage.getItem('api_endpoint')
if (savedEndpoint) {
  api.endpoint = savedEndpoint
  apiEndpoint.value = savedEndpoint
}

const navItems = [
  { label: 'Home', path: '/', icon: 'home' },
  { label: 'Bots', path: '/bots', icon: 'bot' },
  { label: 'Overview', path: '/overview', icon: 'grid' },
]
</script>

<template>
  <!-- Mobile Header -->
  <header v-if="isMobile" class="mobile-header">
    <router-link to="/" class="mobile-logo">
      <div class="logo-icon">
        <svg viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
          <circle cx="16" cy="16" r="16" fill="url(#logo-gradient-mobile)" />
          <rect x="9" y="13" width="2.5" height="6" rx="1.25" fill="white" />
          <rect x="14.75" y="7" width="2.5" height="18" rx="1.25" fill="white" />
          <rect x="20.5" y="11" width="2.5" height="10" rx="1.25" fill="white" />
          <defs>
            <linearGradient id="logo-gradient-mobile" x1="0" y1="0" x2="32" y2="32">
              <stop stop-color="#c084fc" />
              <stop offset="1" stop-color="#818cf8" />
            </linearGradient>
          </defs>
        </svg>
      </div>
      <span class="logo-text">TS3AudioBot</span>
    </router-link>
    <Button variant="ghost" color="neutral" size="sm" icon-only @click="toggleMobileMenu">
      <Icon :name="isMobileMenuOpen ? 'x' : 'menu'" :size="20" />
    </Button>
  </header>

  <!-- Mobile Menu Overlay -->
  <div v-if="isMobile && isMobileMenuOpen" class="mobile-overlay" @click="closeMobileMenu" />

  <!-- Sidebar / Mobile Drawer -->
  <aside :class="['sidebar', { 'sidebar-mobile': isMobile, 'sidebar-mobile-open': isMobileMenuOpen }]">
    <div class="sidebar-top">
      <!-- Logo (desktop only) -->
      <router-link to="/" class="sidebar-logo">
        <div class="logo-icon">
          <svg viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
            <circle cx="16" cy="16" r="16" fill="url(#logo-gradient)" />
            <rect x="9" y="13" width="2.5" height="6" rx="1.25" fill="white" />
            <rect x="14.75" y="7" width="2.5" height="18" rx="1.25" fill="white" />
            <rect x="20.5" y="11" width="2.5" height="10" rx="1.25" fill="white" />
            <defs>
              <linearGradient id="logo-gradient" x1="0" y1="0" x2="32" y2="32">
                <stop stop-color="#c084fc" />
                <stop offset="1" stop-color="#818cf8" />
              </linearGradient>
            </defs>
          </svg>
        </div>
        <span class="logo-text">TS3AudioBot</span>
      </router-link>

      <!-- Navigation -->
      <nav class="sidebar-nav">
        <router-link
          v-for="item in navItems"
          :key="item.path"
          :to="item.path"
          class="nav-link"
          :class="{ 'nav-link-active': $route.path === item.path || ($route.path.startsWith('/bot') && item.path === '/bots') }"
          @click="closeMobileMenu"
        >
          <Icon :name="item.icon" :size="18" />
          <span>{{ item.label }}</span>
        </router-link>
      </nav>
    </div>

    <div class="sidebar-bottom">
      <!-- OpenAPI Link -->
      <a href="/openapi" target="_blank" class="nav-link nav-link-external">
        <Icon name="external" :size="18" />
        <span>API Docs</span>
      </a>

      <div class="sidebar-actions">
        <Button
          variant="ghost"
          color="neutral"
          size="sm"
          class="action-btn"
          @click="showSiteSettings = true"
        >
          <Icon name="settings" :size="18" />
          <span>Settings</span>
        </Button>
        <Button
          variant="ghost"
          color="neutral"
          size="sm"
          class="action-btn"
          @click="toggleTheme"
        >
          <Icon :name="isDark ? 'sun' : 'moon'" :size="18" />
          <span>{{ isDark ? 'Light' : 'Dark' }}</span>
        </Button>
      </div>
    </div>

    <!-- Site Settings Modal -->
    <Modal v-model:open="showSiteSettings" title="Site Settings" size="sm">
      <div class="settings-form">
        <div class="setting-item">
          <label class="setting-label">API Endpoint</label>
          <p class="setting-description">The API endpoint for the TS3AudioBot server.</p>
          <Input v-model="apiEndpoint" placeholder="/api" />
        </div>
      </div>
      <template #footer>
        <Button variant="ghost" color="neutral" @click="showSiteSettings = false">Cancel</Button>
        <Button @click="saveSiteSettings">Save</Button>
      </template>
    </Modal>
  </aside>
</template>

<style scoped>
/* Mobile Header */
.mobile-header {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  height: 56px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 1rem;
  background: var(--color-bg-elevated);
  border-bottom: 1px solid var(--color-border);
  z-index: 120;
}

.mobile-logo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  text-decoration: none;
  color: var(--color-fg);
  font-weight: 700;
  font-size: 15px;
}

.mobile-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  z-index: 115;
}

/* Sidebar */
.sidebar {
  width: var(--sidebar-width);
  height: 100vh;
  position: sticky;
  top: 0;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  padding: 1.5rem 1rem;
  background: var(--color-bg-elevated);
  border-right: 1px solid var(--color-border);
  flex-shrink: 0;
  z-index: 110;
  transition: transform 0.25s ease-out;
}

.sidebar-mobile {
  position: fixed;
  top: 0;
  left: 0;
  width: 280px;
  transform: translateX(-100%);
  z-index: 120;
}

.sidebar-mobile-open {
  transform: translateX(0);
}

.sidebar-top {
  display: flex;
  flex-direction: column;
  gap: 2rem;
}

/* Logo */
.sidebar-logo {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  text-decoration: none;
  color: var(--color-fg);
  font-weight: 700;
  font-size: 16px;
  padding: 0 0.5rem;
  letter-spacing: -0.02em;
}

.logo-icon {
  width: 32px;
  height: 32px;
  flex-shrink: 0;
}

.logo-text {
  white-space: nowrap;
}

/* Navigation */
.sidebar-nav {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.nav-link {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.625rem 0.75rem;
  border-radius: var(--radius-md);
  font-size: 14px;
  font-weight: 500;
  color: var(--color-fg-muted);
  text-decoration: none;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

.nav-link:hover {
  color: var(--color-fg);
  background: var(--color-bg-inset);
}

.nav-link-active {
  color: var(--color-accent);
  background: var(--color-accent-muted);
}

.nav-link-active :deep(.ui-icon) {
  color: var(--color-accent);
}

.sidebar-bottom {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.nav-link-external {
  opacity: 0.8;
}

.sidebar-actions {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding-top: 1rem;
  border-top: 1px solid var(--color-border);
}

.action-btn {
  justify-content: flex-start;
  width: 100%;
  padding-left: 0.75rem;
  height: 40px;
  font-weight: 500;
  font-size: 14px;
}

.action-btn :deep(.ui-icon) {
  margin-right: 0.75rem;
}

/* Settings form */
.settings-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.setting-item {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.setting-label {
  font-size: 14px;
  font-weight: 500;
}

.setting-description {
  font-size: 12px;
  color: var(--color-fg-muted);
  margin: 0;
}

/* Tablet: Collapsed sidebar */
@media (max-width: 1280px) and (min-width: 769px) {
  .sidebar {
    width: 64px;
    padding: 1.5rem 0.5rem;
    align-items: center;
  }
  
  .logo-text, .nav-link span, .action-btn span {
    display: none;
  }
  
  .sidebar-logo {
    padding: 0;
    justify-content: center;
  }
  
  .nav-link {
    justify-content: center;
    padding: 0.75rem;
  }
  
  .action-btn {
    justify-content: center;
    padding: 0;
  }
  
  .action-btn :deep(.ui-icon) {
    margin-right: 0;
  }
}

/* Mobile: Hide desktop sidebar, show mobile drawer */
@media (max-width: 768px) {
  .sidebar:not(.sidebar-mobile) {
    display: none;
  }
  
  .sidebar-mobile .sidebar-logo {
    display: none;
  }
  
  .sidebar-mobile .sidebar-top {
    padding-top: 1rem;
  }
}
</style>
