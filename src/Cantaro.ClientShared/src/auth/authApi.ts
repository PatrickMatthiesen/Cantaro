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

interface AuthRequestOptions {
  method: 'GET' | 'POST';
  fallbackMessage: string;
  body?: unknown;
}

class AuthApiClient {
  private getHeaders(): HeadersInit {
    return {
      'Content-Type': 'application/json',
    };
  }

  private async request(path: string, { method, fallbackMessage, body }: AuthRequestOptions): Promise<Response> {
    const response = await fetch(path, {
      method,
      headers: this.getHeaders(),
      credentials: 'include',
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    if (!response.ok) {
      throw new Error(await readApiError(response, fallbackMessage));
    }

    return response;
  }

  async register(request: RegisterRequest): Promise<void> {
    await this.request('/api/register', {
      method: 'POST',
      fallbackMessage: 'Registration failed',
      body: request,
    });
  }

  async login(request: LoginRequest): Promise<void> {
    await this.request('/api/login?useCookies=true', {
      method: 'POST',
      fallbackMessage: 'Login failed',
      body: request,
    });
  }

  async logout(): Promise<void> {
    await this.request('/api/logout', {
      method: 'POST',
      fallbackMessage: 'Logout failed',
    });
  }

  async getCurrentUser(): Promise<User> {
    const response = await this.request('/api/auth/me', {
      method: 'GET',
      fallbackMessage: 'Failed to fetch user',
    });

    return response.json();
  }
}

export const authApi = new AuthApiClient();
