import { API_CONFIG } from '../config/api';
import { Asset } from '../types/asset';

export const assetService = {
  getAssets: async (): Promise<Asset[]> => {
    const response = await fetch(`${API_CONFIG.baseUrl}/api/assets`);
    if (!response.ok) {
      throw new Error('Failed to fetch assets');
    }
    return response.json();
  },

  getAssetById: async (id: string): Promise<Asset | undefined> => {
    const response = await fetch(`${API_CONFIG.baseUrl}/api/assets/${id}`);
    if (!response.ok) {
      if (response.status === 404) {
        return undefined;
      }
      throw new Error('Failed to fetch asset');
    }
    return response.json();
  },
}; 