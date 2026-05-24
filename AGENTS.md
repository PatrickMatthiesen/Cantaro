# Cantaro

This is a media and music library management application.

## Dev tools

We use dotnet for the backend and bun with Vite, React, TypeScript, Tailwind and Tanstack router for the frontend.

### Running the application

We use Aspire for development, use aspire start or stop do boot up the database, backend, and frontend. load the aspire skill if more info is needed on using aspire.

### Tests

If you run the dotnet tests, make sure that aspire is stopped as the processes otherwise hold a lock on files in the bin folder.

### Linting

For validating the frontend code, please use bun run check and before finishing use bun run check:fallow to find issues.
