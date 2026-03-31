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
    return '请先在审核队列中以管理员身份登录，再管理模型提供方。';
  }

  if (error?.status === 403) {
    return '当前账号没有权限管理模型提供方。';
  }

  if (error?.status === 404) {
    return '当前 Server 版本暂未开放模型提供方管理接口。';
  }

  return error?.message || '无法加载模型提供方配置。';
}

export async function loadProviders(force = false) {
  if (!force && !providersSupported && providersMessage) {
    updateSharedModelTag($, '不可用');
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
    updateSharedModelTag($, '不可用');
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
    alert('切换模型失败：' + getProviderErrorMessage(error));
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
    body.innerHTML = `<div class="llm-empty">${esc(providersMessage || '模型提供方配置当前不可用。')}</div>`;
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
            <div class="llm-provider-meta">${esc(provider.model)} · ${esc(provider.apiKeyHint || '未配置密钥提示')}</div>
          </div>
          <span class="llm-provider-badge ${badge}">${esc(provider.providerType || 'openai')}</span>
          <div class="llm-provider-actions">
            <button class="llm-provider-action-btn" data-llm-action="edit" data-provider-id="${encodeURIComponent(provider.id)}" title="编辑">编辑</button>
            <button class="llm-provider-action-btn delete" data-llm-action="delete" data-provider-id="${encodeURIComponent(provider.id)}" title="删除">删除</button>
          </div>
        </div>`;
    }
    html += '</div>';
  } else {
    html += '<div class="llm-empty">还没有配置任何模型提供方。</div>';
  }

  html += renderForm();
  body.innerHTML = html;
}

function renderForm() {
  const provider = editingProvider;
  const isEdit = Boolean(provider?.id);

  let html = '<div class="llm-form">';
  html += `<h4>${isEdit ? '编辑提供方' : '新增提供方'}</h4>`;

  if (!isEdit) {
    html += `<div class="llm-form-row">
      <div class="llm-form-group">
        <label>预设</label>
        <select id="llmPreset" data-llm-change-action="apply-preset">
          <option value="">-- 选择预设 --</option>
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
        <label>名称</label>
        <input id="llmFormName" value="${esc(provider?.name || '')}" placeholder="例如：GPT-4o" />
      </div>
      <div class="llm-form-group">
        <label>类型</label>
        <select id="llmFormType">
          <option value="openai"${(provider?.providerType || 'openai') === 'openai' ? ' selected' : ''}>OpenAI 兼容</option>
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
        <label>模型</label>
        <input id="llmFormModel" value="${esc(provider?.model || '')}" placeholder="gpt-4o" />
      </div>
    </div>
    <div class="llm-form-row">
      <div class="llm-form-group">
        <label>Embedding Base URL</label>
        <input id="llmFormEmbUrl" value="${esc(provider?.embeddingBaseUrl || '')}" placeholder="可选" />
      </div>
      <div class="llm-form-group">
        <label>Embedding 模型</label>
        <input id="llmFormEmbModel" value="${esc(provider?.embeddingModel || '')}" placeholder="可选" />
      </div>
    </div>
    <div class="llm-form-actions">
      <button class="btn btn-primary btn-sm" data-llm-action="save">${isEdit ? '保存' : '新增'}</button>
      ${isEdit ? '<button class="btn btn-secondary btn-sm" data-llm-action="cancel">取消</button>' : ''}
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
    alert('切换模型失败：' + getProviderErrorMessage(error));
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

  if (!confirm(`确定删除提供方“${provider.name || provider.model}”吗？`)) return;

  try {
    await api(`/agent/providers/${id}`, { method: 'DELETE' });
    await loadProviders(true);
    renderSettings();
  } catch (error) {
    alert('删除失败：' + getProviderErrorMessage(error));
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
    alert('新建提供方时必须填写 API Key。');
    return;
  }

  if (!baseUrl || !model) {
    alert('Base URL 和模型名不能为空。');
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
    alert('保存失败：' + getProviderErrorMessage(error));
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
