# Cantaro

Cantaro is an open-source, self-hostable "music identity and playlist brain" that unifies your playlists and track mappings across multiple streaming services.

## Overview

Cantaro acts as the canonical vessel that holds your music graph and pours it into Spotify, YouTube/YouTube Music, and other platforms as synchronized playlists, while keeping your preferences, mappings, and tags under your control.

## Architecture

- **Backend**: ASP.NET Core 10 Web API (C#)
- **Frontend**: React + TypeScript + Vite (Node till bun works in aspire)
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

The optional browser extension accelerates playlist sync by detecting changes in real-time:

```bash
cd src/Cantaro.BrowserExtension
bun install
bun run dev              # For Chrome/Edge/Brave
bun run dev:firefox      # For Firefox
```

See [Cantaro.BrowserExtension/README.md](src/Cantaro.BrowserExtension/README.md) for detailed instructions on loading and using the extension.

## Contributing

Contributions are welcome! Please read our contributing guidelines before submitting PRs.

## License

This project is licensed under the Apache 2.0 License. See the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Built with [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/)
- Uses [MusicBrainz](https://musicbrainz.org/) for music metadata
- Inspired by the need for vendor-independent music library management
