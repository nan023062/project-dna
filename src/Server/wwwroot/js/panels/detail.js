/**
 * Read-only module detail panel for knowledge drill-down.
 */

import { $, api, escapeHtml } from '../utils.js';

const MEMORY_TAGS = [
  { key: '#identity', label: 'Identity', icon: 'ID' },
  { key: 'links', label: 'Links', icon: 'DEP' },
  { key: '#lesson', label: 'Lessons', icon: 'LOG' },
  { key: '#changelog', label: 'History', icon: 'REV' },
  { key: '#active-task', label: 'Current', icon: 'NOW' }
];

let switchTabFn = null;
let currentModule = null;
let currentTag = null;
let memoriesByTag = {};

export function initDetail(switchTab) {
  switchTabFn = switchTab;
}

function getDetailErrorMessage(error) {
  if (error?.status === 401) {
    return '请先在审核队列中以管理员身份登录，再查看模块详情。';
  }

  if (error?.status === 403) {
    return '当前账号没有权限读取模块详情。';
  }

  return error?.message || '请求失败。';
}

export async function showModuleDetail(name, moduleType = null) {
  if (switchTabFn) switchTabFn('detail');

  const container = $('detailContent');
  container.innerHTML = '<div class="empty">加载中...</div>';

  try {
    const topoData = await api('/topology');
    const moduleInfo = topoData.modules?.find(x => x.name === name);
    const moduleId = moduleInfo?.name || null;

    if (!moduleId) {
      container.innerHTML = '<div class="empty">未找到模块元数据。</div>';
      return;
    }

    const memories = await api(`/memory/query?nodeId=${encodeURIComponent(moduleId)}&limit=100`);

    currentModule = name;
    memoriesByTag = {
      '#identity': memories.filter(m => Array.isArray(m.tags) && m.tags.includes('#identity')),
      links: [],
      '#lesson': memories.filter(m => Array.isArray(m.tags) && m.tags.includes('#lesson')),
      '#changelog': memories.filter(m => Array.isArray(m.tags) && m.tags.includes('#changelog')),
      '#active-task': memories.filter(m => Array.isArray(m.tags) && m.tags.includes('#active-task'))
    };

    if (moduleInfo) {
      memoriesByTag.links = [{
        content: `## Declared Dependencies\n${(moduleInfo.dependencies || []).map(d => `- ${d}`).join('\n')}\n\n## Computed Dependencies\n${(moduleInfo.computedDependencies || []).map(d => `- ${d}`).join('\n')}`
      }];
    }

    renderDetailPanel(name);

    const typeEl = $('detailModuleType');
    if (typeEl) {
      typeEl.textContent = formatGameRole(moduleType || moduleInfo?.type || 'coder');
    }

    selectTag('#identity');
  } catch (error) {
    container.innerHTML = `<div class="empty">${escapeHtml(getDetailErrorMessage(error))}</div>`;
  }
}

function renderDetailPanel(name) {
  const container = $('detailContent');
  container.classList.remove('empty');

  let html = '<div class="detail-panel">';
  html += `<div class="detail-header">
    <h2>${escapeHtml(name)}</h2>
    <div style="font-size: 13px; color: #94a3b8; margin-top: 4px;">角色：<span id="detailModuleType" style="color: #38bdf8;">加载中...</span></div>
  </div>`;

  html += '<div class="memory-tabs" id="memoryTabs">';
  for (const tag of MEMORY_TAGS) {
    const exists = memoriesByTag[tag.key] && memoriesByTag[tag.key].length > 0;
    html += `<div class="memory-tab${!exists ? ' missing' : ''}" data-tag="${tag.key}">${tag.icon} ${tag.label}</div>`;
  }
  html += '</div>';

  html += '<div id="memoryEditorArea" class="memory-editor-wrap" style="overflow-y: auto; padding: 16px;"></div>';
  html += '</div>';
  container.innerHTML = html;

  container.querySelectorAll('.memory-tab').forEach(tab => {
    tab.addEventListener('click', () => selectTag(tab.dataset.tag));
  });
}

function selectTag(tagKey) {
  currentTag = tagKey;
  void currentTag;
  void currentModule;

  $('memoryTabs').querySelectorAll('.memory-tab').forEach(tab => {
    tab.classList.toggle('active', tab.dataset.tag === tagKey);
  });

  const area = $('memoryEditorArea');
  const items = memoriesByTag[tagKey] || [];
  if (items.length === 0) {
    area.innerHTML = '<div class="empty">没有匹配的知识。</div>';
    return;
  }

  const combinedContent = items.map(memory => {
    const nodeType = memory.nodeType ?? memory.layer ?? '-';
    let header = `> **ID**: ${memory.id} | **Type**: ${memory.type} | **Node Type**: ${nodeType} | **Freshness**: ${memory.freshness}\n`;
    if (memory.summary) header += `> **Summary**: ${memory.summary}\n`;
    return header + '\n' + memory.content;
  }).join('\n\n---\n\n');

  if (window.marked) {
    area.innerHTML = `<div class="memory-rendered">${window.marked.parse(combinedContent)}</div>`;
    return;
  }

  area.innerHTML = `<pre class="memory-readonly">${escapeHtml(combinedContent)}</pre>`;
}

function formatGameRole(moduleType) {
  const role = String(moduleType || '').trim().toLowerCase();
  if (role === 'designer') return '策划';
  if (role === 'art') return '美术';
  return '程序';
}
