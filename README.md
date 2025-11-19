# Cantaro

Cantaro is an open-source, self-hostable "music identity and playlist brain" that unifies your playlists and track mappings across multiple streaming services.

## Overview

Cantaro acts as the canonical vessel that holds your music graph and pours it into Spotify, YouTube/YouTube Music, and other platforms as synchronized playlists, while keeping your preferences, mappings, and tags under your control.

## Architecture

- **Backend**: ASP.NET Core 10 Web API (C#)
- **Frontend**: React + TypeScript + Vite
- **Database**: PostgreSQL
- **Orchestration**: .NET Aspire 13
- **Package Manager**: npm/bun

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [.NET Aspire 13](https://learn.microsoft.com/dotnet/aspire/)
- [Node.js](https://nodejs.org/) (LTS version recommended)
- [Docker](https://www.docker.com/) (for PostgreSQL and local development)

## Getting Started

### Installation

1. Clone the repository:
   ```bash
   git clone https://github.com/PatrickMatthiesen/Cantaro.git
   cd Cantaro
   ```

2. Install .NET Aspire templates (if not already installed):
   ```bash
   dotnet new install Aspire.ProjectTemplates
   ```

3. Install frontend dependencies:
   ```bash
   cd src/Cantaro.Web
   npm install
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
dotnet build Cantaro.sln
```

Build frontend for production:
```bash
cd src/Cantaro.Web
npm run build
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

### API Development

```bash
cd src/Cantaro.Api
dotnet watch run
```

### Frontend Development

```bash
cd src/Cantaro.Web
npm run dev
```

### Database Migrations

EF Core migrations are used to manage the database schema:

```bash
# Create a new migration
cd src/Cantaro.Api
dotnet ef migrations add MigrationName

# Apply migrations to the database
dotnet ef database update
```

When running locally with Aspire in Development mode, migrations are applied automatically on startup.

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
npm install
npm run dev              # For Chrome/Edge/Brave
npm run dev:firefox      # For Firefox
```

See [Cantaro.BrowserExtension/README.md](src/Cantaro.BrowserExtension/README.md) for detailed instructions on loading and using the extension.

## Contributing

Contributions are welcome! Please read our contributing guidelines before submitting PRs.

## License

(To be added)

## Acknowledgments

- Built with [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/)
- Uses [MusicBrainz](https://musicbrainz.org/) for music metadata
- Inspired by the need for vendor-independent music library management
