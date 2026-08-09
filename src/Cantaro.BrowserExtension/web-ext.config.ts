import { defineWebExtConfig } from 'wxt';

export default defineWebExtConfig({
  // WXT builds and watches the extension; the chosen test browser owns its profile.
  disabled: true,
});
