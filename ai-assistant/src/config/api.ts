export const API_CONFIG = {
  baseUrl: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000',
  endpoints: {
    chat: '/api/chat',
    assets: '/api/assets'
  }
}; 