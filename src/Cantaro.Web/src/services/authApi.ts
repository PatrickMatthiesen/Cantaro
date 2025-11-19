export interface User {
  id: number;
  email: string;
  createdAt: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

class AuthApiClient {
  private getHeaders(): HeadersInit {
    return {
      'Content-Type': 'application/json',
    };
  }

  async register(request: RegisterRequest): Promise<void> {
    const response = await fetch(`/api/register`, {
      method: 'POST',
      headers: this.getHeaders(),
      credentials: 'include',
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Registration failed' }));
      throw new Error(error.message || 'Registration failed');
    }
  }

  async login(request: LoginRequest): Promise<void> {
    const response = await fetch(`/api/login?useCookies=true`, {
      method: 'POST',
      headers: this.getHeaders(),
      credentials: 'include',
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Login failed' }));
      throw new Error(error.message || 'Login failed');
    }
  }

  async logout(): Promise<void> {
    const response = await fetch(`/api/logout`, {
      method: 'POST',
      headers: this.getHeaders(),
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Logout failed' }));
      throw new Error(error.message || 'Logout failed');
    }
  }

  async getCurrentUser(): Promise<User> {
    const response = await fetch(`/api/auth/me`, {
      method: 'GET',
      headers: this.getHeaders(),
      credentials: 'include',
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Failed to fetch user' }));
      throw new Error(error.message || 'Failed to fetch user');
    }

    return response.json();
  }
}

export const authApi = new AuthApiClient();
