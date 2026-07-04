export interface User {
  id: number;
  email: string;
  displayName: string;
  avatarUrl?: string;
  createdAt: string;
  preferences: ProfilePreferences;
}

export type ThemePreference = 'system' | 'light' | 'dark';

export interface ProfilePreferences {
  theme: ThemePreference;
  notifyOnSyncSuccess: boolean;
  notifyOnSyncFailure: boolean;
  notifyOnMediaReview: boolean;
  keepPlaylistOrder: boolean;
  keepPlaylistMetadata: boolean;
  hideUnavailableTracks: boolean;
  scheduledSync: boolean;
  blurEmailAddress: boolean;
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
  return error.error || error.message || fallbackMessage;
}

interface AuthRequestOptions {
  method: 'GET' | 'POST' | 'PUT' | 'DELETE';
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
    const response = await this.request('/api/profile', {
      method: 'GET',
      fallbackMessage: 'Failed to fetch user',
    });

    return response.json();
  }

  async updateProfile(displayName: string): Promise<User> {
    const response = await this.request('/api/profile', { method: 'PUT', fallbackMessage: 'Failed to update profile', body: { displayName } });
    return response.json();
  }

  async updatePreferences(preferences: ProfilePreferences): Promise<User> {
    const response = await this.request('/api/profile/preferences', { method: 'PUT', fallbackMessage: 'Failed to update preferences', body: preferences });
    return response.json();
  }

  async uploadAvatar(avatar: Blob): Promise<User> {
    const form = new FormData();
    const extension = avatar.type === 'image/jpeg' ? 'jpg' : avatar.type === 'image/png' ? 'png' : 'webp';
    form.append('avatar', avatar, `avatar.${extension}`);
    const response = await fetch('/api/profile/avatar', { method: 'POST', credentials: 'include', body: form });
    if (!response.ok) throw new Error(await readApiError(response, 'Failed to upload avatar'));
    return response.json();
  }

  async deleteAvatar(): Promise<User> {
    const response = await this.request('/api/profile/avatar', { method: 'DELETE', fallbackMessage: 'Failed to remove avatar' });
    return response.json();
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await this.request('/api/profile/change-password', { method: 'POST', fallbackMessage: 'Failed to change password', body: { currentPassword, newPassword } });
  }

  async deleteAccount(currentPassword: string): Promise<void> {
    await this.request('/api/profile', { method: 'DELETE', fallbackMessage: 'Failed to delete account', body: { currentPassword } });
  }
}

export const authApi = new AuthApiClient();
