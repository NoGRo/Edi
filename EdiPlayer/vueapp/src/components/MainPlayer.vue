<template>
  <div class="video-player">
    <video ref="videoPlayer" controls></video>
    <input type="file" @change="handleFileChange" accept="video/*" />
  </div>
</template>

<script lang="ts">
import { defineComponent, ref } from 'vue';
import { useStore } from 'vuex';

export default defineComponent({
  setup() {
    const videoPlayer = ref<HTMLVideoElement | null>(null);
    const store = useStore();

    const handleFileChange = (event: Event) => {
      const target = event.target as HTMLInputElement;
      if (target.files && target.files[0]) {
        const file = target.files[0];
        const url = URL.createObjectURL(file);
        if (videoPlayer.value) {
          videoPlayer.value.src = url;
        }
        store.dispatch('edi/LoadFile', file.path);
      }
    };

    return { videoPlayer, handleFileChange };
  },
});
</script>

<style>
.video-player {
  /* Estilos básicos para el reproductor de video */
}
</style>