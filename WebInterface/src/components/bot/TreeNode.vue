<script lang="ts">
import { defineComponent, type PropType, ref } from 'vue'
import { Button, Icon, Input } from '@/components/ui'
import { TargetSendMode, type CmdServerTree, type CmdServerTreeChannel, type CmdServerTreeUser, type CmdWhisperList } from '@/api/types'

interface ChannelNode {
  own: CmdServerTreeChannel
  after?: ChannelNode
  children: ChannelNode[]
  users: CmdServerTreeUser[]
}

interface TreeMeta {
  tree: CmdServerTree
  whisperList: CmdWhisperList
}

export default defineComponent({
  name: 'TreeNode',
  components: { Button, Icon, Input },
  props: {
    node: { type: Object as PropType<ChannelNode>, required: true },
    meta: { type: Object as PropType<TreeMeta>, required: true },
    expandedChannels: { type: Object as PropType<Set<number>>, required: true },
  },
  emits: ['toggle', 'move', 'whisper', 'stop-whisper', 'voice-here', 'rename-bot'],
  setup() {
    const editingUserId = ref<number | null>(null)
    const editingName = ref('')
    
    return { editingUserId, editingName }
  },
  computed: {
    hasChildren(): boolean {
      return this.node.children.length > 0 || this.node.users.length > 0
    },
    isExpanded(): boolean {
      return this.expandedChannels.has(this.node.own.Id)
    },
    channelDisplay(): { spacerClass: string; displayName: string } {
      const name = this.node.own.Name
      const match = /^\[(c|r|\*|)spacer[^\]]*\](.*)$/.exec(name)
      if (!match) return { spacerClass: '', displayName: name }
      let displayName = match[2]
      let spacerClass = ''
      if (match[1] === '*') {
        spacerClass = 'spacer-fill'
        displayName = displayName.repeat(Math.ceil(50 / (displayName.length || 1)))
      } else if (match[1] === 'c') {
        spacerClass = 'spacer-center'
      } else if (match[1] === 'r') {
        spacerClass = 'spacer-right'
      }
      return { spacerClass, displayName }
    },
    isChannelWhispering(): boolean {
      if (this.meta.whisperList.SendMode !== TargetSendMode.Whisper) return false
      return this.meta.whisperList.WhisperChannel?.includes(this.node.own.Id) ?? false
    }
  },
  methods: {
    isUserWhispering(userId: number): boolean {
      if (this.meta.whisperList.SendMode === TargetSendMode.Voice) {
        return userId === this.meta.tree.OwnClient
      }
      if (this.meta.whisperList.SendMode === TargetSendMode.Whisper) {
        return this.meta.whisperList.WhisperClients?.includes(userId) ?? false
      }
      return false
    },
    isOwnBot(userId: number): boolean {
      return this.meta.tree.OwnClient === userId
    },
    startEditName(user: CmdServerTreeUser) {
      this.editingUserId = user.Id
      this.editingName = user.Name
    },
    cancelEditName() {
      this.editingUserId = null
      this.editingName = ''
    },
    saveEditName() {
      if (this.editingName.trim()) {
        this.$emit('rename-bot', this.editingName.trim())
      }
      this.editingUserId = null
      this.editingName = ''
    }
  }
})
</script>

