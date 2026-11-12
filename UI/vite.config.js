import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 로컬 백엔드 바인딩 주소 (IPv6 ECONNREFUSED AggregateError 방지를 위해 127.0.0.1 명시)
const BACKEND_TARGET = 'http://127.0.0.1:5130'

// https://vitejs.dev/config/
export default defineConfig(({ mode }) => {
  const isProd = mode === 'production'

  return {
    plugins: [react()],
    base: './',
    define: {
      __API_BASE__: JSON.stringify(isProd ? '/APIs/o' : '/APIs/o')
    },
    server: {
      port: 5180,
      proxy: {
        // 1. 웹서버 빌드 경로(/APIs/o 또는 APIs/o) 로컬 프록시 매핑 (rewrite를 통해 로컬 5120 포트로 전달)
        '^/APIs/o': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000,
          rewrite: (path) => path.replace(/^\/?APIs\/o/, '') || '/'
        },
        'APIs/o/Login': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000,
          rewrite: (path) => path.replace(/^\/?APIs\/o/, '') || '/'
        },
        '/APIs/o/Login': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000,
          rewrite: (path) => path.replace(/^\/?APIs\/o/, '') || '/'
        },
        '/APIs/o/Project': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000,
          rewrite: (path) => path.replace(/^\/?APIs\/o/, '') || '/'
        },
        '/APIs/o/pro_list': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000,
          rewrite: (path) => path.replace(/^\/?APIs\/o/, '') || '/'
        },
        '/APIs/o/Pro_list': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000,
          rewrite: (path) => path.replace(/^\/?APIs\/o/, '') || '/'
        },
        '/APIs/o/pro_register': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000,
          rewrite: (path) => path.replace(/^\/?APIs\/o/, '') || '/'
        },
        '/APIs/o/Pro_register': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000,
          rewrite: (path) => path.replace(/^\/?APIs\/o/, '') || '/'
        },

        // 2. 기존 로컬 직접 경로 호환성 유지 (/Login, /Project, /pro_list, /pro_register)
        '/Login': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000
        },
        '/Project': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000
        },
        '/pro_list': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000
        },
        '/Pro_list': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000
        },
        '/pro_register': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000
        },
        '/Pro_register': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          secure: false,
          timeout: 300000,
          proxyTimeout: 300000
        },

        // 3. SignalR Hub 웹소켓 프록시 (/hubs/worker 등)
        '/hubs': {
          target: BACKEND_TARGET,
          changeOrigin: true,
          ws: true,
          secure: false
        }
      }
    }
  }
})

