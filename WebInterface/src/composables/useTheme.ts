import { ref, onMounted } from 'vue'

type Theme = 'light' | 'dark' | 'system'

const theme = ref<Theme>('system')
const isDark = ref(false)

function getSystemTheme(): boolean {
  return window.matchMedia('(prefers-color-scheme: dark)').matches
}

function updateTheme() {
  const dark = theme.value === 'system' ? getSystemTheme() : theme.value === 'dark'
  isDark.value = dark
  
  if (dark) {
    document.documentElement.classList.add('dark')
  } else {
    document.documentElement.classList.remove('dark')
  }
}

function setTheme(newTheme: Theme) {
  theme.value = newTheme
  localStorage.setItem('theme', newTheme)
  updateTheme()
}

function toggleTheme() {
  if (theme.value === 'system') {
    setTheme(getSystemTheme() ? 'light' : 'dark')
  } else {
    setTheme(theme.value === 'dark' ? 'light' : 'dark')
  }
}

export function useTheme() {
  onMounted(() => {
    // Load saved theme
    const saved = localStorage.getItem('theme') as Theme | null
    if (saved && ['light', 'dark', 'system'].includes(saved)) {
      theme.value = saved
    }
    updateTheme()
    
    // Listen for system theme changes
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    mediaQuery.addEventListener('change', () => {
      if (theme.value === 'system') {
        updateTheme()
      }
    })
  })
  
  return {
    theme,
    isDark,
    setTheme,
    toggleTheme,
  }
}
