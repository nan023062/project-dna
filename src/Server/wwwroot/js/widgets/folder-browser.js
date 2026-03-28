/**
 * FolderBrowser — 可复用的文件夹浏览器组件
 *
 * 用法：
 *   import { FolderBrowser } from './widgets/folder-browser.js';
 *   const browser = new FolderBrowser({
 *     title: '选择文件夹',
 *     onConfirm: (path) => { ... }
 *   });
 *   browser.open('/initial/path');
 */

import { escapeAttr } from '../utils.js';

let _instanceCounter = 0;

export class FolderBrowser {
  /**
   * @param {Object} opts
   * @param {string}   opts.title      - 弹窗标题
   * @param {Function} opts.onConfirm  - 确认回调 (path) => void
   * @param {string}   [opts.rootPath] - 根路径约束，设置后不能导航到此目录之外
   */
  constructor({ title = '选择文件夹', onConfirm, rootPath = '' }) {
    this._id = 'folderBrowser_' + (++_instanceCounter);
    this._currentPath = '';
    this._rootPath = '';
    this._onConfirm = onConfirm;
    this._title = title;
    this._el = this._createDOM();
    document.body.appendChild(this._el);
    if (rootPath) this.setRootPath(rootPath);
  }

  setRootPath(rootPath) {
    this._rootPath = rootPath.replace(/[\\/]+$/, '');
  }

  _createDOM() {
    const overlay = document.createElement('div');
    overlay.className = 'modal-overlay';
    overlay.id = this._id;
    overlay.addEventListener('click', (e) => {
      if (e.target === overlay) this.close();
    });

    overlay.innerHTML = `
      <div class="modal">
        <div class="modal-header">
          <h3>${this._title}</h3>
          <button class="close-btn">&times;</button>
        </div>
        <div class="modal-breadcrumb" data-role="crumb"></div>
        <div class="modal-body" data-role="body"></div>
        <div class="modal-footer">
          <input type="text" data-role="pathInput" placeholder="当前路径" readonly />
          <button class="btn btn-primary btn-sm" data-role="confirmBtn">选择此目录</button>
        </div>
      </div>`;

    overlay.querySelector('.close-btn').addEventListener('click', () => this.close());
    overlay.querySelector('[data-role="confirmBtn"]').addEventListener('click', () => this._confirm());

    return overlay;
  }

  _q(role) {
    return this._el.querySelector(`[data-role="${role}"]`);
  }

  async open(initialPath = '') {
    this._el.classList.add('open');
    await this.browseTo(initialPath || this._rootPath || '');
  }

  close() {
    this._el.classList.remove('open');
  }

  _confirm() {
    if (this._onConfirm) this._onConfirm(this._currentPath);
    this.close();
  }

  _isWithinRoot(targetPath) {
    if (!this._rootPath) return true;
    if (targetPath === '__drives__') return false;
    const norm = p => p.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase();
    return norm(targetPath).startsWith(norm(this._rootPath));
  }

  async browseTo(path) {
    if (!this._isWithinRoot(path)) {
      path = this._rootPath;
    }

    const bodyEl = this._q('body');
    const crumbEl = this._q('crumb');
    const pathInput = this._q('pathInput');

    try {
      const url = '/api/browse' + (path ? '?path=' + encodeURIComponent(path) : '');
      const res = await fetch(url);
      const data = await res.json();
      if (data.error) return;

      if (!this._isWithinRoot(data.current)) {
        return this.browseTo(this._rootPath);
      }

      this._currentPath = data.current;
      pathInput.value = data.current;

      crumbEl.innerHTML = this._renderBreadcrumb(data);
      bodyEl.innerHTML = this._renderEntries(data);

      bodyEl.querySelectorAll('[data-nav]').forEach(el => {
        el.addEventListener('click', () => this.browseTo(el.dataset.nav));
      });
    } catch (e) {
      bodyEl.innerHTML = `<div style="padding:16px;color:#ef4444">${e.message}</div>`;
    }
  }

  _renderBreadcrumb(data) {
    if (data.atDriveList) {
      return '<span style="color:#e2e8f0">\u{1F4BB} 我的电脑</span>';
    }

    const parts = data.current.replace(/\\/g, '/').split('/').filter(Boolean);
    let html = '';

    if (!this._rootPath) {
      html += `<span class="crumb" data-crumb-nav="__drives__">\u{1F4BB}</span><span>/</span>`;
    }

    const rootParts = this._rootPath
      ? this._rootPath.replace(/\\/g, '/').split('/').filter(Boolean)
      : [];

    let accumulated = '';
    for (let i = 0; i < parts.length; i++) {
      accumulated += parts[i] + '/';
      const full = accumulated.replace(/\/$/, '');

      const isLocked = this._rootPath && i < rootParts.length;
      const isLast = i === parts.length - 1;

      if (isLast) {
        html += `<span style="color:#e2e8f0">${parts[i]}</span>`;
      } else if (isLocked) {
        html += `<span style="color:#4a4a6a">${parts[i]}</span><span>/</span>`;
      } else {
        html += `<span class="crumb" data-crumb-nav="${escapeAttr(full)}">${parts[i]}</span><span>/</span>`;
      }
    }

    setTimeout(() => {
      this._el.querySelectorAll('[data-crumb-nav]').forEach(el => {
        el.addEventListener('click', () => this.browseTo(el.dataset.crumbNav));
      });
    }, 0);

    return html;
  }

  _renderEntries(data) {
    let html = '';

    if (data.atDriveList) {
      for (const e of data.entries) {
        html += `<div class="dir-item" data-nav="${escapeAttr(e.path)}" style="font-weight:600">
          <span class="icon">\u{1F4BE}</span>
          <span>${e.name}</span>
        </div>`;
      }
      return html;
    }

    const norm = p => p.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase();
    const atRoot = this._rootPath && norm(data.current) === norm(this._rootPath);

    if (atRoot) {
      // already at root constraint
    } else if (this._rootPath) {
      if (data.parent && this._isWithinRoot(data.parent)) {
        html += `<div class="dir-item up" data-nav="${escapeAttr(data.parent)}"><span class="icon">⬆</span> ..</div>`;
      } else {
        html += `<div class="dir-item up" data-nav="${escapeAttr(this._rootPath)}"><span class="icon">⬆</span> ..</div>`;
      }
    } else if (data.atDriveRoot) {
      html += `<div class="dir-item up" data-nav="__drives__"><span class="icon">\u{1F4BB}</span> 所有盘符…</div>`;
    } else if (data.parent) {
      html += `<div class="dir-item up" data-nav="${escapeAttr(data.parent)}"><span class="icon">⬆</span> ..</div>`;
    }

    for (const e of data.entries) {
      html += `<div class="dir-item" data-nav="${escapeAttr(e.path)}">
        <span class="icon">\u{1F4C1}</span>
        <span>${e.name}</span>
        
      </div>`;
    }

    if (!data.entries.length && !data.parent && !data.atDriveRoot) {
      html = '<div style="padding:16px;color:#4a4a6a;text-align:center">空目录</div>';
    }

    return html;
  }

  destroy() {
    this._el.remove();
  }
}
