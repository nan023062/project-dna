import {
  $,
  escapeHtml,
  escapeAttr,
  createApiClient,
  createSessionStorageStore
} from '/dna-shared/js/core/web-utils.js';

const AUTH_SCOPE_KEY = 'dna.client.auth.scope';
const LEGACY_AUTH_TOKEN_KEY = 'dna.client.token';
const LEGACY_AUTH_USER_KEY = 'dna.client.user';
const authScopeStore = createSessionStorageStore(AUTH_SCOPE_KEY);
let currentAuthScope = normalizeAuthScope(authScopeStore.get());

const sharedApi = createApiClient({
  getAuthToken: () => authTokenStore.get(),
  textFallbackField: 'error'
});

export { $, escapeHtml, escapeAttr };

export function getAuthScope() {
  return currentAuthScope;
}

export function setAuthScope(scope) {
  const normalized = normalizeAuthScope(scope);
  const changed = normalized !== currentAuthScope;
  currentAuthScope = normalized;
  authScopeStore.set(normalized);
  return changed;
}

export function getAuthToken() {
  return authTokenStore.get();
}

export function setAuthToken(token) {
  if (!token) {
    authTokenStore.clear();
    return;
  }

  authTokenStore.set(token);
}

export function clearAuthToken() {
  authTokenStore.clear();
}

export function getAuthUser() {
  return authUserStore.get();
}

export function setAuthUser(user) {
  if (!user) {
    authUserStore.clear();
    return;
  }

  authUserStore.set(user);
}

export function clearAuthUser() {
  authUserStore.clear();
}

export function clearAuthState() {
  clearAuthToken();
  clearAuthUser();
}

export const buildApiUrl = sharedApi.buildApiUrl;
export const apiFetch = sharedApi.apiFetch;
export const api = sharedApi.api;

const authTokenStore = {
  get: () => readScopedValue('dna.client.token', value => value, LEGACY_AUTH_TOKEN_KEY),
  set: value => writeScopedValue('dna.client.token', value, current => String(current)),
  clear: () => clearScopedValue('dna.client.token')
};

const authUserStore = {
  get: () => readScopedValue('dna.client.user', raw => JSON.parse(raw), LEGACY_AUTH_USER_KEY),
  set: value => writeScopedValue('dna.client.user', value, current => JSON.stringify(current)),
  clear: () => clearScopedValue('dna.client.user')
};

function normalizeAuthScope(scope) {
  const normalized = String(scope ?? '').trim();
  return normalized || 'default';
}

function buildScopedStorageKey(prefix) {
  return `${prefix}:${normalizeAuthScope(currentAuthScope)}`;
}

function readScopedValue(prefix, parse, legacyKey = null) {
  const storageKey = buildScopedStorageKey(prefix);

  try {
    let raw = sessionStorage.getItem(storageKey);
    if (raw === null && legacyKey) {
      raw = sessionStorage.getItem(legacyKey);
      if (raw !== null) {
        sessionStorage.setItem(storageKey, raw);
        sessionStorage.removeItem(legacyKey);
      }
    }

    if (raw === null) return null;
    return parse(raw);
  } catch {
    return null;
  }
}

function writeScopedValue(prefix, value, serialize) {
  if (value == null) {
    clearScopedValue(prefix);
    return;
  }

  try {
    sessionStorage.setItem(buildScopedStorageKey(prefix), serialize(value));
  } catch {
    // ignore storage failures in browser shells
  }
}

function clearScopedValue(prefix) {
  try {
    sessionStorage.removeItem(buildScopedStorageKey(prefix));
  } catch {
    // ignore storage failures in browser shells
  }
}
