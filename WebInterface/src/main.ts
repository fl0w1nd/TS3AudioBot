import { createApp } from 'vue'
import { createRouter, createWebHashHistory } from 'vue-router'
import App from './App.vue'
import { ApiAuth } from './api/auth'
import { api } from './api/client'
import './styles/main.css'
import FloatingVue from 'floating-vue'
import 'floating-vue/dist/style.css'

// Routes
import Home from './pages/Home.vue'
import Bots from './pages/Bots.vue'
import Bot from './pages/Bot.vue'
import BotServer from './pages/BotServer.vue'
import BotSettings from './pages/BotSettings.vue'
import BotPlaylists from './pages/BotPlaylists.vue'
import BotRecordings from './pages/BotRecordings.vue'
import Overview from './pages/Overview.vue'

const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', component: Home },
    { path: '/bots', component: Bots, name: 'bots' },
    { path: '/overview', component: Overview, name: 'overview' },
    {
      path: '/bot/:id',
      component: Bot,
      props: route => ({ botId: Number(route.params.id), online: true }),
      children: [
        { path: '', redirect: to => ({ name: 'bot-server', params: to.params }) },
        { path: 'server', name: 'bot-server', component: BotServer },
        { path: 'playlists/:playlist?', name: 'bot-playlists', component: BotPlaylists },
        { path: 'recordings', name: 'bot-recordings', component: BotRecordings },
        { path: 'settings', name: 'bot-settings', component: BotSettings, props: { online: true } },
      ]
    },
    {
      path: '/bot_offline/:name',
      component: Bot,
      props: route => ({ botName: route.params.name as string, online: false }),
      children: [
        { path: 'settings', name: 'bot-settings-offline', component: BotSettings, props: { online: false } },
      ]
    },
  ]
})

// Restore auth from localStorage
const savedAuth = localStorage.getItem('api_auth')
if (savedAuth) {
  api.auth = ApiAuth.fromString(savedAuth)
}

const app = createApp(App)
app.use(router)
app.use(FloatingVue)
app.mount('#app')
