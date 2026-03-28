/**
 * 模块详情面板（只读仪表盘）
 * 展示模块的记忆内容，所有编辑由 AI 通过 MCP 完成
 */

import { $, escapeHtml } from '../utils.js';

const MEMORY_TAGS = [
  { key: '#identity', label: '身份', icon: '🧬' },
  { key: 'links',     label: '依赖', icon: '🔗' },
  { key: '#lesson',   label: '教训', icon: '💡' },
  { key: '#changelog',label: '历史', icon: '📋' },
  { key: '#active-task',label: '当前', icon: '⚡' }
];

let _switchTabFn = null;
let _currentModule = null;
let _currentTag = null;
let _memories = {};

export function initDetail(switchTab) {
  _switchTabFn = switchTab;
}

export async function showModuleDetail(name, moduleType = null) {
  if (_switchTabFn) _switchTabFn('detail');
  const container = $('detailContent');
  container.innerHTML = '<div class="empty">加载中…</div>';

  try {
    let moduleId = null;
    const topoRes = await fetch('/api/topology');
    const topoData = await topoRes.json();
    const moduleInfo = topoData.modules?.find(x => x.name === name);
    if (moduleInfo) {
        moduleId = moduleInfo.name;
    }

    if (!moduleId) {
        container.innerHTML = `<div class="empty">未找到模块信息</div>`;
        return;
    }

    const res = await fetch(`/api/memory/query?nodeId=${encodeURIComponent(moduleId)}&limit=100`);
    const memories = await res.json();
    
    _currentModule = name;
    _memories = {
      '#identity': memories.filter(m => m.tags.includes('#identity')),
      'links': [],
      '#lesson': memories.filter(m => m.tags.includes('#lesson')),
      '#changelog': memories.filter(m => m.tags.includes('#changelog')),
      '#active-task': memories.filter(m => m.tags.includes('#active-task'))
    };
    
    if (moduleInfo) {
        _memories['links'] = [{
            content: `## 声明依赖\n${(moduleInfo.dependencies || []).map(d => `- ${d}`).join('\n')}\n\n## 事实依赖\n${(moduleInfo.computedDependencies || []).map(d => `- ${d}`).join('\n')}`
        }];
    }

    renderDetailPanel(name);
    
    if (moduleType) {
        const typeEl = $('detailModuleType');
        if (typeEl) typeEl.textContent = formatGameRole(moduleType);
    } else if (moduleInfo) {
        const typeEl = $('detailModuleType');
        if (typeEl) typeEl.textContent = formatGameRole(moduleInfo.type || 'coder');
    }

    selectTag('#identity');
  } catch (e) {
    container.innerHTML = `<div class="empty">请求失败: ${escapeHtml(e.message)}</div>`;
  }
}

function renderDetailPanel(name) {
  const container = $('detailContent');
  container.classList.remove('empty');

  let html = '<div class="detail-panel">';

  html += `<div class="detail-header">
    <h2>${escapeHtml(name)}</h2>
    <div style="font-size: 13px; color: #94a3b8; margin-top: 4px;">游戏角色/职责: <span id="detailModuleType" style="color: #38bdf8;">加载中...</span></div>
  </div>`;

  html += '<div class="memory-tabs" id="memoryTabs">';
  for (const t of MEMORY_TAGS) {
    const exists = _memories[t.key] && _memories[t.key].length > 0;
    html += `<div class="memory-tab${!exists ? ' missing' : ''}" data-tag="${t.key}">${t.icon} ${t.label}</div>`;
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
  _currentTag = tagKey;

  $('memoryTabs').querySelectorAll('.memory-tab').forEach(tab => {
    tab.classList.toggle('active', tab.dataset.tag === tagKey);
  });

  const area = $('memoryEditorArea');
  const items = _memories[tagKey] || [];

  if (items.length === 0) {
    area.innerHTML = '<div class="empty">暂无相关记忆</div>';
    return;
  }

  let combinedContent = items.map(m => {
    let header = `> **ID**: ${m.id} | **类型**: ${m.type} | **层级**: ${m.layer} | **鲜活度**: ${m.freshness}\n`;
    if (m.summary) header += `> **摘要**: ${m.summary}\n`;
    return header + '\n' + m.content;
  }).join('\n\n---\n\n');

  if (window.marked) {
    area.innerHTML = `<div class="memory-rendered">${window.marked.parse(combinedContent)}</div>`;
  } else {
    area.innerHTML = `<pre class="memory-readonly">${escapeHtml(combinedContent)}</pre>`;
  }
}

function formatGameRole(moduleType) {
  const role = String(moduleType || '').trim().toLowerCase();
  if (role === 'coder') return '程序（职责：游戏逻辑与系统开发）';
  if (role === 'designer') return '策划（职责：玩法设计与数值配置）';
  if (role === 'art') return '美术（职责：视觉资产与动画）';
  return '程序（职责：游戏逻辑与系统开发）';
}
