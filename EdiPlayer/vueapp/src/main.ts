import { createApp } from 'vue'
import App from './App.vue'
import axios from 'axios';

axios.defaults.baseURL = 'http://localhost:5000/';

createApp(App).mount('#app')
