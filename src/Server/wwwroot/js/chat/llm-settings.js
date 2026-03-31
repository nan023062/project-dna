/**
 * LLM provider settings for the server chat panel.
 * Gracefully degrades when provider-management endpoints are unavailable.
 */

import { $, api } from '../utils.js';
import { bindDelegatedDocumentEvents } from '/dna-shared/js/core/dom-actions.js';
import { PROVIDER_PRESETS } from '/dna-shared/js/chat/provider-presets.js';
import {
  getActiveModelName,
  getProviderBadgeClass,
  updateModelTag as updateSharedModelTag
} from '/dna-shared/js/chat/provider-ui.js';

let providers = [];
let activeProviderId = null;
let editingProvider = null;
let providersSupported = true;
let providersMessage = '';
let llmSettingsEventsBound = false;

function getProviderErrorMessage(error) {
  if (error?.status === 401) {
    return 'Sign in as admin in Review Queue to manage LLM providers.';
  }

  if (error?.status === 403) {
    return 'The current account does not have permission to manage LLM providers.';
  }

  if (error?.status === 404) {
    return 'This server build does not expose provider-management endpoints yet.';
  }

  return error?.message || 'Unable to load provider settings.';
}

export async function loadProviders(force = false) {
  if (!force && !providersSupported && providersMessage) {
    updateSharedModelTag($, 'Unavailable');
    return { supported: false, message: providersMessage };
  }

  try {
    const data = await api('/agent/providers');
    providers = data.providers || [];
    activeProviderId = data.activeProviderId || data.activeId || null;
    providersSupported = true;
    providersMessage = '';
    updateSharedModelTag($, getActiveModelName(providers, activeProviderId));
    return { supported: true };
  } catch (error) {
    providers = [];
    activeProviderId = null;
    providersSupported = error?.status !== 404 ? true : false;
    providersMessage = getProviderErrorMessage(error);
    updateSharedModelTag($, 'Unavailable');
    return { supported: false, message: providersMessage, error };
  }
}

export function getProviderList() {
  return providers;
}

export function getActiveProviderId() {
  return activeProviderId;
}

async function requestSetActiveProvider(id) {
  const payload = { id, providerId: id };

  try {
    return await api('/agent/providers/active', {
      method: 'POST',
      body: payload
    });
  } catch (error) {
    if (error?.status === 404 || error?.status === 405) {
      return api('/agent/providers/active', {
        method: 'PUT',
        body: payload
      });
    }

    throw error;
  }
}

export async function switchProvider(id) {
  try {
    await requestSetActiveProvider(id);
    await loadProviders(true);
    updateSharedModelTag($, getActiveModelName(providers, activeProviderId));
  } catch (error) {
    alert('Provider switch failed: ' + getProviderErrorMessage(error));
  }
}

export async function openLlmSettings() {
  await loadProviders();
  renderSettings();
  $('llmSettingsOverlay').classList.add('open');
}

export function closeLlmSettings() {
  $('llmSettingsOverlay').classList.remove('open');
  editingProvider = null;
}

async function handleLlmAction(action, element) {
  const providerId = element?.dataset.providerId
    ? decodeURIComponent(element.dataset.providerId)
    : '';

  switch (action) {
    case 'set-active':
      if (providerId) {
        await setActive(providerId);
      }
      break;
    case 'edit':
      if (providerId) {
        startEdit(providerId);
      }
      break;
    case 'delete':
      if (providerId) {
        await deleteProvider(providerId);
      }
      break;
    case 'save':
      await saveProvider();
      break;
    case 'cancel':
      cancelEdit();
      break;
    default:
      break;
  }
}

function handleLlmChangeAction(action, element) {
  switch (action) {
    case 'apply-preset':
      applyPreset(element?.value || '');
      break;
    default:
      break;
  }
}

