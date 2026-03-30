export const $ = id => document.getElementById(id);

export function escapeHtml(text) {
  const d = document.createElement('div');
  d.textContent = text ?? '';
  return d.innerHTML;
}

export function escapeAttr(str = '') {
  return String(str).replace(/&/g, '&amp;').replace(/"/g, '&quot;');
}

export function createSessionStorageStore(storageKey, options = {}) {
  const {
    parse = value => value,
    serialize = value => String(value),
    onChange = null
  } = options;

  function clear() {
    try {
      sessionStorage.removeItem(storageKey);
      onChange?.(null);
    } catch {
      // ignore storage failures in browser shells
    }
  }

  return {
    get() {
      try {
        const raw = sessionStorage.getItem(storageKey);
        if (raw === null) return null;
        return parse(raw);
      } catch {
        return null;
      }
    },
    set(value) {
      if (value == null) {
        clear();
        return;
      }

      try {
        sessionStorage.setItem(storageKey, serialize(value));
        onChange?.(value);
      } catch {
        // ignore storage failures in browser shells
      }
    },
    clear
  };
}

function isAbsoluteUrl(path) {
  return /^https?:\/\//i.test(path) || path.startsWith('//');
}

export function buildApiUrl(path, basePath = '/api') {
  if (!path) return basePath;
  if (isAbsoluteUrl(path) || path.startsWith(basePath)) return path;

  const normalizedBase = basePath.endsWith('/') ? basePath.slice(0, -1) : basePath;
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  return normalizedBase + normalizedPath;
}

function buildApiInit(options = {}, getAuthToken) {
  const {
    skipAuth = false,
    headers: customHeaders,
    body,
    ...rest
  } = options;

  const headers = { ...(customHeaders || {}) };
  const init = { ...rest, headers };
  const token = skipAuth ? null : getAuthToken?.();

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

  return init;
}

export function createApiClient(options = {}) {
  const {
    getAuthToken = () => null,
    basePath = '/api',
    textFallbackField = 'message'
  } = options;

  const resolveApiUrl = path => buildApiUrl(path, basePath);

  async function apiFetch(path, requestOptions = {}) {
    return fetch(resolveApiUrl(path), buildApiInit(requestOptions, getAuthToken));
  }

  async function api(path, requestOptions = {}) {
    const res = await apiFetch(path, requestOptions);
    const text = await res.text();
    let payload = {};

    if (text) {
      try {
        payload = JSON.parse(text);
      } catch {
        payload = { [textFallbackField]: text };
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

  return {
    buildApiUrl: resolveApiUrl,
    apiFetch,
    api
  };
}
