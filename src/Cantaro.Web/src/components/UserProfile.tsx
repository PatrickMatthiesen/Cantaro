import { useAuth } from '../contexts/AuthContext';

export const UserProfile = () => {
  const { user, logout } = useAuth();

  if (!user) {
    return null;
  }

  return (
    <div style={{ 
      maxWidth: '600px', 
      margin: '2rem auto', 
      padding: '1.5rem',
      border: '1px solid #ccc',
      borderRadius: '8px',
      backgroundColor: '#f9f9f9'
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
        onClick={logout}
        style={{
          padding: '0.75rem 1.5rem',
          fontSize: '1rem',
          backgroundColor: '#dc3545',
          color: 'white',
          border: 'none',
          borderRadius: '4px',
          cursor: 'pointer',
        }}
      >
        Logout
      </button>
    </div>
  );
};