<template>
  <li class="tree-node">
    <div class="channel-line" :class="{ 'channel-playing': isChannelWhispering }">
      <button v-if="hasChildren" class="collapse-btn" @click="$emit('toggle', node.own.Id)">
        <Icon :name="isExpanded ? 'chevron-down' : 'chevron-right'" :size="14" />
      </button>
      <span v-else class="collapse-spacer" />
      <span class="channel-icon">
        <Icon :name="node.own.Subscribed ? 'channel' : 'channel-off'" :size="14" />
      </span>
      <span class="channel-name" :class="channelDisplay.spacerClass">{{ channelDisplay.displayName }}</span>
      <span v-if="node.own.HasPassword" class="channel-lock">
        <Icon name="lock" :size="12" />
      </span>
      <span v-if="isChannelWhispering" class="playing-indicator">
        <Icon name="volume-high" :size="14" />
      </span>
      <div class="channel-actions">
        <Button variant="ghost" color="neutral" size="sm" icon-only title="Join" @click.stop="$emit('move', node.own)">
          <Icon name="login" :size="14" />
        </Button>
        <Button 
          v-if="!isChannelWhispering" 
          variant="ghost" 
          color="neutral" 
          size="sm" 
          icon-only 
          title="Whisper here"
          @click.stop="$emit('whisper', node.own.Id)"
        >
          <Icon name="volume-high" :size="14" />
        </Button>
        <Button 
          v-else 
          variant="ghost" 
          color="neutral" 
          size="sm" 
          icon-only 
          title="Stop whisper"
          @click.stop="$emit('stop-whisper', node.own.Id)"
        >
          <Icon name="volume-mute" :size="14" />
        </Button>
      </div>
    </div>
    
    <ul v-if="hasChildren && isExpanded" class="tree-list">
      <!-- Users -->
      <li v-for="user in node.users" :key="'u-' + user.Id" class="tree-node user-node">
        <div class="user-line" :class="{ 'user-playing': isUserWhispering(user.Id) }">
          <span class="collapse-spacer" />
          <span class="user-icon">
            <Icon :name="isOwnBot(user.Id) ? 'bot' : 'user'" :size="14" />
          </span>
          <!-- Editable name for own bot -->
          <template v-if="isOwnBot(user.Id)">
            <div v-if="editingUserId === user.Id" class="user-name-edit">
              <input
                v-model="editingName"
                type="text"
                class="user-name-input"
                @keydown.enter="saveEditName"
                @keydown.escape="cancelEditName"
                @blur="saveEditName"
              />
            </div>
            <span v-else class="user-name user-name-editable" @click="startEditName(user)">
              {{ user.Name }}
              <Icon name="edit" :size="10" class="edit-icon" />
            </span>
          </template>
          <span v-else class="user-name">{{ user.Name }}</span>
          <span v-if="isUserWhispering(user.Id)" class="playing-indicator">
            <Icon name="volume-high" :size="14" />
          </span>
          <div v-if="isOwnBot(user.Id)" class="user-actions">
            <Button variant="ghost" color="neutral" size="sm" icon-only title="Voice here" @click="$emit('voice-here')">
              <Icon name="volume-high" :size="14" />
            </Button>
          </div>
        </div>
      </li>
      
      <!-- Child channels (recursive) -->
      <TreeNode
        v-for="child in node.children"
        :key="'c-' + child.own.Id"
        :node="child"
        :meta="meta"
        :expanded-channels="expandedChannels"
        @toggle="$emit('toggle', $event)"
        @move="$emit('move', $event)"
        @whisper="$emit('whisper', $event)"
        @stop-whisper="$emit('stop-whisper', $event)"
        @voice-here="$emit('voice-here')"
        @rename-bot="$emit('rename-bot', $event)"
      />
    </ul>
  </li>
</template>

<style scoped>
.tree-node { margin: 0; }

.tree-list {
  list-style: none;
  margin: 0;
  padding: 0 0 0 1rem;
}

.channel-line,
.user-line {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.25rem 0.5rem;
  border-radius: var(--radius-sm);
  transition: background 0.1s ease-out;
}

.channel-line:hover,
.user-line:hover {
  background: var(--color-bg-inset);
}

.channel-playing,
.user-playing {
  animation: wave 4s linear infinite;
  background: linear-gradient(90deg, transparent, transparent, var(--color-success-muted), transparent, transparent);
  background-size: 500% 100%;
}

@keyframes wave {
  0% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

.collapse-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  padding: 0;
  border: none;
  background: transparent;
  color: var(--color-fg-muted);
  cursor: pointer;
  border-radius: var(--radius-sm);
}

.collapse-btn:hover {
  background: var(--color-bg-elevated);
}

.collapse-spacer {
  width: 20px;
  flex-shrink: 0;
}

.channel-icon,
.user-icon {
  display: flex;
  color: var(--color-fg-muted);
}

.channel-name,
.user-name {
  flex: 1;
  font-size: 13px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.channel-lock {
  color: var(--color-warning);
  margin-left: 0.25rem;
}

.playing-indicator {
  color: var(--color-success);
  margin-left: 0.25rem;
}

.channel-actions,
.user-actions {
  display: none;
  gap: 0.125rem;
  margin-left: auto;
}

.channel-line:hover .channel-actions,
.user-line:hover .user-actions {
  display: flex;
}

.spacer-center {
  text-align: center;
  justify-content: center;
}

.spacer-right {
  text-align: right;
  justify-content: flex-end;
}

.spacer-fill {
  text-align: center;
  overflow: hidden;
}

.user-node {
  margin-left: 0.5rem;
}

/* Editable user name */
.user-name-editable {
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.user-name-editable .edit-icon {
  opacity: 0;
  transition: opacity 0.1s ease-out;
}

.user-line:hover .user-name-editable .edit-icon {
  opacity: 0.5;
}

.user-name-edit {
  flex: 1;
  min-width: 0;
}

.user-name-input {
  width: 100%;
  padding: 0.125rem 0.375rem;
  font-size: 13px;
  border: 1px solid var(--color-accent);
  border-radius: var(--radius-sm);
  background: var(--color-bg-elevated);
  color: var(--color-fg);
  outline: none;
}
</style>
