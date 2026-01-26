<script setup lang="ts">
interface Props {
  variant?: 'solid' | 'soft' | 'ghost' | 'outline'
  color?: 'accent' | 'neutral' | 'success' | 'warning' | 'error'
  size?: 'sm' | 'md' | 'lg'
  disabled?: boolean
  loading?: boolean
  fullWidth?: boolean
  iconOnly?: boolean
  title?: string
}

withDefaults(defineProps<Props>(), {
  variant: 'solid',
  color: 'accent',
  size: 'md',
  disabled: false,
  loading: false,
  fullWidth: false,
  iconOnly: false,
  title: undefined,
})

// Remove manual click handling to rely on Vue's attribute fallthrough
// The :disabled binding on the button element already prevents clicks when loading/disabled
</script>

<template>
  <button
    v-tooltip="title"
    :class="[
      'btn',
      `btn-${variant}`,
      `btn-${color}`,
      `btn-${size}`,
      {
        'btn-disabled': disabled,
        'btn-loading': loading,
        'btn-full': fullWidth,
        'btn-icon': iconOnly,
      }
    ]"
    :disabled="disabled || loading"
  >
    <span v-if="loading" class="btn-spinner">
      <svg class="animate-spin" viewBox="0 0 24 24" fill="none">
        <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="3" stroke-opacity="0.25" />
        <path d="M12 2a10 10 0 0 1 10 10" stroke="currentColor" stroke-width="3" stroke-linecap="round" />
      </svg>
    </span>
    <span class="btn-content" :class="{ 'opacity-0': loading }">
      <slot />
    </span>
  </button>
</template>

<style scoped>
.btn {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  font-family: var(--font-sans);
  font-weight: 500;
  border: none;
  border-radius: var(--radius-md);
  cursor: pointer;
  white-space: nowrap;
  transition: all 0.15s ease-out;
  user-select: none;
}

.btn-content {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
}

.btn-spinner {
  position: absolute;
  display: flex;
  align-items: center;
  justify-content: center;
}

.btn-spinner svg {
  width: 1.125em;
  height: 1.125em;
}

/* Sizes */
.btn-sm {
  height: 28px;
  padding: 0 0.625rem;
  font-size: 12px;
}

.btn-md {
  height: 34px;
  padding: 0 0.875rem;
  font-size: 13px;
}

.btn-lg {
  height: 40px;
  padding: 0 1.125rem;
  font-size: 14px;
}

/* Icon only */
.btn-icon.btn-sm { width: 28px; padding: 0; }
.btn-icon.btn-md { width: 34px; padding: 0; }
.btn-icon.btn-lg { width: 40px; padding: 0; }

/* Solid variants */
.btn-solid.btn-accent {
  background: var(--color-accent);
  color: var(--color-accent-fg);
  box-shadow: 
    0 0 0 1px oklch(0 0 0 / 0.1),
    0 1px 2px oklch(0 0 0 / 0.1),
    inset 0 1px 0 oklch(1 0 0 / 0.1);
}

.btn-solid.btn-accent:hover:not(:disabled) {
  background: var(--color-accent-hover);
  box-shadow: 
    0 0 0 1px oklch(0 0 0 / 0.15),
    0 2px 4px oklch(0 0 0 / 0.15),
    inset 0 1px 0 oklch(1 0 0 / 0.1);
}

.btn-solid.btn-neutral {
  background: var(--color-fg);
  color: var(--color-bg);
  box-shadow: 
    0 0 0 1px oklch(0 0 0 / 0.1),
    0 1px 2px oklch(0 0 0 / 0.1);
}

.btn-solid.btn-neutral:hover:not(:disabled) {
  opacity: 0.9;
}

.btn-solid.btn-success {
  background: var(--color-success);
  color: white;
}

.btn-solid.btn-error {
  background: var(--color-error);
  color: white;
}

/* Soft variants */
.btn-soft.btn-accent {
  background: var(--color-accent-muted);
  color: var(--color-accent);
}

.btn-soft.btn-accent:hover:not(:disabled) {
  background: oklch(from var(--color-accent) l c h / 0.18);
}

.btn-soft.btn-neutral {
  background: var(--color-bg-inset);
  color: var(--color-fg);
}

.btn-soft.btn-neutral:hover:not(:disabled) {
  background: var(--color-border);
}

.btn-soft.btn-success {
  background: var(--color-success-muted);
  color: var(--color-success);
}

.btn-soft.btn-error {
  background: var(--color-error-muted);
  color: var(--color-error);
}

/* Ghost variants */
.btn-ghost {
  background: transparent;
}

.btn-ghost.btn-accent {
  color: var(--color-accent);
}

.btn-ghost.btn-accent:hover:not(:disabled) {
  background: var(--color-accent-muted);
}

.btn-ghost.btn-neutral {
  color: var(--color-fg-muted);
}

.btn-ghost.btn-neutral:hover:not(:disabled) {
  background: var(--color-bg-inset);
  color: var(--color-fg);
}

/* Outline variants */
.btn-outline {
  background: transparent;
  box-shadow: var(--shadow-sm);
}

.btn-outline.btn-accent {
  color: var(--color-accent);
}

.btn-outline.btn-accent:hover:not(:disabled) {
  background: var(--color-accent-muted);
}

.btn-outline.btn-neutral {
  color: var(--color-fg);
}

.btn-outline.btn-neutral:hover:not(:disabled) {
  background: var(--color-bg-inset);
}

/* States */
.btn-disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-loading {
  cursor: wait;
}

.btn-full {
  width: 100%;
}

/* Focus */
.btn:focus-visible {
  outline: none;
  box-shadow: 
    0 0 0 2px var(--color-bg),
    0 0 0 4px var(--color-accent);
}
</style>
