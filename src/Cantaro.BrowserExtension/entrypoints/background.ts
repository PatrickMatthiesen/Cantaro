import browser from 'webextension-polyfill';

export default defineBackground(() => {
  console.log('Cantaro extension background script loaded');

  // Listen for messages from content scripts
  browser.runtime.onMessage.addListener(async (message, sender) => {
    console.log('Received message:', message, 'from:', sender.tab?.url);

    if (message.type === 'PLAYLIST_EVENT') {
      await handlePlaylistEvent(message.payload);
    }

    return { success: true };
  });

  // Initialize extension
  initialize();
});

async function initialize() {
  console.log('Initializing Cantaro extension');
  
  // Check if API base URL is configured
  const config = await browser.storage.local.get('apiBaseUrl');
  if (!config.apiBaseUrl) {
    console.log('API base URL not configured. Setting default for local development.');
    await browser.storage.local.set({
      apiBaseUrl: 'http://localhost:5000',
    });
  }
}

async function handlePlaylistEvent(payload: any) {
  console.log('Handling playlist event:', payload);

  try {
    // Get API configuration
    const config = await browser.storage.local.get(['apiBaseUrl', 'authToken']);
    
    if (!config.authToken) {
      console.warn('No auth token found. User needs to authenticate.');
      return;
    }

    // Send event to Cantaro backend
    const response = await fetch(`${config.apiBaseUrl}/api/extension/events`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${config.authToken}`,
      },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      console.error('Failed to send event to backend:', response.status, response.statusText);
    } else {
      console.log('Event successfully sent to backend');
    }
  } catch (error) {
    console.error('Error sending event to backend:', error);
  }
}
