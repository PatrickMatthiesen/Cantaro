# Cantaro

Cantaro is an open-source, self-hostable media and music library that unifies playlists and track mappings, tracks watched episodes, and helps users return to the right provider page.

## Overview

Cantaro acts as the canonical vessel that holds your music graph and pours it into Spotify, YouTube/YouTube Music, and other platforms as synchronized playlists, while keeping your preferences, mappings, and tags under your control.

## Architecture

- **Backend**: ASP.NET Core 10 Web API (C#)
- **Frontend**: React + TypeScript + Vite (Bun)
- **Extension**: React + TypeScript + WXT (Bun)
- **Database**: PostgreSQL
- **Orchestration**: .NET Aspire 13
- **Package Manager**: Bun

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [.NET Aspire 13](https://learn.microsoft.com/dotnet/aspire/)
- [Bun](https://bun.sh/) 1.3+
- [Node.js](https://nodejs.org/) (for tooling/VS Code integrations)
- [Docker](https://www.docker.com/) (for PostgreSQL and local development)

## Getting Started

### Option 1: GitHub Codespaces (Recommended for Quick Start)

The easiest way to get started with Cantaro is using GitHub Codespaces, which provides a pre-configured development environment in your browser:

1. Click the **Code** button on the GitHub repository
2. Select **Codespaces** tab
3. Click **Create codespace on main** (or your desired branch)

The environment will automatically install all required dependencies:
- .NET 10 SDK
- Node.js 24
- Bun 1.3
- .NET Aspire CLI
- EF Core tools
- Docker support
- All Bun-managed dependencies

Once the setup is complete, you can start developing immediately!

### Option 2: Local Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/PatrickMatthiesen/Cantaro.git
   cd Cantaro
   ```

2. Install the required tools

   Either run the [post-create script](.devcontainer/post-create.sh) or install manually:

   - [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
   - [.NET Aspire 13](https://aspire.dev/)
   - [Node.js](https://nodejs.org/) (Latest version recommended)
   - [Bun](https://bun.sh/) 1.3+
   - Container runtime supported by Aspire, for example:
      - [Docker](https://www.docker.com/) (for PostgreSQL)
      - [Podman](https://podman.io/) (alternative to Docker)

2. Install Aspire
   ```bash
   curl -sSL https://aspire.dev/install.sh | bash
   ```

3. Install frontend dependencies:
   ```bash
   cd src/Cantaro.Web
   bun install
   cd ../..
   ```

### Running Locally with Aspire

The easiest way to run Cantaro locally is using .NET Aspire, which orchestrates all services:

```bash
cd src/Cantaro.AppHost
dotnet run
```

This will:
- Start the PostgreSQL database with pgAdmin
- Start the Cantaro API
- Start the frontend development server
- Open the Aspire dashboard where you can monitor all services

Access the services:
- **Aspire Dashboard**: Check console output for URL (typically https://localhost:17XXX)
- **API**: Will be available on a port shown in the Aspire dashboard
- **Frontend**: Will be available on a port shown in the Aspire dashboard
- **pgAdmin**: Will be available on a port shown in the Aspire dashboard

### Building for Production

Build all projects:
```bash
aspire do build
```

Build frontend for production:
```bash
cd src/Cantaro.Web
bun run build
```

### Deployment

Manually running **Deploy Cantaro** from GitHub Actions publishes the Aspire Docker Compose model, connects the runner to the tailnet, and uses Tailscale SSH to run `aspire deploy` against Docker on the remote host.
The host must have Docker 28+ with Docker Compose, and the tailnet policy must allow `tag:ci` to reach it over Tailscale SSH.

In production, the frontend is built and copied into the API container, which serves both the UI and the `/api` routes. Aspire publishes that container on host port `7689`; configure the reverse proxy to forward the site to `http://<docker-host>:7689` and terminate HTTPS at the proxy.

Configure the OAuth applications with callback URLs based on the public HTTPS origin served by that proxy (replace `cantaro.example.com` with the real hostname):

- YouTube/Google: `https://cantaro.example.com/api/platforms/youtube/callback`
- Spotify: `https://cantaro.example.com/api/platforms/spotify/callback`
- AniList: `https://cantaro.example.com/api/media/providers/anilist/callback`

The redirect URLs must match exactly, including the `https` scheme and path.
Set the `CANTARO_HTTPS_BASE_URL` GitHub environment variable to the public
HTTPS origin, for example `https://cantaro.example.com`. Cantaro uses that
global origin when generating OAuth callbacks behind the HTTP reverse proxy.

For Spotify development, register
`https://cantaro.dev.localhost:5173/api/platforms/spotify/callback` in the
Spotify app. Aspire serves the frontend at the matching HTTPS origin using its
trusted development certificate. The integration uses the secure-backend
Authorization Code flow and requests only
`playlist-read-private`, `playlist-read-collaborative`, and
`user-read-private`; it never requests email access. Configure
`SpotifyClientId` and `SpotifyClientSecret` as Aspire secrets.

The current Spotify integration is deliberately import-only. It refreshes
provider observations when a playlist is imported again, keeps canonical
Cantaro tracks separate from temporary Spotify metadata, attributes linked
Spotify artwork and content in the UI, and deletes the connected user's
Spotify account, imported playlists, mappings, and orphaned Spotify provider
data on disconnect. Pushing Spotify-derived content to another provider is
out of scope until that workflow receives a separate policy review.

Normal CI uses representative HTTP fixtures and does not require Spotify
credentials. A live smoke test must use a Spotify Development Mode app whose
owner has Premium and whose test users are allowlisted; current limits vary by
quota mode, so consult Spotify's current migration and changelog documentation
instead of assuming Premium is required for every end user.

Create a GitHub environment named `Production` and add these environment secrets:

- `SSH_HOST` and `SSH_USER`
- `TS_OAUTH_CLIENT_ID` and `TS_OAUTH_SECRET`
- `CANTARO_POSTGRES_PASSWORD`
- `CANTARO_YOUTUBE_CLIENT_ID` and `CANTARO_YOUTUBE_CLIENT_SECRET`
- `CANTARO_SPOTIFY_CLIENT_ID` and `CANTARO_SPOTIFY_CLIENT_SECRET`
- `CANTARO_ANILIST_CLIENT_ID` and `CANTARO_ANILIST_CLIENT_SECRET`
- `CANTARO_EXTENSION_AUTH_JWT_SIGNING_KEY`

Add the environment variable `CANTARO_HTTPS_BASE_URL` with the public HTTPS
origin, for example `https://cantaro.example.com`.

Generate the JWT signing key from at least 32 bytes of cryptographically secure random data; do not use a password or memorable phrase. For example:

```bash
openssl rand -base64 32
```

Copy the complete output into `CANTARO_EXTENSION_AUTH_JWT_SIGNING_KEY` without committing it to the repository. Keep the value stable between deployments; replacing it invalidates outstanding browser-extension authorization tokens.

## Project Structure

```
Cantaro/
├── docs/                           # Documentation
│   ├── infrastructure.md          # Architecture overview
│   └── agent-extra.md             # Additional specifications
├── src/
│   ├── Cantaro.Api/               # ASP.NET Core Web API
│   ├── Cantaro.AppHost/           # Aspire orchestration
│   ├── Cantaro.BrowserExtension/  # WXT browser extension
│   ├── Cantaro.ServiceDefaults/   # Shared Aspire service defaults
│   └── Cantaro.Web/               # React frontend
└── Cantaro.sln                    # Solution file
```

## Core Features (Planned)

- 🎵 **Unified Playlist Management**: Create and manage playlists in one place
- 🔄 **Cross-Platform Sync**: Sync playlists across Spotify, YouTube Music, and more
- 🎯 **Smart Track Mapping**: Use MusicBrainz IDs, ISRCs, and intelligent matching
- 🔐 **Privacy-Respecting**: Self-hostable with encrypted OAuth tokens
- 🌐 **Open Source**: Built on open standards and open data

## Development

### Database Migrations

EF Core migrations are used to manage the database schema:

```bash
# Create a new migration
cd src/Cantaro.Api
dotnet ef migrations add MigrationName

# Apply migrations to the database
dotnet ef database update
```

When running locally with Aspire in Development mode, migrations are applied automatically on startup. Meaning you just need to create the migration and restart the API.

### Authentication

Cantaro uses secure cookie-based authentication for user accounts:

- **Register**: POST `/api/register` with `{ "email": "user@example.com", "password": "yourpassword" }`
- **Login**: POST `/api/login?useCookies=true` with `{ "email": "user@example.com", "password": "yourpassword" }`
- **Logout**: POST `/api/logout` to invalidate the session
- **Get Current User**: GET `/api/auth/me` (authentication cookie sent automatically)

Authentication uses httpOnly cookies with the following security features:
- **HttpOnly**: Prevents JavaScript access (XSS protection)
- **SameSite=Lax**: CSRF protection
- **Secure**: HTTPS-only in production
- **14-day expiration**: With sliding window refresh

The browser automatically includes the authentication cookie in all API requests—no manual token management required.

### Browser Extension Development

The optional browser extension accelerates playlist sync and observes supported media pages in real time:

The extension's data handling is described in the [browser extension privacy policy](./PRIVACY.md).

```bash
cd src/Cantaro.BrowserExtension
bun install
bun run dev              # Build and watch the Chrome extension
bun run dev:firefox      # Build and watch the Firefox extension
```

WXT does not launch a browser. Chrome development output is written to
`src/Cantaro.BrowserExtension/.output/chrome-mv3-dev`; load that directory as
an unpacked extension in the browser profile used for testing. Chrome DevTools
MCP can install and reload it in its persistent managed profile, so extension
development needs only that one Chrome window.

#### Crunchyroll episode URL collection

When the extension is enabled, connected to a Cantaro instance, and the user visits Crunchyroll, it collects episode destination URLs from the page that the user is already viewing. On a watch page this includes the current episode and a rendered next-episode link. On a series page it collects the episode ID, URL, number, and title for every episode card rendered in the currently selected season. If the user changes seasons, the newly rendered season can be collected as another batch.

This is passive page observation: Cantaro does not click through the season selector, crawl Crunchyroll in the background, call private Crunchyroll APIs, download video or subtitle content, or read Crunchyroll credentials or cookies. It sends only the rendered provider identifiers, destination URLs, titles/numbers, and normal watch-progress observation data to the user's configured Cantaro server.

If a Crunchyroll series page does not expose a usable title, selected season, or safe rendered episode URLs after five seconds, the collector writes a warning to that page's browser console with the relevant DOM counts and failure reason. Delivery results distinguish batches accepted by the API, batches safely queued while signed out or offline, rejected batches, and successful `pending_match` catalog evidence. Catalog matching never opens the watch-progress resolution UI.

Enable **Verbose logging** in the extension settings to additionally log each series-page scan, extraction diagnostics, collected episode IDs and URLs, duplicate fingerprints, and the result returned by the extension background service. Warnings and errors remain enabled even when verbose logging is off.

After an observation has been matched or explicitly resolved to the correct Cantaro media title, its validated episode destinations become shared catalog data inside that Cantaro instance. Other users of the same instance can therefore receive a direct **Continue watching** link without storing duplicate copies of the URL. Cantaro accepts only HTTPS URLs on the exact `crunchyroll.com` or `www.crunchyroll.com` hosts, records how many times a destination has been seen, and marks conflicting mappings instead of silently redirecting them.

The extension keeps extraction and tracking state inside each provider tab. The popup asks only the active tab for its current context, while the background worker is a stateless authenticated delivery router with a bounded offline queue. See the extension [architecture](src/Cantaro.BrowserExtension/ARCHITECTURE.md) and [permission rationale](src/Cantaro.BrowserExtension/PERMISSIONS.md).

## Contributing

Contributions are welcome! Please read our contributing guidelines before submitting PRs.

## License

This project is licensed under the Apache 2.0 License. See the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Built with [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/)
- Uses [MusicBrainz](https://musicbrainz.org/) for music metadata
- Inspired by the need for vendor-independent music library management
