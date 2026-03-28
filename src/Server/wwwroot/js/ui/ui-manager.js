/**
 * UIManager — 界面管理器
 *
 * 管理整个左半屏的 UI 层级：
 *   z-0  FullscreenPanel（全屏界面 + Tab 页签）
 *   z-1  DialogStack（弹窗栈，由当前 Tab 管理）
 *   z-2  TipsLayer（悬浮提示，最高层级，不可交互）
 *
 * 右半屏 ChatPanel 独立于此管理器。
 */

import { FullscreenPanel } from './fullscreen-panel.js';
import { DialogStack } from './dialog-stack.js';
import { TipsLayer } from './tips-layer.js';

class UIManager {
  constructor() {
    this._fullscreen = null;
    this._dialogStack = null;
    this._tipsLayer = null;
    this._initialized = false;
  }

  init(mainAreaEl) {
    if (this._initialized) return;
    this._initialized = true;

    this._fullscreen = new FullscreenPanel(mainAreaEl);
    this._dialogStack = new DialogStack(mainAreaEl);
    this._tipsLayer = new TipsLayer(document.body);

    document.addEventListener('click', () => this._tipsLayer.hideAll());
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') {
        if (this._tipsLayer.hasActive()) {
          this._tipsLayer.hideAll();
        } else if (this._dialogStack.hasActive()) {
          this._dialogStack.closeTop();
        }
      }
    });
  }

  get fullscreen() { return this._fullscreen; }
  get dialogs() { return this._dialogStack; }
  get tips() { return this._tipsLayer; }

  switchTab(tabId) {
    this._tipsLayer.hideAll();
    this._dialogStack.closeAll();
    this._fullscreen.switchTab(tabId);
  }

  get activeTabId() {
    return this._fullscreen?.activeTabId ?? null;
  }
}

export const ui = new UIManager();
