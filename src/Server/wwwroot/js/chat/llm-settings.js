/**
 * LLM 模型配置设置模块
 * - 管理大模型 Provider 的增删改
 * - 设置活跃 Provider
 * - 配置弹窗 UI
 */

import { $ } from '../utils.js';

function updateModelTag(model) {
  const el = document.getElementById('chatModelTag');
  if (el) el.textContent = model || '未配置';
}

let providers = [];
let activeProviderId = null;
let editingProvider = null;

const PRESETS = {
  openai: { name: 'OpenAI', baseUrl: 'https://api.openai.com/v1', model: 'gpt-4o' },
  anthropic: { name: 'Anthropic Claude', baseUrl: 'https://api.anthropic.com', model: 'claude-sonnet-4-20250514' },
  deepseek: { name: 'DeepSeek', baseUrl: 'https://api.deepseek.com/v1', model: 'deepseek-chat', providerType: 'openai' },
  moonshot: { name: 'Moonshot', baseUrl: 'https://api.moonshot.cn/v1', model: 'moonshot-v1-8k', providerType: 'openai' },
  custom: { name: '', baseUrl: '', model: '', providerType: 'openai' }
};

export async function loadProviders() {
  try {
    const resp = await fetch('/api/agent/providers');
    const data = await resp.json();
    providers = data.providers || [];
    activeProviderId = data.activeProviderId || data.activeId || null;
    updateModelTag(getActiveModelName());
  } catch { /* best effort */ }
}

export function getProviderList() { return providers; }
export function getActiveProviderId() { return activeProviderId; }

async function requestSetActiveProvider(id) {
  const payload = { id, providerId: id };

  let resp = await fetch('/api/agent/providers/active', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  });

  if (resp.status === 404 || resp.status === 405) {
    resp = await fetch('/api/agent/providers/active', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
  }

  return resp;
}

export async function switchProvider(id) {
  try {
    const resp = await requestSetActiveProvider(id);
    if (!resp.ok) throw new Error(`切换失败 (${resp.status})`);
    await loadProviders();
    updateModelTag(getActiveModelName());
  } catch (err) {
    alert('模型切换失败: ' + (err?.message || String(err)));
  }
}

export function openLlmSettings() {
  loadProviders().then(() => renderSettings());
  $('llmSettingsOverlay').classList.add('open');
}

export function closeLlmSettings() {
  $('llmSettingsOverlay').classList.remove('open');
  editingProvider = null;
}

function getActiveModelName() {
  const p = providers.find(p => p.id === activeProviderId);
  return p ? p.model : '未配置';
}

function renderSettings() {
  const body = $('llmSettingsBody');
  let html = '';

  if (providers.length > 0) {
    html += '<div class="llm-provider-list">';
    for (const p of providers) {
      const isActive = p.id === activeProviderId;
      const badge = p.providerType === 'anthropic' ? 'anthropic' : 'openai';
      html += `
        <div class="llm-provider-card${isActive ? ' active' : ''}" onclick="window._llmSetActive('${p.id}')">
          <div class="llm-provider-info">
            <div class="llm-provider-name">${esc(p.name || p.model)}</div>
            <div class="llm-provider-meta">${esc(p.model)} · ${esc(p.apiKeyHint || '未设置')}</div>
          </div>
          <span class="llm-provider-badge ${badge}">${p.providerType}</span>
          <div class="llm-provider-actions" onclick="event.stopPropagation()">
            <button class="llm-provider-action-btn" onclick="window._llmEdit('${p.id}')" title="编辑">✏</button>
            <button class="llm-provider-action-btn delete" onclick="window._llmDelete('${p.id}')" title="删除">✕</button>
          </div>
        </div>`;
    }
    html += '</div>';
  } else {
    html += '<div class="llm-empty">尚未配置任何大模型。添加一个开始使用 AI 助手。</div>';
  }

  html += renderForm();
  body.innerHTML = html;
}

