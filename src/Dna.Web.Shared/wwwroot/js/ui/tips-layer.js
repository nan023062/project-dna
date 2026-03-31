export class TipsLayer {
  constructor(containerEl) {
    this._container = containerEl;
    this._activeTip = null;
    this._tipEl = document.createElement('div');
    this._tipEl.className = 'ui-tips';
    this._tipEl.style.display = 'none';
    this._container.appendChild(this._tipEl);
  }

  show(content, x, y) {
    this._tipEl.innerHTML = content;
    this._tipEl.style.display = 'block';
    this._tipEl.style.left = `${x}px`;
    this._tipEl.style.top = `${y}px`;
    this._activeTip = { content, x, y };

    requestAnimationFrame(() => {
      const rect = this._tipEl.getBoundingClientRect();
      const vw = window.innerWidth;
      const vh = window.innerHeight;
      if (rect.right > vw) this._tipEl.style.left = `${vw - rect.width - 8}px`;
      if (rect.bottom > vh) this._tipEl.style.top = `${y - rect.height - 8}px`;
    });
  }

  showNear(content, targetEl) {
    const rect = targetEl.getBoundingClientRect();
    this.show(content, rect.left, rect.bottom + 4);
  }

  hideAll() {
    this._tipEl.style.display = 'none';
    this._tipEl.innerHTML = '';
    this._activeTip = null;
  }

  hasActive() {
    return this._activeTip !== null;
  }
}
