# Cantaro Web Frontend

Modern React + TypeScript interface for Cantaro built with Vite, Tailwind CSS 4, and Bun.

## Requirements

- [Bun](https://bun.sh/) 1.3+
- Node.js (only needed for language server/tooling)

Install dependencies:

```bash
cd src/Cantaro.Web
bun install
```

## Scripts

```bash
# Start the dev server with HMR
bun dev

# Type-check and build for production
bun run build

# Preview the production build locally
bun run preview

# Lint source files
bun run lint
```

## Styling

- Tailwind CSS v4 with design tokens declared in `src/index.css`
- Components follow the Cantaro design language (dark mode by default)
- Avoid re-introducing inline styles; prefer semantic utility classes

## API access

Vite proxies `/api` requests to the API URL exposed by Aspire via environment variables (`API_HTTP` / `API_HTTPS`). See `vite.config.ts` for the proxy rules.