function bindLlmSettingsEvents() {
  if (llmSettingsEventsBound || typeof document === 'undefined') {
    return;
  }

  llmSettingsEventsBound = true;

  bindDelegatedDocumentEvents([
    {
      eventName: 'click',
      selector: '[data-llm-action]',
      within: '#llmSettingsBody',
      preventDefault: true,
      handler: ({ element }) => void handleLlmAction(element.dataset.llmAction, element)
    },
    {
      eventName: 'change',
      selector: '[data-llm-change-action]',
      within: '#llmSettingsBody',
      handler: ({ element }) => handleLlmChangeAction(element.dataset.llmChangeAction, element)
    }
  ]);
}

function renderSettings() {
  const body = $('llmSettingsBody');
  if (!body) return;

  if (!providersSupported) {
    body.innerHTML = `<div class="llm-empty">${esc(providersMessage || 'Provider settings are unavailable.')}</div>`;
    return;
  }

  let html = '';
  if (providersMessage) {
    html += `<div class="llm-empty">${esc(providersMessage)}</div>`;
  }

  if (providers.length > 0) {
    html += '<div class="llm-provider-list">';
    for (const provider of providers) {
      const isActive = provider.id === activeProviderId;
      const badge = getProviderBadgeClass(provider.providerType);
      html += `
        <div
          class="llm-provider-card${isActive ? ' active' : ''}"
          data-llm-action="set-active"
          data-provider-id="${encodeURIComponent(provider.id)}"
        >
          <div class="llm-provider-info">
            <div class="llm-provider-name">${esc(provider.name || provider.model)}</div>
            <div class="llm-provider-meta">${esc(provider.model)} · ${esc(provider.apiKeyHint || 'no key hint')}</div>
          </div>
          <span class="llm-provider-badge ${badge}">${esc(provider.providerType || 'openai')}</span>
          <div class="llm-provider-actions">
            <button class="llm-provider-action-btn" data-llm-action="edit" data-provider-id="${encodeURIComponent(provider.id)}" title="Edit">Edit</button>
            <button class="llm-provider-action-btn delete" data-llm-action="delete" data-provider-id="${encodeURIComponent(provider.id)}" title="Delete">Delete</button>
          </div>
        </div>`;
    }
    html += '</div>';
  } else {
    html += '<div class="llm-empty">No provider is configured yet.</div>';
  }

  html += renderForm();
  body.innerHTML = html;
}

function renderForm() {
  const provider = editingProvider;
  const isEdit = Boolean(provider?.id);

  let html = '<div class="llm-form">';
  html += `<h4>${isEdit ? 'Edit Provider' : 'Add Provider'}</h4>`;

  if (!isEdit) {
    html += `<div class="llm-form-row">
      <div class="llm-form-group">
        <label>Preset</label>
        <select id="llmPreset" data-llm-change-action="apply-preset">
          <option value="">-- Select preset --</option>
          <option value="openai">OpenAI (GPT-4o)</option>
          <option value="anthropic">Anthropic (Claude)</option>
          <option value="deepseek">DeepSeek</option>
          <option value="moonshot">Moonshot</option>
          <option value="custom">Custom</option>
        </select>
      </div>
    </div>`;
  }

  html += `
    <div class="llm-form-row">
      <div class="llm-form-group">
        <label>Name</label>
        <input id="llmFormName" value="${esc(provider?.name || '')}" placeholder="Example: GPT-4o" />
      </div>
      <div class="llm-form-group">
        <label>Type</label>
        <select id="llmFormType">
          <option value="openai"${(provider?.providerType || 'openai') === 'openai' ? ' selected' : ''}>OpenAI compatible</option>
          <option value="anthropic"${provider?.providerType === 'anthropic' ? ' selected' : ''}>Anthropic</option>
        </select>
      </div>
    </div>
    <div class="llm-form-row">
      <div class="llm-form-group">
        <label>API Key</label>
        <input id="llmFormKey" type="password" value="${esc(provider?.apiKey || '')}" placeholder="sk-..." />
      </div>
    </div>
    <div class="llm-form-row">
      <div class="llm-form-group">
        <label>Base URL</label>
        <input id="llmFormUrl" value="${esc(provider?.baseUrl || '')}" placeholder="https://api.openai.com/v1" />
      </div>
      <div class="llm-form-group">
        <label>Model</label>
        <input id="llmFormModel" value="${esc(provider?.model || '')}" placeholder="gpt-4o" />
      </div>
    </div>
    <div class="llm-form-row">
      <div class="llm-form-group">
        <label>Embedding Base URL</label>
        <input id="llmFormEmbUrl" value="${esc(provider?.embeddingBaseUrl || '')}" placeholder="Optional" />
      </div>
      <div class="llm-form-group">
        <label>Embedding Model</label>
        <input id="llmFormEmbModel" value="${esc(provider?.embeddingModel || '')}" placeholder="Optional" />
      </div>
    </div>
    <div class="llm-form-actions">
      <button class="btn btn-primary btn-sm" data-llm-action="save">${isEdit ? 'Save' : 'Add'}</button>
      ${isEdit ? '<button class="btn btn-secondary btn-sm" data-llm-action="cancel">Cancel</button>' : ''}
    </div>`;

  html += '</div>';
  return html;
}

