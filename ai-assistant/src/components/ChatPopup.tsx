'use client'

import React, { useState, useRef, useEffect } from 'react';
import { Avatar, Input, Button, Card, Typography, message } from 'antd';
import { 
  CloseOutlined, 
  SendOutlined, 
  PaperClipOutlined, 
  SmileOutlined, 
  FileImageOutlined 
} from '@ant-design/icons';
import { chatService } from '../services/chatService';
import { Message } from '../types/chat';
import { useNavigate } from 'react-router-dom';
import { API_CONFIG } from '../config/api';
import ReactMarkdown from 'react-markdown';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { vscDarkPlus } from 'react-syntax-highlighter/dist/esm/styles/prism';

const { Text } = Typography;

export default function ChatPopup() {
  const navigate = useNavigate();
  const [isOpen, setIsOpen] = useState(false);
  const [inputValue, setInputValue] = useState('');
  const [messages, setMessages] = useState<Message[]>([
    {
      id: '1',
      content: "Hello! How can I help you today?",
      isUser: false,
      timestamp: new Date()
    }
  ]);
  const [isTyping, setIsTyping] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [sessionId, setSessionId] = useState<string>();

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  const handleSendMessage = async () => {
    if (!inputValue.trim() || isLoading) return;

    const userMessage: Message = {
      id: Date.now().toString(),
      content: inputValue.trim(),
      isUser: true,
      timestamp: new Date()
    };

    setMessages(prev => [...prev, userMessage]);
    setInputValue('');
    setIsLoading(true);
    setIsTyping(true);

    try {
      const response = await chatService.sendMessage(userMessage.content, sessionId);
      setSessionId(response.sessionId);

      const aiMessage: Message = {
        id: (Date.now() + 1).toString(),
        content: response.content,
        isUser: false,
        timestamp: new Date()
      };

      setMessages(prev => [...prev, aiMessage]);
      
      if (response.commandResult?.command === 'NavigateToAsset') {
        const data = response.commandResult.data;
        if (data.found) {
          navigate(`/assets/${data.assetId}`);
        }
      }
    } catch (error) {
      console.error('Error sending message:', error);
      message.error('Failed to send message');
    } finally {
      setIsLoading(false);
      setIsTyping(false);
    }
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSendMessage();
    }
  };

  const formatMessageContent = (content: string) => {
    const imagePlaceholderRegex = /\{image:\/api\/file\/(.*?)\}/g;
    
    // Replace image placeholders with markdown image syntax
    const markdownContent = content.replace(imagePlaceholderRegex, (_, fileId) => {
      const imageUrl = `${API_CONFIG.baseUrl}/api/file/${fileId}`;
      return `![Assistant generated](${imageUrl})`;
    });

    return (
      <ReactMarkdown
        components={{
          img: ({node, ...props}) => (
            <img
              {...props}
              style={{ 
                maxWidth: '100%', 
                borderRadius: 4, 
                margin: '8px 0' 
              }} 
            />
          ),
          p: ({node, ...props}) => (
            <p 
              {...props} 
              style={{ 
                margin: '0 0' 
              }}
            />
          ),
          code({node, inline, className, children, ...props}) {
            const match = /language-(\w+)/.exec(className || '');
            return !inline && match ? (
              <SyntaxHighlighter
                {...props}
                style={vscDarkPlus}
                language={match[1]}
                PreTag="div"
              >
                {String(children).replace(/\n$/, '')}
              </SyntaxHighlighter>
            ) : (
              <code {...props} className={className}>
                {children}
              </code>
            );
          }
        }}
      >
        {markdownContent}
      </ReactMarkdown>
    );
  };

  return (
    <>
      {/* Chat toggle button */}
      <div style={{ position: 'fixed', bottom: 32, right: 32, zIndex: 1000 }}>
        {!isOpen && (
          <Button
            type="primary"
            shape="circle"
            size="large"
            icon={<SmileOutlined />}
            onClick={() => setIsOpen(true)}
            style={{ 
              width: 52, 
              height: 52,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: '24px'
            }}
          />
        )}

        {/* Chat window - positioned higher with smaller dimensions */}
        {isOpen && (
          <div style={{ position: 'absolute', bottom: 0, right: 0 }}>
            <Card
              style={{ 
                width: 580,
                boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
                borderRadius: 8,
                overflow: 'hidden'
              }}
              bodyStyle={{ padding: 0 }}
            >
              {/* Header - reduced padding */}
              <div style={{ 
                padding: '12px 16px',
                borderBottom: '1px solid #f0f0f0',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between'
              }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                  <Avatar src="https://i.pravatar.cc/100" size={40} />
                  <div>
                    <Text strong style={{ fontSize: 14 }}>AI Assistant</Text>
                    <br />
                    <Text type="secondary" style={{ fontSize: 12 }}>
                      {isTyping ? 'Typing...' : 'Online'}
                    </Text>
                  </div>
                </div>
                <Button 
                  type="text" 
                  icon={<CloseOutlined />} 
                  onClick={() => setIsOpen(false)}
                />
              </div>

              {/* Messages area - reduced height and padding */}
              <div style={{ 
                height: 440,
                overflowY: 'auto',
                padding: 16,
                background: '#f5f5f5'
              }}>
                {messages.map((message) => (
                  <div
                    key={message.id}
                    style={{ 
                      display: 'flex',
                      justifyContent: message.isUser ? 'flex-end' : 'flex-start',
                      marginBottom: 12
                    }}
                  >
                    {!message.isUser && (
                      <Avatar
                        src="https://i.pravatar.cc/100"
                        size={28}
                        style={{ marginRight: 8, marginTop: 4 }}
                      />
                    )}
                    <div style={{ 
                      background: message.isUser ? '#1890ff' : '#e6f7ff',
                      color: message.isUser ? '#fff' : '#000',
                      padding: '8px 12px',
                      borderRadius: 8,
                      maxWidth: '75%',
                      wordBreak: 'break-word'
                    }}>
                      <Text style={{ 
                        color: message.isUser ? '#fff' : 'inherit',
                        fontSize: 13
                      }}>
                        {formatMessageContent(message.content)}
                      </Text>
                    </div>
                  </div>
                ))}
                <div ref={messagesEndRef} />
              </div>

              {/* Input area - reduced padding */}
              <div style={{ 
                padding: '12px 16px',
                borderTop: '1px solid #f0f0f0',
                background: '#fff'
              }}>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                  <Input 
                    placeholder="Type a message..."
                    value={inputValue}
                    onChange={(e) => setInputValue(e.target.value)}
                    onKeyPress={handleKeyPress}
                    bordered={false}
                    style={{ 
                      flex: 1,
                      fontSize: 13
                    }}
                  />
                  <div style={{ display: 'flex', gap: 8 }}>
                    <Button type="text" icon={<PaperClipOutlined />} />
                    <Button type="text" icon={<FileImageOutlined />} />
                    <Button type="text" icon={<SmileOutlined />} />
                    <Button 
                      type="primary" 
                      icon={<SendOutlined />} 
                      onClick={handleSendMessage}
                      disabled={!inputValue.trim() || isLoading}
                      loading={isLoading}
                    />
                  </div>
                </div>
              </div>
            </Card>
          </div>
        )}
      </div>
    </>
  );
} 