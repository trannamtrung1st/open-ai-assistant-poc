import React, { useEffect, useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Typography, Card, Descriptions, Spin, Button } from 'antd';
import { LeftOutlined } from '@ant-design/icons';
import { Asset } from '../../types/asset';
import { assetService } from '../../services/assetService';

const { Title } = Typography;

const AssetDetails: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [asset, setAsset] = useState<Asset | undefined>();
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const loadAsset = async () => {
      if (id) {
        const data = await assetService.getAssetById(id);
        setAsset(data);
        setLoading(false);
      }
    };
    loadAsset();
  }, [id]);

  if (loading) {
    return <Spin size="large" />;
  }

  if (!asset) {
    return <div>Asset not found</div>;
  }

  return (
    <div>
      <Button 
        icon={<LeftOutlined />} 
        onClick={() => navigate('/assets')}
        style={{ marginBottom: 16 }}
      >
        Back to Assets
      </Button>
      <Title level={2}>Asset Details</Title>
      <Card>
        <Descriptions bordered>
          <Descriptions.Item label="Asset ID" span={3}>{asset.id}</Descriptions.Item>
          <Descriptions.Item label="Name" span={3}>{asset.name}</Descriptions.Item>
        </Descriptions>
      </Card>
    </div>
  );
};

export default AssetDetails; 