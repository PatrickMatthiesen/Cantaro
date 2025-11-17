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

export interface AuthResponse {
  token: string;
  user: User;
}

class AuthApiClient {
  private getHeaders(includeAuth: boolean = false): HeadersInit {
    const headers: HeadersInit = {
      'Content-Type': 'application/json',
    };

    if (includeAuth) {
      const token = localStorage.getItem('authToken');
      if (token) {
        headers['Authorization'] = `Bearer ${token}`;
      }
    }

    return headers;
  }

  async register(request: RegisterRequest): Promise<AuthResponse> {
    const response = await fetch(`/api/auth/register`, {
      method: 'POST',
      headers: this.getHeaders(),
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Registration failed' }));
      throw new Error(error.message || 'Registration failed');
    }

    return response.json();
  }

  async login(request: LoginRequest): Promise<AuthResponse> {
    const response = await fetch(`/api/auth/login`, {
      method: 'POST',
      headers: this.getHeaders(),
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Login failed' }));
      throw new Error(error.message || 'Login failed');
    }

    return response.json();
  }

  async getCurrentUser(): Promise<User> {
    const response = await fetch(`/api/auth/me`, {
      method: 'GET',
      headers: this.getHeaders(true),
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Failed to fetch user' }));
      throw new Error(error.message || 'Failed to fetch user');
    }

    return response.json();
  }
}

export const authApi = new AuthApiClient();
