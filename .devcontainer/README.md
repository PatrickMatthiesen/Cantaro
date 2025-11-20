# Dev Container Configuration

This directory contains the configuration for GitHub Codespaces and VS Code Dev Containers.

## What's Included

The dev container is configured to match the agent setup workflow and includes:

- **Base Image**: Official Microsoft .NET 10.0 dev container
- **Node.js 24**: For frontend and browser extension development
- **Docker-in-Docker**: For running PostgreSQL and other containerized services
- **Aspire CLI**: For orchestrating the application services
- **EF Core Tools**: For database migrations
- **VS Code Extensions**: C#, ESLint, Prettier, and more

## Post-Create Setup

The `post-create.sh` script automatically runs after the container is created and:

1. Installs Aspire CLI
2. Installs EF Core tools globally
3. Restores .NET dependencies
4. Installs npm dependencies for the web frontend
5. Installs npm dependencies for the browser extension

## Port Forwarding

The following ports are automatically forwarded:

- **5000**: Cantaro API
- **5173**: Frontend dev server (Vite)
- **5432**: PostgreSQL database
- **8080**: Aspire dashboard

## Usage

### In GitHub Codespaces

1. Go to the repository on GitHub
2. Click **Code** → **Codespaces** → **Create codespace**
3. Wait for the environment to be set up (this may take a few minutes on first run)
4. Start developing!

### In VS Code

1. Install the "Dev Containers" extension
2. Open the repository in VS Code
3. Press `F1` and select "Dev Containers: Reopen in Container"
4. Wait for the container to build and the post-create script to run

## Running Cantaro

Once the dev container is ready, start the application with:

```bash
cd src/Cantaro.AppHost
dotnet run
```

This will launch the Aspire dashboard where you can monitor all services.

## Maintenance

This configuration is kept in sync with `.github/workflows/copilot-setup-steps.yml` to ensure consistency between the agent development environment and Codespaces.
