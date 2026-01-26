<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  modelValue?: string | number
  type?: string
  placeholder?: string
  disabled?: boolean
  readonly?: boolean
  size?: 'sm' | 'md' | 'lg'
  error?: boolean
  prefix?: string
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: '',
  type: 'text',
  placeholder: '',
  disabled: false,
  readonly: false,
  size: 'md',
  error: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
  'focus': [e: FocusEvent]
  'blur': [e: FocusEvent]
  'keydown': [e: KeyboardEvent]
}>()

const inputValue = computed({
  get: () => String(props.modelValue ?? ''),
  set: (value: string) => emit('update:modelValue', value),
})
</script>

<template>
  <div
    :class="[
      'input-wrapper',
      `input-${size}`,
      {
        'input-disabled': disabled,
        'input-error': error,
        'has-prefix': prefix || $slots.prefix,
        'has-suffix': $slots.suffix,
      }
    ]"
  >
    <span v-if="prefix || $slots.prefix" class="input-prefix">
      <slot name="prefix">{{ prefix }}</slot>
    </span>
    
    <input
      v-model="inputValue"
      :type="type"
      :placeholder="placeholder"
      :disabled="disabled"
      :readonly="readonly"
      class="input-field"
      @focus="emit('focus', $event)"
      @blur="emit('blur', $event)"
      @keydown="emit('keydown', $event)"
    />
    
    <span v-if="$slots.suffix" class="input-suffix">
      <slot name="suffix" />
    </span>
  </div>
</template>

<style scoped>
.input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
  background: var(--color-bg-elevated);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-sm);
  transition: box-shadow 0.15s ease-out;
}

.input-wrapper:focus-within {
  box-shadow: 
    0 0 0 2px var(--color-bg),
    0 0 0 4px var(--color-accent);
}

.input-error {
  box-shadow: 
    0 0 0 1px var(--color-error),
    0 1px 2px oklch(0 0 0 / 0.05);
}

.input-disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.input-field {
  flex: 1;
  min-width: 0;
  background: transparent;
  border: none;
  outline: none;
  font-family: var(--font-sans);
  color: var(--color-fg);
}

.input-field::placeholder {
  color: var(--color-fg-subtle);
}

.input-field:disabled {
  cursor: not-allowed;
}

/* Sizes */
.input-sm {
  height: 28px;
}

.input-sm .input-field {
  padding: 0 0.5rem;
  font-size: 12px;
}

.input-md {
  height: 34px;
}

.input-md .input-field {
  padding: 0 0.75rem;
  font-size: 13px;
}

.input-lg {
  height: 40px;
}

.input-lg .input-field {
  padding: 0 0.875rem;
  font-size: 14px;
}

/* Prefix/Suffix */
.input-prefix,
.input-suffix {
  display: flex;
  align-items: center;
  color: var(--color-fg-muted);
  flex-shrink: 0;
}

.input-prefix {
  padding-left: 0.625rem;
}

.input-suffix {
  padding-right: 0.625rem;
}

.has-prefix .input-field {
  padding-left: 0.375rem;
}

.has-suffix .input-field {
  padding-right: 0.375rem;
}
</style>
