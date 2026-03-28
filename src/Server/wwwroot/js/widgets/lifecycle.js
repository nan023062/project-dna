/**
 * Panel 生命周期管理
 *
 * 核心原则：浮动 widget（tooltip、popwindow、sidebar）由所属 panel 拥有，
 * panel 切换（deactivate）或 DOM 重建时，所有子 widget 自动销毁。
 *
 * 用法：
 *   // app.js — 切换页签时驱动生命周期
 *   activatePanel('topology');
 *
 *   // widget 内部 — 打开时注册，关闭时注销
 *   const dispose = () => this.destroy();
 *   registerDisposable(dispose);       // 打开时
 *   unregisterDisposable(dispose);     // 正常关闭时
 *
 *   // tree-manager.js — DOM 重建前清理当前 panel 的所有浮动 widget
 *   disposeActiveWidgets();
 */

const _disposables = new Map();
let _activePanel = null;

export function getActivePanel() { return _activePanel; }

export function activatePanel(panelId) {
  if (_activePanel && _activePanel !== panelId) {
    _disposePanel(_activePanel);
  }
  _activePanel = panelId;
}

export function registerDisposable(disposeFn) {
  if (!_activePanel) return;
  if (!_disposables.has(_activePanel)) _disposables.set(_activePanel, new Set());
  _disposables.get(_activePanel).add(disposeFn);
}

export function unregisterDisposable(disposeFn) {
  if (!_activePanel) return;
  _disposables.get(_activePanel)?.delete(disposeFn);
}

export function disposeActiveWidgets() {
  if (_activePanel) _disposePanel(_activePanel);
}

function _disposePanel(panelId) {
  const fns = _disposables.get(panelId);
  if (!fns || fns.size === 0) return;
  for (const fn of fns) {
    try { fn(); } catch (_) { /* best effort */ }
  }
  fns.clear();
}
