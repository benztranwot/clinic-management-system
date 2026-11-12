// Remove entries written by older versions before Auth0 initializes its in-memory cache.
export function clearLegacyAuthSessionCache(browser: Window) {
  try {
    const storage = browser.sessionStorage;
    for (const key of Object.keys(storage)) {
      if (key.startsWith("clinic-auth:")) storage.removeItem(key);
    }
  } catch {
    // The app can sign in even when the browser blocks access to session storage.
  }
}
