import { useState } from 'react';
import { useAuth } from '../contexts/AuthContext';

export const UserProfile = () => {
  const { user, logout } = useAuth();
  const [isLoggingOut, setIsLoggingOut] = useState(false);

  if (!user) {
    return null;
  }

  const handleLogout = async () => {
    setIsLoggingOut(true);
    try {
      await logout();
    } catch (error) {
      console.error('Logout error:', error);
    } finally {
      setIsLoggingOut(false);
    }
  };

  return (
    <div style={{ 
      maxWidth: '600px', 
      margin: '2rem auto', 
      padding: '1.5rem',
      border: '1px solid #ccc',
      borderRadius: '8px',
      backgroundColor: '#f9f9f9',
      color: '#333',
    }}>
      <h2>User Profile</h2>
      <div style={{ marginBottom: '1rem' }}>
        <strong>You are logged in as:</strong>
      </div>
      <div style={{ marginBottom: '0.5rem' }}>
        <strong>Email:</strong> {user.email}
      </div>
      <div style={{ marginBottom: '0.5rem' }}>
        <strong>User ID:</strong> {user.id}
      </div>
      <div style={{ marginBottom: '1rem' }}>
        <strong>Member since:</strong> {new Date(user.createdAt).toLocaleDateString()}
      </div>
      <button
        onClick={handleLogout}
        disabled={isLoggingOut}
        style={{
          padding: '0.75rem 1.5rem',
          fontSize: '1rem',
          backgroundColor: '#dc3545',
          color: 'white',
          border: 'none',
          borderRadius: '4px',
          cursor: isLoggingOut ? 'not-allowed' : 'pointer',
          opacity: isLoggingOut ? 0.6 : 1,
        }}
      >
        {isLoggingOut ? 'Logging out...' : 'Logout'}
      </button>
    </div>
  );
};
