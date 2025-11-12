import browser from 'webextension-polyfill';

const form = document.getElementById('settingsForm') as HTMLFormElement;
const statusDiv = document.getElementById('status') as HTMLDivElement;
const apiBaseUrlInput = document.getElementById('apiBaseUrl') as HTMLInputElement;
const authTokenInput = document.getElementById('authToken') as HTMLInputElement;

// Load saved settings
loadSettings();

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  await saveSettings();
});

async function loadSettings() {
  try {
    const config = await browser.storage.local.get(['apiBaseUrl', 'authToken']);
    
    if (config.apiBaseUrl) {
      apiBaseUrlInput.value = config.apiBaseUrl;
    }
    
    if (config.authToken) {
      authTokenInput.value = config.authToken;
    }
  } catch (error) {
    console.error('Error loading settings:', error);
    showStatus('Failed to load settings', 'error');
  }
}

async function saveSettings() {
  try {
    const apiBaseUrl = apiBaseUrlInput.value.trim();
    const authToken = authTokenInput.value.trim();

    await browser.storage.local.set({
      apiBaseUrl,
      authToken: authToken || null,
    });

    showStatus('Settings saved successfully!', 'success');
    
    // Hide status after 3 seconds
    setTimeout(() => {
      statusDiv.style.display = 'none';
    }, 3000);
  } catch (error) {
    console.error('Error saving settings:', error);
    showStatus('Failed to save settings', 'error');
  }
}

function showStatus(message: string, type: 'success' | 'error') {
  statusDiv.textContent = message;
  statusDiv.className = `status ${type}`;
  statusDiv.style.display = 'block';
}
