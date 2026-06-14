# Cantaro

This is a media and music library management application.

## Music

The goal for music in Cantaro is to have a unified experience across platforms and sync playlists and libraries across platforms, and to have a web interface for managing the library.
So if one platform is connected and a playlists is chosen to be synced, then that playlist will be synced to all other platforms that are connected. If a change is made on a platform, then the change will be synced back to Cantaro and out to all other platforms that are connected.

## Media (Movies, TV shows, Anime, etc.)

This is for tracking what episodes and movies have been watched. The browser extension injects into the streaming service and tracks what has been watched, and then syncs that back to Cantaro and out to a provider like Anilist or Trakt.
The web app then shows all the media based on what has been watched and what is in the library, and allows for searching and filtering of the media.

## Dev tools

We use Aspire for development, which is a tool that helps with running and managing the application.
We use dotnet for the backend and bun with Vite, React, TypeScript, Tailwind and Tanstack router for the frontend.

### Running the application

We use Aspire for development, use aspire start or stop to boot up the database, backend, and frontend. load the aspire skill if more info is needed on using aspire.
Aspire is using https and the test users credentials are in Cantaro.MigrationService\appsettings.Development.json

### Aspire publish output

The `aspire-output` folders are generated artifacts from `aspire publish` or `aspire deploy`. Do not hand-edit them as source; update the AppHost or deployment inputs and regenerate the output instead.

### Tests

If you run the dotnet tests, make sure that aspire is stopped as the processes otherwise hold a lock on files in the bin folder.

### Linting

For validating the frontend code, please use bun run check and before finishing use bun run check:fallow to find issues.
