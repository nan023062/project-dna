/**
 * 通用工具模块
 * - DOM 选择器
 * - HTML 转义
 * - API 请求封装
 * - 管理台鉴权状态
 */

export const $ = id => document.getElementById(id);
const AUTH_TOKEN_KEY = 'dna.admin.token';

export function escapeHtml(text) {
  const d = document.createElement('div');
  d.textContent = text;
  return d.innerHTML;
}

/** 转义字符串用于 HTML 属性值（双引号上下文） */
export function escapeAttr(str) {
  return str.replace(/&/g, '&amp;').replace(/"/g, '&quot;');
}

export function getAuthToken() {
  try {
    return sessionStorage.getItem(AUTH_TOKEN_KEY);
  } catch {
    return null;
  }
}

export function setAuthToken(token) {
  try {
    if (!token) {
      sessionStorage.removeItem(AUTH_TOKEN_KEY);
      return;
    }
    sessionStorage.setItem(AUTH_TOKEN_KEY, token);
  } catch {
    // ignore storage failures in local admin UI
  }
}

export function clearAuthToken() {
  try {
    sessionStorage.removeItem(AUTH_TOKEN_KEY);
  } catch {
    // ignore storage failures in local admin UI
  }
}

export async function api(path, options = {}) {
  const {
    skipAuth = false,
    headers: customHeaders,
    body,
    ...rest
  } = options;

  const headers = { ...(customHeaders || {}) };
  const init = { ...rest, headers };
  const token = skipAuth ? null : getAuthToken();

  if (token && !headers.Authorization) {
    headers.Authorization = `Bearer ${token}`;
  }

  if (typeof body === 'string') {
    if (!headers['Content-Type']) {
      headers['Content-Type'] = 'application/json';
    }
    init.body = body;
  } else if (body && typeof body === 'object' && !(body instanceof FormData)) {
    headers['Content-Type'] = headers['Content-Type'] || 'application/json';
    init.body = JSON.stringify(body);
  } else if (body !== undefined) {
    init.body = body;
  }

  const res = await fetch('/api' + path, init);
  const text = await res.text();
  let payload = {};

  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = { message: text };
    }
  }

  if (!res.ok) {
    const error = new Error(payload?.error || payload?.message || `${res.status} ${res.statusText}`);
    error.status = res.status;
    error.payload = payload;
    throw error;
  }

  return payload;
}
