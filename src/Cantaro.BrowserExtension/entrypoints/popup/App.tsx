import { FormEvent, useEffect, useRef, useState } from 'react';
import './App.css';

type StatusType = 'success' | 'error';

function App() {
  const [apiBaseUrl, setApiBaseUrl] = useState('');
  const [accessToken, setAccessToken] = useState('');
  const [status, setStatus] = useState<{ message: string; type: StatusType } | null>(null);
  const [loading, setLoading] = useState(true);
  const statusTimeout = useRef<number | null>(null);

  const showStatus = (message: string, type: StatusType) => {
    setStatus({ message, type });
    if (statusTimeout.current) {
      window.clearTimeout(statusTimeout.current);
    }
    statusTimeout.current = window.setTimeout(() => setStatus(null), 3000);
  };

  useEffect(() => {
    let mounted = true;

    browser
      .storage.local.get(['apiBaseUrl', 'accessToken'])
      .then((config: { apiBaseUrl?: string; accessToken?: string }) => {
        if (!mounted) {
          return;
        }
        setApiBaseUrl(config.apiBaseUrl ?? '');
        setAccessToken(config.accessToken ?? '');
      })
      .catch((error) => {
        console.error('Error loading settings:', error);
        showStatus('Failed to load settings', 'error');
      })
      .finally(() => {
        if (mounted) {
          setLoading(false);
        }
      });

    return () => {
      mounted = false;
      if (statusTimeout.current) {
        window.clearTimeout(statusTimeout.current);
      }
    };
  }, []);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    try {
      await browser.storage.local.set({
        apiBaseUrl: apiBaseUrl.trim(),
        accessToken: accessToken.trim() || null,
      });
      showStatus('Settings saved successfully!', 'success');
    } catch (error) {
      console.error('Error saving settings:', error);
      showStatus('Failed to save settings', 'error');
    }
  };

  return (
    <div className="popup-shell">
      <h1>Cantaro Settings</h1>
      <form className="settings-form" onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="apiBaseUrl">API Base URL</label>
          <input
            id="apiBaseUrl"
            type="url"
            placeholder="http://localhost:5000"
            required
            value={apiBaseUrl}
            onChange={(event) => setApiBaseUrl(event.target.value)}
            disabled={loading}
          />
          <div className="info">The URL of your Cantaro API instance</div>
        </div>

        <div className="form-group">
          <label htmlFor="accessToken">Authentication Token (Optional)</label>
          <input
            id="accessToken"
            type="password"
            placeholder="Your Cantaro auth token"
            value={accessToken}
            onChange={(event) => setAccessToken(event.target.value)}
            disabled={loading}
          />
          <div className="info">Get this from the Cantaro web app after logging in</div>
        </div>

        <button type="submit" disabled={loading}>
          Save Settings
        </button>
      </form>

      {status && (
        <div className={`status ${status.type}`} role="status" aria-live="polite">
          {status.message}
        </div>
      )}
    </div>
  );
}

export default App;
