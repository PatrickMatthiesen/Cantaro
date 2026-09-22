import { createContext, useContext, useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import { authApi } from '@cantaro/client-shared/auth';
import type { User, RegisterRequest, LoginRequest, ThemePreference } from '@cantaro/client-shared/auth';
import { router } from '../router';

interface AuthContextType {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (request: LoginRequest) => Promise<void>;
  register: (request: RegisterRequest) => Promise<void>;
  logout: () => Promise<void>;
  logoutError: string | null;
  setProfile: (user: User) => void;
  refreshProfile: () => Promise<User>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

// eslint-disable-next-line react-refresh/only-export-components
export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};

interface AuthProviderProps {
  children: ReactNode;
}

export const AuthProvider = ({ children }: AuthProviderProps) => {
  const [user, setUser] = useState<User | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [logoutError, setLogoutError] = useState<string | null>(null);

  useEffect(() => {
    const loadUser = async () => {
      try {
        const currentUser = await authApi.getCurrentUser();
        setUser(currentUser);
      } catch {
        // User is not authenticated or session expired
        console.log('No active session');
      }
      setIsLoading(false);
    };

    loadUser();
  }, []);

  const login = async (request: LoginRequest) => {
    await authApi.login(request);
    const currentUser = await authApi.getCurrentUser();
    setUser(currentUser);
    await router.invalidate();
  };

  const register = async (request: RegisterRequest) => {
    await authApi.register(request);
  };

  const logout = async () => {
    setLogoutError(null);
    try {
      await authApi.logout();
    } catch {
      setLogoutError('Could not sign out. Please try again.');
      return;
    }
    setUser(null);
    await router.invalidate();
  };

  const setProfile = (profile: User) => setUser(profile);
  const refreshProfile = async () => {
    const profile = await authApi.getCurrentUser();
    setUser(profile);
    return profile;
  };

  useEffect(() => {
    const theme = user?.preferences.theme ?? (localStorage.getItem('cantaro-theme') as ThemePreference | null) ?? 'system';
    localStorage.setItem('cantaro-theme', theme);
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const applyTheme = () => {
      const resolved = theme === 'system' ? (media.matches ? 'dark' : 'light') : theme;
      document.documentElement.dataset.theme = resolved;
      document.documentElement.style.colorScheme = resolved;
    };
    applyTheme();
    media.addEventListener('change', applyTheme);
    return () => media.removeEventListener('change', applyTheme);
  }, [user?.preferences.theme]);

  const value: AuthContextType = {
    user,
    isAuthenticated: !!user,
    isLoading,
    login,
    register,
    logout,
    logoutError,
    setProfile,
    refreshProfile,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};
