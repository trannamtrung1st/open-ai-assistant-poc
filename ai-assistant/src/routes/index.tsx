import { createBrowserRouter } from 'react-router-dom';
import MainLayout from '../components/Layout/MainLayout';
import HomePage from '../pages/Home';
import AssetsPage from '../pages/Assets';
import AssetDetails from '../pages/Assets/AssetDetails';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <MainLayout />,
    children: [
      {
        index: true,
        element: <HomePage />,
      },
      {
        path: 'assets',
        element: <AssetsPage />,
      },
      {
        path: 'assets/:id',
        element: <AssetDetails />,
      },
      {
        path: '*',
        element: <div>404 - Page Not Found</div>,
      },
    ],
  },
]);