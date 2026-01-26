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
    <div class="slider-track">
      <div
        class="slider-fill"
        :style="{ width: `${percentage}%` }"
      />
      <div
        class="slider-thumb"
        :style="{ left: `${percentage}%` }"
      />
    </div>
    <input
      type="range"
      :value="modelValue"
      :min="min"
      :max="max"
      :step="step"
      :disabled="disabled"
      class="slider-input"
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

.slider-track {
  position: relative;
  flex: 1;
  height: 4px;
  background: var(--color-bg-inset);
  border-radius: 2px;
  overflow: visible;
}

.slider-fill {
  position: absolute;
  top: 0;
  left: 0;
  height: 100%;
  background: var(--color-accent);
  border-radius: 2px;
  transition: width 0.05s ease-out;
}

.slider-thumb {
  position: absolute;
  top: 50%;
  width: 14px;
  height: 14px;
  background: var(--color-bg-elevated);
  border-radius: 50%;
  transform: translate(-50%, -50%);
  box-shadow: 
    0 0 0 2px var(--color-accent),
    0 2px 4px oklch(0 0 0 / 0.15);
  transition: transform 0.1s ease-out, box-shadow 0.1s ease-out;
  pointer-events: none;
}

.slider-dragging .slider-thumb {
  transform: translate(-50%, -50%) scale(1.15);
  box-shadow: 
    0 0 0 2px var(--color-accent),
    0 0 0 4px var(--color-accent-muted),
    0 2px 8px oklch(0 0 0 / 0.2);
}

.slider-input {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  margin: 0;
  opacity: 0;
  cursor: pointer;
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
