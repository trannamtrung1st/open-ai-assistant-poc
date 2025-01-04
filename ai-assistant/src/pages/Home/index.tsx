import React from 'react';
import { Typography } from 'antd';

const { Title } = Typography;

const HomePage: React.FC = () => {
  return (
    <div>
      <Title level={2}>Home</Title>
      <p>Welcome to AI Assistant!</p>
    </div>
  );
};

export default HomePage;
