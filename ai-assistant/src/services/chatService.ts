import { API_CONFIG } from '../config/api';
import { Message } from '../types/chat';

interface ChatApiRequest {
  message: string;
  sessionId?: string;
}

interface ChatApiResponse {
  content: string;
  sessionId: string;
  navigateToAsset?: {
    assetId: string;
    found: boolean;
  };
}

interface ErrorResponse {
  error: string;
}

export const chatService = {
  sendMessage: async (message: string, sessionId?: string): Promise<ChatApiResponse> => {
    try {
      const response = await fetch(`${API_CONFIG.baseUrl}${API_CONFIG.endpoints.chat}`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Accept': 'application/json'
        },
        body: JSON.stringify({
          message,
          sessionId
        } as ChatRequest),
      });

      const data = await response.json();
      
      if (!response.ok) {
        const errorMessage = (data as ErrorResponse).error || `HTTP error! status: ${response.status}`;
        throw new Error(errorMessage);
      }

      return data as ChatApiResponse;
    } catch (error) {
      console.error('Chat API Error:', error);
      throw error instanceof Error ? error : new Error('Failed to send message');
    }
  }
}; 