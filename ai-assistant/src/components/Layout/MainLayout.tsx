import React from 'react';
import { Layout } from 'antd';
import { Outlet } from 'react-router-dom';
import Sider from './Sider';
import ChatPopup from '../ChatPopup';

const { Content } = Layout;

const MainLayout: React.FC = () => {
  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider />
      <Layout>
        <Content style={{ margin: '24px 16px', padding: 24, background: '#fff' }}>
          <Outlet />
        </Content>
      </Layout>
      <ChatPopup />
    </Layout>
  );
};

export default MainLayout; 