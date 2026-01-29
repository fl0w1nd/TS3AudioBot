<script setup lang="ts">
import { computed, ref } from 'vue'

interface Props {
  modelValue?: number
  min?: number
  max?: number
  step?: number
  disabled?: boolean
  showValue?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  modelValue: 0,
  min: 0,
  max: 100,
  step: 1,
  disabled: false,
  showValue: false,
})

const emit = defineEmits<{
  'update:modelValue': [value: number]
  'change': [value: number]
}>()

const isDragging = ref(false)

const percentage = computed(() => {
  return ((props.modelValue - props.min) / (props.max - props.min)) * 100
})

function handleInput(event: Event) {
  const target = event.target as HTMLInputElement
  const value = Number(target.value)
  emit('update:modelValue', value)
}

function handleChange(event: Event) {
  const target = event.target as HTMLInputElement
  const value = Number(target.value)
  emit('change', value)
}
</script>

<template>
  <div
    :class="[
      'slider-wrapper',
      {
        'slider-disabled': disabled,
        'slider-dragging': isDragging,
      }
    ]"
  >
    <input
      type="range"
      :value="modelValue"
      :min="min"
      :max="max"
      :step="step"
      :disabled="disabled"
      class="slider-input"
      :style="{ '--fill-percent': `${percentage}%` }"
      @input="handleInput"
      @change="handleChange"
      @mousedown="isDragging = true"
      @mouseup="isDragging = false"
      @touchstart="isDragging = true"
      @touchend="isDragging = false"
    />
    <span v-if="showValue" class="slider-value">{{ modelValue }}</span>
  </div>
</template>

<style scoped>
.slider-wrapper {
  position: relative;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  width: 100%;
  height: 20px;
}

.slider-input {
  -webkit-appearance: none;
  appearance: none;
  flex: 1;
  height: 4px;
  margin: 0;
  background: linear-gradient(
    to right,
    var(--color-accent) 0%,
    var(--color-accent) var(--fill-percent, 0%),
    var(--color-bg-inset) var(--fill-percent, 0%),
    var(--color-bg-inset) 100%
  );
  border-radius: 2px;
  cursor: pointer;
  outline: none;
}

/* WebKit (Chrome, Safari, Edge) */
.slider-input::-webkit-slider-thumb {
  -webkit-appearance: none;
  appearance: none;
  width: 14px;
  height: 14px;
  background: var(--color-bg-elevated);
  border: 2px solid var(--color-accent);
  border-radius: 50%;
  cursor: pointer;
  box-shadow: 0 2px 4px oklch(0 0 0 / 0.15);
  transition: transform 0.1s ease-out, box-shadow 0.1s ease-out;
}

.slider-input::-webkit-slider-thumb:hover {
  transform: scale(1.1);
}

.slider-dragging .slider-input::-webkit-slider-thumb {
  transform: scale(1.15);
  box-shadow: 
    0 0 0 4px var(--color-accent-muted),
    0 2px 8px oklch(0 0 0 / 0.2);
}

/* Firefox */
.slider-input::-moz-range-thumb {
  width: 14px;
  height: 14px;
  background: var(--color-bg-elevated);
  border: 2px solid var(--color-accent);
  border-radius: 50%;
  cursor: pointer;
  box-shadow: 0 2px 4px oklch(0 0 0 / 0.15);
  transition: transform 0.1s ease-out, box-shadow 0.1s ease-out;
}

.slider-input::-moz-range-thumb:hover {
  transform: scale(1.1);
}

.slider-input::-moz-range-track {
  height: 4px;
  background: transparent;
  border-radius: 2px;
}

.slider-value {
  min-width: 2.5rem;
  font-size: 12px;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  color: var(--color-fg-muted);
  text-align: right;
}

.slider-disabled {
  opacity: 0.5;
  pointer-events: none;
}

.slider-disabled .slider-input {
  cursor: not-allowed;
}
</style>
