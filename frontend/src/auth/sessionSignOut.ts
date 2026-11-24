const signOutKey = "clinic-session:sign-out";

// Each write carries a new value so consecutive sign-outs notify other tabs.
export function broadcastSignOut(browser: Window) {
  try {
    browser.localStorage.setItem(signOutKey, browser.crypto.randomUUID());
  } catch {
    // Blocked storage must not prevent this tab from signing out.
  }
}

export function subscribeToSignOut(browser: Window, onSignOut: () => void) {
  function handleStorage(event: StorageEvent) {
    if (event.key === signOutKey && event.newValue
      && event.storageArea === browser.localStorage) {
      onSignOut();
    }
  }

  browser.addEventListener("storage", handleStorage);
  return () => browser.removeEventListener("storage", handleStorage);
}
