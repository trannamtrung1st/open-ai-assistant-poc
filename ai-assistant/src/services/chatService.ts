import { API_CONFIG } from '../config/api';
import { Message } from '../types/chat';

interface ChatResponse {
  content: string;
  error?: string;
}

export const chatService = {
  sendMessage: async (message: string): Promise<ChatResponse> => {
    try {
      const response = await fetch(`${API_CONFIG.baseUrl}${API_CONFIG.endpoints.chat}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          message,
          timestamp: new Date().toISOString()
        }),
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      const data = await response.json();
      return { content: data.content };
    } catch (error) {
      console.error('Chat API Error:', error);
      return {
        content: "I apologize, but I'm having trouble processing your request right now.",
        error: error instanceof Error ? error.message : 'Unknown error'
      };
    }
  }
}; 