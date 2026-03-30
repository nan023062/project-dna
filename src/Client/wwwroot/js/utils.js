import {
  $,
  escapeHtml,
  escapeAttr,
  createApiClient,
  createSessionStorageStore
} from '/dna-shared/js/core/web-utils.js';

const authTokenStore = createSessionStorageStore('dna.client.token');
const authUserStore = createSessionStorageStore('dna.client.user', {
  parse: raw => JSON.parse(raw),
  serialize: value => JSON.stringify(value)
});
const sharedApi = createApiClient({
  getAuthToken: () => authTokenStore.get(),
  textFallbackField: 'error'
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
