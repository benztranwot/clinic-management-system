import type { ICache } from "@auth0/auth0-react";

const prefix = "clinic-auth:";

// Keep the SDK cache through reloads in this tab. Auth0 owns token renewal and logout.
export function createAuthSessionCache(browser: Window): ICache | undefined {
  let storage: Storage;
  try {
    storage = browser.sessionStorage;
    storage.setItem(prefix + "probe", "1");
    storage.removeItem(prefix + "probe");
  } catch {
    return undefined;
  }

  return {
    get<T>(key: string): T | undefined {
      const item = storage.getItem(prefix + key);
      if (item === null) return undefined;

      try {
        return JSON.parse(item) as T;
      } catch {
        storage.removeItem(prefix + key);
        return undefined;
      }
    },
    set<T>(key: string, entry: T) {
      storage.setItem(prefix + key, JSON.stringify(entry));
    },
    remove(key: string) {
      storage.removeItem(prefix + key);
    },
    allKeys() {
      return Object.keys(storage).filter((key) => key.startsWith(prefix))
        .map((key) => key.slice(prefix.length));
    },
  };
}
