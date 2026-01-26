<script setup lang="ts">
interface Props {
  padding?: 'none' | 'sm' | 'md' | 'lg'
  hoverable?: boolean
  clickable?: boolean
}

withDefaults(defineProps<Props>(), {
  padding: 'md',
  hoverable: false,
  clickable: false,
})
</script>

<template>
  <div
    :class="[
      'card',
      `card-p-${padding}`,
      {
        'card-hoverable': hoverable,
        'card-clickable': clickable,
      }
    ]"
  >
    <slot />
  </div>
</template>

<style scoped>
.card {
  background: var(--color-bg-elevated);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-md);
}

/* Padding */
.card-p-none { padding: 0; }
.card-p-sm { padding: 0.75rem; }
.card-p-md { padding: 1rem; }
.card-p-lg { padding: 1.5rem; }

/* Hoverable */
.card-hoverable {
  transition: box-shadow 0.15s ease-out, transform 0.15s ease-out;
}

.card-hoverable:hover {
  box-shadow: var(--shadow-lg);
}

/* Clickable */
.card-clickable {
  cursor: pointer;
  transition: box-shadow 0.15s ease-out, transform 0.15s ease-out;
}

.card-clickable:hover {
  box-shadow: var(--shadow-lg);
  transform: translateY(-1px);
}

.card-clickable:active {
  transform: translateY(0);
}
</style>
