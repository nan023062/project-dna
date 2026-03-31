export class DialogStack {
  constructor(containerEl) {
    this._container = containerEl;
    this._stack = [];
    this._overlayEl = null;
    this._ensureOverlay();
  }

  _ensureOverlay() {
    this._overlayEl = document.createElement('div');
    this._overlayEl.className = 'ui-dialog-overlay';
    this._overlayEl.style.display = 'none';
    this._overlayEl.addEventListener('click', event => {
      if (event.target === this._overlayEl) {
        this.closeTop();
      }
    });
    this._container.appendChild(this._overlayEl);
  }

  open(opts) {
    const existing = this._stack.find(dialog => dialog.id === opts.id);
    if (existing) {
      this._bringToTop(existing);
      return existing;
    }

    const dialogEl = document.createElement('div');
    dialogEl.className = `ui-dialog ${opts.className || ''}`;
    dialogEl.dataset.dialogId = opts.id;

    const headerEl = document.createElement('div');
    headerEl.className = 'ui-dialog-header';
    headerEl.innerHTML = `
      <span class="ui-dialog-title">${opts.title || ''}</span>
      <button class="ui-dialog-close">x</button>
    `;
    dialogEl.appendChild(headerEl);

    const bodyEl = document.createElement('div');
    bodyEl.className = 'ui-dialog-body';
    if (typeof opts.content === 'string') {
      bodyEl.innerHTML = opts.content;
    } else if (opts.content instanceof HTMLElement) {
      bodyEl.appendChild(opts.content);
    }
    dialogEl.appendChild(bodyEl);

    headerEl.querySelector('.ui-dialog-close').addEventListener('click', () => this.close(opts.id));

    const entry = {
      id: opts.id,
      el: dialogEl,
      bodyEl,
      onClose: opts.onClose,
      close: () => this.close(opts.id)
    };

    this._stack.push(entry);
    this._overlayEl.appendChild(dialogEl);
    this._updateVisibility();

    return entry;
  }

  close(dialogId) {
    const idx = this._stack.findIndex(dialog => dialog.id === dialogId);
    if (idx < 0) return;

    const entry = this._stack[idx];
    this._stack.splice(idx, 1);
    entry.el.remove();
    try {
      entry.onClose?.();
    } catch {
      // Ignore close handler errors so the stack can recover cleanly.
    }
    this._updateVisibility();
  }

  closeTop() {
    if (this._stack.length === 0) return;
    this.close(this._stack[this._stack.length - 1].id);
  }

  closeAll() {
    while (this._stack.length > 0) {
      this.closeTop();
    }
  }

  hasActive() {
    return this._stack.length > 0;
  }

  getDialog(dialogId) {
    return this._stack.find(dialog => dialog.id === dialogId) ?? null;
  }

  _bringToTop(entry) {
    const idx = this._stack.indexOf(entry);
    if (idx >= 0 && idx < this._stack.length - 1) {
      this._stack.splice(idx, 1);
      this._stack.push(entry);
      this._overlayEl.appendChild(entry.el);
    }
  }

  _updateVisibility() {
    this._overlayEl.style.display = this._stack.length > 0 ? 'flex' : 'none';
  }
}
