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

async function readApiError(response: Response, fallbackMessage: string): Promise<string> {
  const error = await response.json().catch(() => ({ message: fallbackMessage }));
  return error.message || fallbackMessage;
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
      throw new Error(await readApiError(response, 'Registration failed'));
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
      throw new Error(await readApiError(response, 'Login failed'));
    }
  }

  async logout(): Promise<void> {
    const response = await fetch(`/api/logout`, {
      method: 'POST',
      headers: this.getHeaders(),
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response, 'Logout failed'));
    }
  }

  async getCurrentUser(): Promise<User> {
    const response = await fetch(`/api/auth/me`, {
      method: 'GET',
      headers: this.getHeaders(),
      credentials: 'include',
    });

    if (!response.ok) {
      throw new Error(await readApiError(response, 'Failed to fetch user'));
    }

    return response.json();
  }
}

export const authApi = new AuthApiClient();