async function setActive(id) {
  try {
    await requestSetActiveProvider(id);
    await loadProviders(true);
    updateSharedModelTag($, getActiveModelName(providers, activeProviderId));
    renderSettings();
  } catch (error) {
    alert('Provider switch failed: ' + getProviderErrorMessage(error));
  }
}

function startEdit(id) {
  const provider = providers.find(item => item.id === id);
  if (!provider) return;

  editingProvider = { ...provider, apiKey: '' };
  renderSettings();
}

async function deleteProvider(id) {
  const provider = providers.find(item => item.id === id);
  if (!provider) return;

  if (!confirm(`Delete provider "${provider.name || provider.model}"?`)) return;

  try {
    await api(`/agent/providers/${id}`, { method: 'DELETE' });
    await loadProviders(true);
    renderSettings();
  } catch (error) {
    alert('Delete failed: ' + getProviderErrorMessage(error));
  }
}

async function saveProvider() {
  const name = $('llmFormName').value.trim();
  const providerType = $('llmFormType').value;
  const apiKey = $('llmFormKey').value.trim();
  const baseUrl = $('llmFormUrl').value.trim();
  const model = $('llmFormModel').value.trim();
  const embeddingBaseUrl = $('llmFormEmbUrl').value.trim();
  const embeddingModel = $('llmFormEmbModel').value.trim();

  if (!apiKey && !editingProvider?.id) {
    alert('API key is required for a new provider.');
    return;
  }

  if (!baseUrl || !model) {
    alert('Base URL and model are required.');
    return;
  }

  const body = {
    id: editingProvider?.id || '',
    name: name || model,
    providerType,
    apiKey,
    baseUrl,
    model,
    embeddingBaseUrl,
    embeddingModel
  };

  try {
    await api('/agent/providers', {
      method: 'POST',
      body
    });

    editingProvider = null;
    await loadProviders(true);
    renderSettings();
  } catch (error) {
    alert('Save failed: ' + getProviderErrorMessage(error));
  }
}

function cancelEdit() {
  editingProvider = null;
  renderSettings();
}

function applyPreset(key) {
  const preset = PROVIDER_PRESETS[key];
  if (!preset) return;

  $('llmFormName').value = preset.name;
  $('llmFormUrl').value = preset.baseUrl;
  $('llmFormModel').value = preset.model;
  $('llmFormType').value = preset.providerType || 'openai';
}

function esc(value) {
  if (!value) return '';
  const el = document.createElement('div');
  el.textContent = value;
  return el.innerHTML;
}

bindLlmSettingsEvents();
