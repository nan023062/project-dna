export class FullscreenPanel {
  constructor(containerEl) {
    this._container = containerEl;
    this._tabs = new Map();
    this._activeTabId = null;
    this._onTabChange = [];
  }

  get activeTabId() {
    return this._activeTabId;
  }

  registerTab(tabId, { onActivate, onDeactivate, tabButtonEl, panelEl }) {
    this._tabs.set(tabId, { onActivate, onDeactivate, tabButtonEl, panelEl });
  }

  onTabChange(callback) {
    this._onTabChange.push(callback);
  }

  switchTab(tabId) {
    if (this._activeTabId === tabId) return;

    if (this._activeTabId) {
      const prev = this._tabs.get(this._activeTabId);
      if (prev) {
        prev.tabButtonEl?.classList.remove('active');
        prev.panelEl?.classList.remove('active');
        try {
          prev.onDeactivate?.();
        } catch {
          // Keep tab switching resilient to panel teardown failures.
        }
      }
    }

    this._activeTabId = tabId;
    const next = this._tabs.get(tabId);
    if (next) {
      next.tabButtonEl?.classList.add('active');
      next.panelEl?.classList.add('active');
      try {
        next.onActivate?.();
      } catch {
        // Keep tab switching resilient to panel activation failures.
      }
    }

    for (const callback of this._onTabChange) {
      try {
        callback(tabId);
      } catch {
        // Ignore observer failures so one listener does not block the UI.
      }
    }
  }
}
