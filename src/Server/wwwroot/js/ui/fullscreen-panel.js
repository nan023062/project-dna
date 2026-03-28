/**
 * FullscreenPanel — 全屏界面管理
 *
 * 同一时间只有一个全屏界面实例。
 * 全屏界面内部管理 Tab 页签，同一时间只有一个 Tab 激活。
 * "全屏"是相对的（只占除 Chat 窗口外的部分）。
 */

export class FullscreenPanel {
  constructor(containerEl) {
    this._container = containerEl;
    this._tabs = new Map();
    this._activeTabId = null;
    this._onTabChange = [];
  }

  get activeTabId() { return this._activeTabId; }

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
        if (prev.tabButtonEl) prev.tabButtonEl.classList.remove('active');
        if (prev.panelEl) prev.panelEl.classList.remove('active');
        try { prev.onDeactivate?.(); } catch (_) {}
      }
    }

    this._activeTabId = tabId;
    const next = this._tabs.get(tabId);
    if (next) {
      if (next.tabButtonEl) next.tabButtonEl.classList.add('active');
      if (next.panelEl) next.panelEl.classList.add('active');
      try { next.onActivate?.(); } catch (_) {}
    }

    for (const cb of this._onTabChange) {
      try { cb(tabId); } catch (_) {}
    }
  }
}
