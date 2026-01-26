<script setup lang="ts" generic="T">
interface Column<T> {
  key: keyof T | string
  label: string
  width?: string
  align?: 'left' | 'center' | 'right'
  class?: string
}

interface Props {
  data: T[]
  columns: Column<T>[]
  loading?: boolean
  emptyText?: string
  hoverable?: boolean
  striped?: boolean
}

withDefaults(defineProps<Props>(), {
  loading: false,
  emptyText: 'No data',
  hoverable: true,
  striped: false,
})

const emit = defineEmits<{
  'row-click': [row: T, index: number]
}>()
</script>

<template>
  <div class="table-wrapper">
    <table :class="['table', { 'table-striped': striped }]">
      <thead>
        <tr>
          <th
            v-for="col in columns"
            :key="String(col.key)"
            :style="{ width: col.width }"
            :class="[`text-${col.align ?? 'left'}`, col.class]"
          >
            {{ col.label }}
          </th>
        </tr>
      </thead>
      <tbody>
        <template v-if="loading">
          <tr v-for="i in 3" :key="i" class="table-loading-row">
            <td v-for="col in columns" :key="String(col.key)">
              <div class="table-skeleton animate-shimmer" />
            </td>
          </tr>
        </template>
        <template v-else-if="data.length === 0">
          <tr class="table-empty-row">
            <td :colspan="columns.length">
              <div class="table-empty">
                <slot name="empty">{{ emptyText }}</slot>
              </div>
            </td>
          </tr>
        </template>
        <template v-else>
          <tr
            v-for="(row, index) in data"
            :key="index"
            :class="{ 'table-row-hoverable': hoverable }"
            @click="emit('row-click', row, index)"
          >
            <td
              v-for="col in columns"
              :key="String(col.key)"
              :class="[`text-${col.align ?? 'left'}`, col.class]"
            >
              <slot :name="String(col.key)" :row="row" :index="index">
                {{ (row as Record<string, unknown>)[String(col.key)] }}
              </slot>
            </td>
          </tr>
        </template>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.table-wrapper {
  overflow-x: auto;
}

.table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.table th {
  padding: 0.625rem 0.75rem;
  font-weight: 500;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--color-fg-muted);
  background: var(--color-bg-inset);
  border-bottom: 1px solid var(--color-border);
}

.table td {
  padding: 0.625rem 0.75rem;
  border-bottom: 1px solid var(--color-border);
}

.table tr:last-child td {
  border-bottom: none;
}

.table-row-hoverable {
  cursor: pointer;
  transition: background 0.1s ease-out;
}

.table-row-hoverable:hover {
  background: var(--color-bg-inset);
}

.table-striped tbody tr:nth-child(even) {
  background: var(--color-bg-inset);
}

.text-left { text-align: left; }
.text-center { text-align: center; }
.text-right { text-align: right; }

.table-loading-row td {
  padding: 0.75rem;
}

.table-skeleton {
  height: 16px;
  border-radius: var(--radius-sm);
}

.table-empty {
  padding: 2rem;
  text-align: center;
  color: var(--color-fg-subtle);
}
</style>
