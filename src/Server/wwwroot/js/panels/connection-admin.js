import { $, api, escapeHtml } from '../utils.js';

let _entries = [];
let _selectedId = null;

function asBool(value) {
  return String(value).toLowerCase() === 'true';
}

function readDraft() {
  return {
    ip: $('whitelistIp')?.value?.trim() || '',
    name: $('whitelistName')?.value?.trim() || '',
    role: $('whitelistRole')?.value || 'viewer',
    note: $('whitelistNote')?.value?.trim() || null,
    enabled: asBool($('whitelistEnabled')?.value ?? true)
  };
}

function fillDraft(entry) {
  $('whitelistEntryId').textContent = entry?.id || '新建';
  $('whitelistIp').value = entry?.ip || '';
  $('whitelistName').value = entry?.name || '';
  $('whitelistRole').value = entry?.role || 'viewer';
  $('whitelistEnabled').value = String(entry?.enabled ?? true);
  $('whitelistNote').value = entry?.note || '';
}

function renderList() {
  const root = $('whitelistList');
  if (!root) return;

  if (_entries.length === 0) {
    root.innerHTML = '<div class="empty">暂无白名单用户。</div>';
    return;
  }

  root.innerHTML = _entries.map(entry => {
    const active = entry.id === _selectedId ? 'active' : '';
    const state = entry.enabled ? '启用' : '禁用';
    return `
      <div class="memory-item ${active}" data-action="select-whitelist-entry" data-entry-id="${entry.id}">
        <div class="memory-item-title">${escapeHtml(entry.name || entry.ip)}</div>
        <div class="memory-item-meta">
          <span>${escapeHtml(entry.ip)}</span>
          <span>${escapeHtml(entry.role)} · ${state}</span>
        </div>
      </div>
    `;
  }).join('');
}

export async function loadWhitelist() {
  const result = await api('/connection/whitelist');
  _entries = Array.isArray(result?.entries) ? result.entries.slice() : [];
  _entries.sort((a, b) => String(a.name || a.ip).localeCompare(String(b.name || b.ip), 'zh-CN'));

  if (_selectedId && !_entries.some(entry => entry.id === _selectedId)) {
    _selectedId = null;
  }

  if (!_selectedId && _entries.length > 0) {
    _selectedId = _entries[0].id;
  }

  renderList();
  const selected = _entries.find(entry => entry.id === _selectedId) || null;
  fillDraft(selected);
}

export function selectWhitelistEntry(entryId) {
  _selectedId = entryId;
  renderList();
  const selected = _entries.find(entry => entry.id === entryId) || null;
  fillDraft(selected);
}

export function newWhitelistEntry() {
  _selectedId = null;
  renderList();
  fillDraft(null);
}

export async function saveWhitelistEntry() {
  const payload = readDraft();
  if (!payload.ip || !payload.name) {
    throw new Error('IP 和名称不能为空。');
  }

  if (_selectedId) {
    await api(`/connection/whitelist/${encodeURIComponent(_selectedId)}`, {
      method: 'PUT',
      body: payload
    });
  } else {
    const result = await api('/connection/whitelist', {
      method: 'POST',
      body: payload
    });
    _selectedId = result?.entry?.id || null;
  }

  await loadWhitelist();
}

export async function deleteWhitelistEntry() {
  if (!_selectedId) return;

  const confirmed = window.confirm('确定删除当前白名单用户吗？');
  if (!confirmed) return;

  await api(`/connection/whitelist/${encodeURIComponent(_selectedId)}`, {
    method: 'DELETE'
  });

  _selectedId = null;
  await loadWhitelist();
}
