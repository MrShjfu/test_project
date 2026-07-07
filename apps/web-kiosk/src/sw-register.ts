// Registers the PWA service worker emitted by vite-plugin-pwa (generateSW
// strategy, registerType: 'autoUpdate' — see vite.config.mts). Precaches the
// app shell only; no runtime caching rules (offline data sync is out of scope
// per spec). Split out of main.tsx so it can be no-op'd trivially in tests.
import { registerSW } from 'virtual:pwa-register';

registerSW({ immediate: true });
