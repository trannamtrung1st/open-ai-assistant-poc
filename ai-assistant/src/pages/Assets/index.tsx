import React, { useEffect, useState } from 'react';
import { Typography, Table, Spin } from 'antd';
import { useNavigate } from 'react-router-dom';
import type { ColumnsType } from 'antd/es/table';
import { Asset } from '../../types/asset';
import { assetService } from '../../services/assetService';

const { Title } = Typography;

const AssetsPage: React.FC = () => {
  const navigate = useNavigate();
  const [assets, setAssets] = useState<Asset[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const loadAssets = async () => {
      const data = await assetService.getAssets();
      setAssets(data);
      setLoading(false);
    };
    loadAssets();
  }, []);

  const columns: ColumnsType<Asset> = [
    {
      title: 'Asset ID',
      dataIndex: 'id',
      key: 'id',
    },
    {
      title: 'Name',
      dataIndex: 'name',
      key: 'name',
    },
  ];

  return (
    <div>
      <Title level={2}>Assets</Title>
      <Table
        columns={columns}
        dataSource={assets}
        loading={loading}
        rowKey="id"
        onRow={(record) => ({
          onClick: () => navigate(`/assets/${record.id}`),
          style: { cursor: 'pointer' }
        })}
      />
    </div>
  );
};

export default AssetsPage; 