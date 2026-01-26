<script setup lang="ts">
interface Props {
  variant?: 'solid' | 'soft' | 'outline'
  color?: 'accent' | 'neutral' | 'success' | 'warning' | 'error' | 'info'
  size?: 'sm' | 'md'
  dot?: boolean
}

withDefaults(defineProps<Props>(), {
  variant: 'soft',
  color: 'neutral',
  size: 'md',
  dot: false,
})
</script>

<template>
  <span
    :class="[
      'badge',
      `badge-${variant}`,
      `badge-${color}`,
      `badge-${size}`,
    ]"
  >
    <span v-if="dot" class="badge-dot" />
    <slot />
  </span>
</template>

<style scoped>
.badge {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  font-family: var(--font-sans);
  font-weight: 500;
  white-space: nowrap;
  border-radius: var(--radius-md);
}

/* Sizes */
.badge-sm {
  height: 18px;
  padding: 0 0.375rem;
  font-size: 10px;
}

.badge-md {
  height: 22px;
  padding: 0 0.5rem;
  font-size: 11px;
}

/* Dot */
.badge-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
}

/* Soft variants */
.badge-soft.badge-neutral {
  background: var(--color-bg-inset);
  color: var(--color-fg-muted);
}

.badge-soft.badge-accent {
  background: var(--color-accent-muted);
  color: var(--color-accent);
}

.badge-soft.badge-success {
  background: var(--color-success-muted);
  color: var(--color-success);
}

.badge-soft.badge-warning {
  background: var(--color-warning-muted);
  color: var(--color-warning);
}

.badge-soft.badge-error {
  background: var(--color-error-muted);
  color: var(--color-error);
}

.badge-soft.badge-info {
  background: var(--color-info-muted);
  color: var(--color-info);
}

/* Solid variants */
.badge-solid.badge-neutral {
  background: var(--color-fg);
  color: var(--color-bg);
}

.badge-solid.badge-accent {
  background: var(--color-accent);
  color: white;
}

.badge-solid.badge-success {
  background: var(--color-success);
  color: white;
}

.badge-solid.badge-error {
  background: var(--color-error);
  color: white;
}

/* Outline variants */
.badge-outline {
  background: transparent;
  box-shadow: var(--shadow-sm);
}

.badge-outline.badge-neutral {
  color: var(--color-fg-muted);
}

.badge-outline.badge-accent {
  color: var(--color-accent);
}
</style>
