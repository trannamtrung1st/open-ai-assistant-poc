import { Asset } from '../types/asset';

const mockAssets: Asset[] = [
  { id: '2a8ebca1-3cba-4fd1-937b-ba933af12fb2', name: 'Pump 001' },
  { id: 'a10c9b73-3dd9-40dc-8eaf-08c2c078ec80', name: 'Boiler 002' },
  { id: 'a55e47bd-b286-48f7-8301-f91c580b19bc', name: 'Palletizer 100' },
];

export const assetService = {
  getAssets: async (): Promise<Asset[]> => {
    // Simulate API call
    return new Promise((resolve) => {
      setTimeout(() => {
        resolve(mockAssets);
      }, 500);
    });
  },

  getAssetById: async (id: string): Promise<Asset | undefined> => {
    // Simulate API call
    return new Promise((resolve) => {
      setTimeout(() => {
        resolve(mockAssets.find(asset => asset.id === id));
      }, 500);
    });
  },
}; 