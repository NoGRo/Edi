<template>
  <v-data-table
    :headers="headers"
    :items="devices"
    :items-per-page="5"
    class="elevation-1"
  >
    <template v-slot:item.name="{ item }">
      <span>{{ item.name }}</span>
    </template>

    <template v-slot:item.selectedVariant="{ item }">
      <v-select
        :items="item.variants"
        v-model="item.selectedVariant"
        @change="onVariantChange(item)"
      ></v-select>
    </template>

    <template v-slot:item.isReady="{ item }">
      <span :class="getReadyClass(item.isReady)">{{ getReadyIcon(item.isReady) }}</span>
    </template>
  </v-data-table>
</template>

<script lang="ts">
import { Vue, Component } from 'vue-property-decorator';

@Component
export default class DevicesGrid extends Vue {
  headers = [
    { text: 'Name', value: 'name' },
    { text: 'Selected Variant', value: 'selectedVariant' },
    { text: 'Ready', value: 'isReady' }
  ];

  devices = []; // Aquí deberías cargar los datos de tus dispositivos

  onVariantChange(device: any) {
    // Lógica para manejar el cambio en la selección de variantes
  }

  getReadyIcon(isReady: boolean): string {
    return isReady ? '✔️' : '❌'; // Iconos de ejemplo
  }

  getReadyClass(isReady: boolean): string {
    return isReady ? 'ready-class' : 'not-ready-class'; // Clases CSS de ejemplo
  }
}
</script>

<style scoped>
.ready-class {
  /* Estilos para cuando está listo */
}
.not-ready-class {
  /* Estilos para cuando no está listo */
}
</style>
