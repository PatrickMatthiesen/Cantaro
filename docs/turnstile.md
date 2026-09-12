# Turnstile configuration

Cantaro supports optional Cloudflare Turnstile protection for browser sign-in and account registration. The extension's background requests, token refresh, watch tracking, and provider sync requests do not require a Turnstile token.

The API reads the `Turnstile` configuration section:

```json
{
  "Turnstile": {
    "Mode": "Auto",
    "SiteKey": "your-public-site-key",
    "Secret": "your-server-side-secret",
    "AllowedHostnames": ["cantaro.example.com"]
  }
}
```

`Auto` is the default. It keeps Turnstile disabled when all values are absent, enables it when all values are present, and fails startup when the section is only partially configured. `Enabled` requires all values. `Disabled` is an explicit opt-out for self-hosted deployments and ignores the other values.

The API sends each token to Cloudflare Siteverify and requires a successful response with the expected action and an allowed hostname. Store `Secret` in the deployment secret store. Do not put it in frontend configuration or source control. `AllowedHostnames` must contain the hostname shown in the Turnstile widget response for each deployment, without a scheme or path.

The API wiring is intentionally explicit because Cantaro uses a top-level `Program.cs`:

```csharp
builder.Services.AddTurnstile(builder.Configuration);
// after UseHttpsRedirection and before endpoint mappings:
app.UseTurnstileProtection();
```

The deployment can supply these values through environment variables such as `Turnstile__Mode`, `Turnstile__SiteKey`, `Turnstile__Secret`, and `Turnstile__AllowedHostnames__0`.
