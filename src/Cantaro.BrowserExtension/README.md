# Cantaro Browser Extension

The Cantaro browser extension accelerates near-real-time playlist synchronization between your music services by detecting playlist-related events as you browse supported platforms.

## Features

- 🎵 **Event Detection**: Monitors playlist changes on Spotify, YouTube Music, and other supported platforms
- 🚀 **Real-time Sync**: Sends events to your Cantaro backend for immediate processing
- 🔐 **Privacy-First**: Uses your existing Cantaro authentication; never stores third-party OAuth tokens
- 🌐 **Multi-Browser**: Built with WXT for compatibility with Chromium and Firefox

## Tech Stack

- **Framework**: [WXT](https://wxt.dev/) - Modern web extension framework
- **Language**: TypeScript
- **Browser Support**: Chromium-based browsers (Chrome, Edge, Brave, etc.) and Firefox

## Prerequisites

- Node.js (LTS version recommended)
- npm or your preferred package manager
- A running Cantaro backend instance (see main [README](../../README.md))

## Development Setup

### 1. Install Dependencies

```bash
cd src/Cantaro.BrowserExtension
npm install
```

### 2. Configure API Connection

The extension needs to know where your Cantaro API is running. By default, it uses `http://localhost:5000` for local development, which matches the typical Aspire setup.

You can change this later in the extension's popup settings after it's loaded.

### 3. Start Development Mode

For Chromium-based browsers (Chrome, Edge, Brave, etc.):

```bash
npm run dev
```

For Firefox:

```bash
npm run dev:firefox
```

This will:
- Build the extension in development mode
- Watch for file changes and rebuild automatically
- Output the built extension to `.output/chrome-mv3` or `.output/firefox-mv2`

### 4. Load the Extension in Your Browser

#### Chrome/Edge/Brave

1. Open your browser's extensions page:
   - Chrome: `chrome://extensions`
   - Edge: `edge://extensions`
   - Brave: `brave://extensions`

2. Enable "Developer mode" (toggle in the top right)

3. Click "Load unpacked"

4. Select the directory: `src/Cantaro.BrowserExtension/.output/chrome-mv3`

#### Firefox

1. Open `about:debugging` in Firefox

2. Click "This Firefox" in the sidebar

3. Click "Load Temporary Add-on"

4. Navigate to `src/Cantaro.BrowserExtension/.output/firefox-mv2` and select any file (typically `manifest.json`)

### 5. Configure the Extension

1. Click the Cantaro extension icon in your browser toolbar

2. Enter your Cantaro API URL (e.g., `http://localhost:5000` for local development)

3. (Optional) Enter your authentication token from the Cantaro web app

4. Click "Save Settings"

## Building for Production

### Chromium

```bash
npm run build
```

This creates an unpacked extension in `.output/chrome-mv3`.

To create a distributable ZIP:

```bash
npm run zip
```

The ZIP file will be created in `.output/cantaro-browser-extension-X.X.X-chrome.zip`.

### Firefox

```bash
npm run build:firefox
```

To create a distributable ZIP:

```bash
npm run zip:firefox
```

The ZIP file will be created in `.output/cantaro-browser-extension-X.X.X-firefox.zip`.

## Project Structure

```
Cantaro.BrowserExtension/
├── entrypoints/
│   ├── background.ts           # Service worker/background script
│   ├── popup.html              # Extension popup UI
│   ├── popup.ts                # Popup logic
│   └── *.content.ts            # Content scripts for each platform
├── wxt.config.ts               # WXT configuration
├── tsconfig.json               # TypeScript configuration
├── package.json                # Dependencies and scripts
└── README.md                   # This file
```

## How It Works

1. **Content Scripts**: Run on supported music platform websites (Spotify, YouTube Music, etc.) and observe DOM changes to detect playlist events

2. **Background Script**: Receives events from content scripts and forwards them to the Cantaro backend API

3. **Popup UI**: Allows users to configure the API connection and authentication

4. **Event Flow**:
   ```
   Music Platform → Content Script → Background Script → Cantaro API
   ```

## Supported Platforms

Currently supported (or planned):
- ✅ Spotify (placeholder implementation)
- 🚧 YouTube Music (planned)
- 🚧 YouTube (planned)

## Configuration

The extension stores configuration in browser local storage:

- `apiBaseUrl`: The URL of your Cantaro backend API
- `authToken`: Optional authentication token for API requests

## Security & Privacy

- **No Third-Party Tokens**: The extension never stores Spotify, YouTube, or other service OAuth tokens
- **Local Storage Only**: Configuration is stored locally in your browser
- **Minimal Permissions**: Only requests permissions necessary for its function
- **Transparent Operation**: All code is open source and auditable

## Troubleshooting

### Extension doesn't load

- Make sure you've run `npm install` and `npm run dev`
- Check that you're loading the correct directory (`.output/chrome-mv3` or `.output/firefox-mv2`)
- Look for errors in the browser console

### Events not being sent

- Verify the API URL is correct in the extension popup
- Check that your Cantaro backend is running and accessible
- Open the browser console on the music platform page to see content script logs
- Open the extension's background page console (from the extensions page) to see background script logs

### Development changes not reflecting

- WXT should auto-reload, but you can manually reload the extension from the extensions page
- For content scripts, you may need to refresh the music platform page

## Contributing

Contributions are welcome! The extension is in early development, and many features are placeholders awaiting implementation.

Key areas for contribution:
- Implementing actual playlist event detection for Spotify, YouTube Music, etc.
- Adding support for additional music platforms
- Improving error handling and user feedback
- UI/UX enhancements

## License

(To be determined - should match the main Cantaro project)
