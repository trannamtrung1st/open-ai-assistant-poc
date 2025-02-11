import { API_CONFIG } from '../config/api';

interface CommandResult {
  command: string;
  data: any;
}

interface ChatApiResponse {
  content: string;
  sessionId: string;
  commandResults?: CommandResult[];
}

interface ErrorResponse {
  error: string;
}

export const assistantService = {
  getTokenCount: async (sessionId: string): Promise<number> => {
    const response = await fetch(`${API_CONFIG.baseUrl}${API_CONFIG.endpoints.assistants}/token-count?sessionId=${sessionId}`, {
      method: 'GET'
    });
    const data = await response.json();
    return data.tokenCount;
  },

  sendMessage: async (message: string, sessionId?: string): Promise<ChatApiResponse> => {
    try {
      const response = await fetch(`${API_CONFIG.baseUrl}${API_CONFIG.endpoints.assistants}/messages`, {
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