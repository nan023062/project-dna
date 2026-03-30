/**
 * 通用工具模块
 * - DOM 选择器
 * - HTML 转义
 * - API 请求封装
 */

export const $ = id => document.getElementById(id);

export function escapeHtml(text) {
  const d = document.createElement('div');
  d.textContent = text;
  return d.innerHTML;
}

/** 转义字符串用于 HTML 属性值（双引号上下文） */
export function escapeAttr(str) {
  return str.replace(/&/g, '&amp;').replace(/"/g, '&quot;');
}

export async function api(path, options = {}) {
  const init = { ...options };
  if (options.body && typeof options.body === 'object') {
    init.headers = { 'Content-Type': 'application/json', ...options.headers };
    init.body = JSON.stringify(options.body);
  }
  const res = await fetch('/api' + path, init);
  const text = await res.text();
  let payload = {};
  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = { error: text };
    }
  }

  if (!res.ok) {
    const message = payload?.error || payload?.message || `${res.status} ${res.statusText}`;
    throw new Error(message);
  }

  return payload;
}
