import {
  $,
  escapeHtml,
  escapeAttr,
  createApiClient,
  createSessionStorageStore
} from '/dna-shared/js/core/web-utils.js';

const AUTH_CHANGED_EVENT = 'dna-admin-auth-changed';
const authTokenStore = createSessionStorageStore('dna.admin.token', {
  onChange: token => window.dispatchEvent(new CustomEvent(AUTH_CHANGED_EVENT, { detail: { token } }))
});
const sharedApi = createApiClient({
  getAuthToken: () => authTokenStore.get(),
  textFallbackField: 'message'
});

export { $, escapeHtml, escapeAttr };

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

export const buildApiUrl = sharedApi.buildApiUrl;
export const apiFetch = sharedApi.apiFetch;
export const api = sharedApi.api;
