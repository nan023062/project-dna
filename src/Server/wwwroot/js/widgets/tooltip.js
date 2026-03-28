/**
 * 通用 Tooltip 组件（单例，接入 panel 生命周期）
 *
 * panel 切换或 DOM 重建时，lifecycle 自动调用 dismiss 清理残留 tooltip。
 * 正常 mouseleave 关闭时，注销 lifecycle 注册，避免重复清理。
 */

import { registerDisposable, unregisterDisposable } from './lifecycle.js';

const OFFSET_X = 16;
const OFFSET_Y = 12;
const EDGE_MARGIN = 8;

let _el = null;
let _currentOwner = null;
let _currentPriority = -1;
let _lifecycleDispose = null;

function ensureElement() {
  if (_el) return _el;
  _el = document.createElement('div');
  _el.className = 'app-tooltip';
  document.body.appendChild(_el);
  return _el;
}

export function showTooltip(owner, priority, html, e) {
  const el = ensureElement();

  if (html === null) {
    if (_currentOwner !== owner) return;
    positionAt(el, e.clientX, e.clientY);
    return;
  }

  if (_currentOwner && _currentOwner !== owner && priority < _currentPriority) return;

  _currentOwner = owner;
  _currentPriority = priority;
  el.innerHTML = html;
  el.classList.add('visible');
  positionAt(el, e.clientX, e.clientY);

  if (!_lifecycleDispose) {
    _lifecycleDispose = () => dismiss();
    registerDisposable(_lifecycleDispose);
  }
}

export function hideTooltip(owner) {
  if (!_el) return;
  if (_currentOwner !== owner) return;
  _el.classList.remove('visible');
  _currentOwner = null;
  _currentPriority = -1;

  if (_lifecycleDispose) {
    unregisterDisposable(_lifecycleDispose);
    _lifecycleDispose = null;
  }
}

function dismiss() {
  if (!_el) return;
  _el.classList.remove('visible');
  _currentOwner = null;
  _currentPriority = -1;
  _lifecycleDispose = null;
}

function positionAt(el, mx, my) {
  const vw = window.innerWidth;
  const vh = window.innerHeight;
  const rect = el.getBoundingClientRect();
  const w = rect.width || 380;
  const h = rect.height || 200;

  let x = mx + OFFSET_X;
  let y = my + OFFSET_Y;

  if (x + w + EDGE_MARGIN > vw) x = mx - w - OFFSET_X;
  if (y + h + EDGE_MARGIN > vh) y = my - h - OFFSET_Y;
  if (x < EDGE_MARGIN) x = EDGE_MARGIN;
  if (y < EDGE_MARGIN) y = EDGE_MARGIN;

  el.style.left = x + 'px';
  el.style.top = y + 'px';
}