function renderForm() {
  const ep = editingProvider;
  const isEdit = ep && ep.id;

  let html = '<div class="llm-form">';
  html += `<h4>${isEdit ? '编辑模型' : '添加模型'}</h4>`;

  if (!isEdit) {
    html += `<div class="llm-form-row">
      <div class="llm-form-group">
        <label>快速选择</label>
        <select id="llmPreset" onchange="window._llmApplyPreset(this.value)">
          <option value="">-- 选择预设 --</option>
          <option value="openai">OpenAI (GPT-4o)</option>
          <option value="anthropic">Anthropic (Claude)</option>
          <option value="deepseek">DeepSeek</option>
          <option value="moonshot">Moonshot</option>
          <option value="custom">自定义</option>
        </select>
      </div>
    </div>`;
  }

  html += `
    <div class="llm-form-row">
      <div class="llm-form-group">
        <label>名称</label>
        <input id="llmFormName" value="${esc(ep?.name || '')}" placeholder="例如: GPT-4o" />
      </div>
      <div class="llm-form-group">
        <label>类型</label>
        <select id="llmFormType">
          <option value="openai"${(ep?.providerType || 'openai') === 'openai' ? ' selected' : ''}>OpenAI 兼容</option>
          <option value="anthropic"${ep?.providerType === 'anthropic' ? ' selected' : ''}>Anthropic</option>
        </select>
      </div>
    </div>
    <div class="llm-form-row">
      <div class="llm-form-group">
        <label>API Key</label>
        <input id="llmFormKey" type="password" value="${esc(ep?.apiKey || '')}" placeholder="sk-..." />
      </div>
    </div>
    <div class="llm-form-row">
      <div class="llm-form-group">
        <label>Base URL</label>
        <input id="llmFormUrl" value="${esc(ep?.baseUrl || '')}" placeholder="https://api.openai.com/v1" />
      </div>
      <div class="llm-form-group">
        <label>模型</label>
        <input id="llmFormModel" value="${esc(ep?.model || '')}" placeholder="gpt-4o" />
      </div>
    </div>
    <div class="llm-form-row">
      <div class="llm-form-group">
        <label>Embedding Base URL <span style="color:var(--text-tertiary);font-size:0.85em">（留空同 Base URL）</span></label>
        <input id="llmFormEmbUrl" value="${esc(ep?.embeddingBaseUrl || '')}" placeholder="留空则复用上方 Base URL" />
      </div>
      <div class="llm-form-group">
        <label>Embedding 模型 <span style="color:var(--text-tertiary);font-size:0.85em">（留空则不启用向量检索）</span></label>
        <input id="llmFormEmbModel" value="${esc(ep?.embeddingModel || '')}" placeholder="例如 text-embedding-3-small" />
      </div>
    </div>
    <div class="llm-form-actions">
      <button class="btn btn-primary btn-sm" onclick="window._llmSave()">
        ${isEdit ? '保存修改' : '添加'}
      </button>
      ${isEdit ? '<button class="btn btn-secondary btn-sm" onclick="window._llmCancelEdit()">取消</button>' : ''}
    </div>`;

  html += '</div>';
  return html;
}

// ── Actions (exposed to window for onclick) ──

async function setActive(id) {
  try {
    const resp = await requestSetActiveProvider(id);
    if (!resp.ok) throw new Error(`切换失败 (${resp.status})`);
    await loadProviders();
    updateModelTag(getActiveModelName());
    renderSettings();
  } catch (err) {
    alert('切换失败: ' + (err?.message || String(err)));
  }
}

function startEdit(id) {
  const p = providers.find(p => p.id === id);
  if (!p) return;
  editingProvider = { ...p, apiKey: '' };
  renderSettings();
}

async function deleteProvider(id) {
  const p = providers.find(p => p.id === id);
  if (!p) return;
  if (!confirm(`确认删除模型配置「${p.name || p.model}」？`)) return;

  await fetch(`/api/agent/providers/${id}`, { method: 'DELETE' });
  await loadProviders();
  renderSettings();
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
    alert('请输入 API Key');
    return;
  }
  if (!baseUrl || !model) {
    alert('请填写 Base URL 和模型名称');
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

  await fetch('/api/agent/providers', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });

  editingProvider = null;
  await loadProviders();
  renderSettings();
}

function cancelEdit() {
  editingProvider = null;
  renderSettings();
}

function applyPreset(key) {
  const preset = PRESETS[key];
  if (!preset) return;
  $('llmFormName').value = preset.name;
  $('llmFormUrl').value = preset.baseUrl;
  $('llmFormModel').value = preset.model;
  if (preset.providerType) {
    $('llmFormType').value = preset.providerType;
  } else {
    $('llmFormType').value = key === 'anthropic' ? 'anthropic' : 'openai';
  }
}

// ── Window bridges ──
window._llmSetActive = setActive;
window._llmEdit = startEdit;
window._llmDelete = deleteProvider;
window._llmSave = saveProvider;
window._llmCancelEdit = cancelEdit;
window._llmApplyPreset = applyPreset;

function esc(s) {
  if (!s) return '';
  const d = document.createElement('div');
  d.textContent = s;
  return d.innerHTML;
}
