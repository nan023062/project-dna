import { $, api, escapeHtml } from '../utils.js';

let _memories = [];
let _currentMemoryId = null;

const NODE_TYPE_NAME_TO_VALUE = {
  Project: 0,
  Department: 1,
  Group: 2,
  Team: 3
};

const NODE_TYPE_VALUE_TO_NAME = Object.fromEntries(
  Object.entries(NODE_TYPE_NAME_TO_VALUE).map(([k, v]) => [v, k])
);

const TYPE_NAME_TO_VALUE = {
  Structural: 0,
  Semantic: 1,
  Episodic: 2,
  Working: 3,
  Procedural: 4
};

const TYPE_VALUE_TO_NAME = Object.fromEntries(
  Object.entries(TYPE_NAME_TO_VALUE).map(([k, v]) => [v, k])
);

const SOURCE_NAME_TO_VALUE = {
  Human: 2
};

const TEMPLATE_BY_NODE_TYPE = {
  Project: {
    summary: 'Project 全局愿景与边界',
    background: '项目整体背景',
    goal: '明确项目目标、边界与约束',
    rules: ['商业目标', '核心体验目标', '预算边界', '合规底线', '技术总选型'],
    steps: [],
    notes: ''
  },
  Department: {
    summary: 'Department 规则治理',
    background: '部门级治理规范',
    goal: '统一部门标准并协调资源冲突',
    rules: ['质量标准', '流程规范', '协作约束', '资源优先级'],
    steps: ['设计规范', '评审通过', '执行落地'],
    notes: ''
  },
  Group: {
    summary: 'Group 技术组规范',
    background: '技术组负责的业务域',
    goal: '固化工作流、接口与质量标准',
    rules: ['接口协议', '性能规格', '文件责任域', '依赖约束（DAG）'],
    steps: ['方案设计', '规范评审', '准入审核'],
    notes: ''
  },
  Team: {
    summary: 'Team 执行记录',
    background: '具体任务执行上下文',
    goal: '交付结果并沉淀复盘知识',
    rules: ['授权文件域', '交付验收标准', '过程记忆沉淀'],
    steps: ['领取任务', '执行开发', '提审交付', '复盘沉淀'],
    notes: ''
  }
};

function normalizeNodeTypeName(nodeType) {
  if (typeof nodeType === 'number') return NODE_TYPE_VALUE_TO_NAME[nodeType] ?? 'Group';
  if (nodeType === 'ProjectVision') return 'Project';
  if (nodeType === 'DisciplineStandard') return 'Department';
  if (nodeType === 'CrossDiscipline') return 'Team';
  if (nodeType === 'FeatureSystem') return 'Group';
  if (nodeType === 'Implementation') return 'Team';
  return nodeType || 'Group';
}

function normalizeTypeName(type) {
  if (typeof type === 'number') return TYPE_VALUE_TO_NAME[type] ?? 'Semantic';
  return type || 'Semantic';
}

function getCheckedDisciplines() {
  return Array.from(document.querySelectorAll('#memDisciplines input[type="checkbox"]:checked'))
    .map(i => i.value);
}

function setCheckedDisciplines(values = []) {
  const set = new Set(values);
  document.querySelectorAll('#memDisciplines input[type="checkbox"]').forEach(i => {
    i.checked = set.has(i.value);
  });
}

function renderListEditor(containerId, values = [], placeholder = '') {
  const container = $(containerId);
  container.innerHTML = '';
  values.forEach(v => addListItem(containerId, v, placeholder));
  if (values.length === 0) addListItem(containerId, '', placeholder);
}

function addListItem(containerId, value = '', placeholder = '') {
  const container = $(containerId);
  const row = document.createElement('div');
  row.className = 'memory-list-item';
  row.innerHTML = `
    <input type="text" value="${escapeHtml(value)}" placeholder="${escapeHtml(placeholder)}" />
    <button class="btn btn-secondary btn-sm" type="button">删除</button>
  `;
  row.querySelector('button').addEventListener('click', () => {
    row.remove();
    if (container.children.length === 0) {
      addListItem(containerId, '', placeholder);
    }
    updateGeneratedContent();
  });
  row.querySelector('input').addEventListener('input', updateGeneratedContent);
  container.appendChild(row);
}

function getListValues(containerId) {
  return Array.from($(containerId).querySelectorAll('input'))
    .map(i => i.value.trim())
    .filter(Boolean);
}

function splitLines(value) {
  return (value || '')
    .split('\n')
    .map(s => s.trim())
    .filter(Boolean);
}

function composeMarkdown() {
  const nodeType = $('memLayer').value;
  const type = $('memType').value;
  const background = $('memFieldBackground').value.trim();
  const goal = $('memFieldGoal').value.trim();
  const rules = splitLines($('memFieldRules').value);
  const steps = splitLines($('memFieldSteps').value);
  const notes = $('memFieldNotes').value.trim();

  const lines = [];
  lines.push(`## ${$('memSummary').value.trim() || '未命名记忆'}`);
  lines.push('');
  lines.push(`- 节点类型: ${nodeType}`);
  lines.push(`- 类型: ${type}`);
  if (background) lines.push(`- 背景: ${background}`);
  if (goal) lines.push(`- 目标: ${goal}`);
  lines.push('');

  if (rules.length > 0) {
    lines.push('### 规则要点');
    lines.push(...rules.map(r => `- ${r}`));
    lines.push('');
  }

  if (steps.length > 0) {
    lines.push('### 执行步骤');
    lines.push(...steps.map((s, idx) => `${idx + 1}. ${s}`));
    lines.push('');
  }

  if (notes) {
    lines.push('### 备注');
    lines.push(notes);
    lines.push('');
  }

  return lines.join('\n').trim();
}

function updateGeneratedContent() {
  $('memContent').value = composeMarkdown();
}

function bindStructuredFieldListeners() {
  ['memSummary', 'memLayer', 'memType', 'memFieldBackground', 'memFieldGoal', 'memFieldRules', 'memFieldSteps', 'memFieldNotes']
    .forEach(id => {
      const el = $(id);
      if (el) el.addEventListener('input', updateGeneratedContent);
      if (el && el.tagName === 'SELECT') el.addEventListener('change', updateGeneratedContent);
    });

  document.querySelectorAll('#memDisciplines input[type="checkbox"]').forEach(el => {
    el.addEventListener('change', updateGeneratedContent);
  });
}

function clearStructuredFields() {
  $('memFieldBackground').value = '';
  $('memFieldGoal').value = '';
  $('memFieldRules').value = '';
  $('memFieldSteps').value = '';
  $('memFieldNotes').value = '';
}

function fillTemplate(layerName) {
  const tpl = TEMPLATE_BY_NODE_TYPE[layerName] ?? TEMPLATE_BY_NODE_TYPE.Group;
  $('memSummary').value = tpl.summary;
  $('memFieldBackground').value = tpl.background;
  $('memFieldGoal').value = tpl.goal;
  $('memFieldRules').value = tpl.rules.join('\n');
  $('memFieldSteps').value = tpl.steps.join('\n');
  $('memFieldNotes').value = tpl.notes;
  updateGeneratedContent();
}

function isStructuredFieldsEmpty() {
  return ![
    $('memFieldBackground').value,
    $('memFieldGoal').value,
    $('memFieldRules').value,
    $('memFieldSteps').value,
    $('memFieldNotes').value,
    $('memContent').value
  ].some(v => String(v || '').trim().length > 0);
}

export function onLayerTypeChanged() {
  if (isStructuredFieldsEmpty()) {
    fillTemplate($('memLayer').value);
  }
}

export async function loadMemories() {
  const nodeType = $('memFilterLayer').value;
  const type = $('memFilterType').value;

  let url = '/memory/query?limit=100';
  if (nodeType) url += `&nodeTypes=${nodeType}`;
  if (type) url += `&types=${type}`;

  try {
    _memories = await api(url);
    renderMemoryList();
  } catch (err) {
    $('memoryList').innerHTML = `<div class="empty error">加载失败: ${err.message}</div>`;
  }
}

function renderMemoryList() {
  const container = $('memoryList');
  if (_memories.length === 0) {
    container.innerHTML = '<div class="empty">没有找到记忆</div>';
    return;
  }

  container.innerHTML = _memories.map(m => `
    <div class="memory-item ${_currentMemoryId === m.id ? 'active' : ''}" data-id="${m.id}">
      <div class="memory-item-title">${escapeHtml(m.summary || m.content.substring(0, 30) + '...')}</div>
      <div class="memory-item-meta">
        <span>[${escapeHtml(normalizeNodeTypeName(m.nodeType ?? m.layer))}] ${escapeHtml(normalizeTypeName(m.type))}</span>
        <span>${escapeHtml(m.freshness)}</span>
      </div>
    </div>
  `).join('');

  container.querySelectorAll('.memory-item').forEach(el => {
    el.addEventListener('click', () => selectMemory(el.dataset.id));
  });
}

export function selectMemory(id) {
  _currentMemoryId = id;
  renderMemoryList();

  const memory = _memories.find(m => m.id === id);
  if (!memory) return;

  $('memoryEditorForm').style.display = 'block';
  $('memId').textContent = memory.id;
  $('memSummary').value = memory.summary || '';
  $('memType').value = normalizeTypeName(memory.type);
  $('memLayer').value = normalizeNodeTypeName(memory.nodeType ?? memory.layer);
  $('memImportance').value = memory.importance || 0.5;

  setCheckedDisciplines(memory.disciplines || []);
  renderListEditor('memFeaturesList', memory.features || [], '例如：character');
  renderListEditor('memNodeIdField', memory.nodeId ? [memory.nodeId] : [], '例如：node-id');
  renderListEditor('memTagsList', memory.tags || [], '例如：#lesson');

  clearStructuredFields();
  $('memFieldNotes').value = memory.content || '';
  updateGeneratedContent();

  $('btnDeleteMemory').style.display = 'inline-block';
}

export function createNew() {
  _currentMemoryId = null;
  renderMemoryList();

  $('memoryEditorForm').style.display = 'block';
  $('memId').textContent = '新建记忆';
  $('memSummary').value = '';
  $('memType').value = 'Semantic';
  $('memLayer').value = 'Group';
  $('memImportance').value = 0.8;

  setCheckedDisciplines([]);
  renderListEditor('memFeaturesList', [], '例如：character');
  renderListEditor('memNodeIdField', [], '例如：node-id');
  renderListEditor('memTagsList', [], '例如：#lesson');

  clearStructuredFields();
  fillTemplate('Group');

  $('btnDeleteMemory').style.display = 'none';
}

export function addFeature() {
  addListItem('memFeaturesList', '', '例如：character');
}

export function addNodeId() {
  addListItem('memNodeIdField', '', '例如：node-id');
}

export function addTag() {
  addListItem('memTagsList', '', '例如：#lesson');
}

export async function saveMemory() {
  updateGeneratedContent();

  const typeName = $('memType').value;
  const nodeTypeName = $('memLayer').value;

  const request = {
    source: SOURCE_NAME_TO_VALUE.Human,
    summary: $('memSummary').value.trim() || null,
    type: TYPE_NAME_TO_VALUE[typeName] ?? TYPE_NAME_TO_VALUE.Semantic,
    nodeType: NODE_TYPE_NAME_TO_VALUE[nodeTypeName] ?? NODE_TYPE_NAME_TO_VALUE.Group,
    disciplines: getCheckedDisciplines(),
    features: getListValues('memFeaturesList'),
    nodeId: getListValues('memNodeIdField')[0] || null,
    tags: getListValues('memTagsList'),
    importance: parseFloat($('memImportance').value) || 0.5,
    content: $('memContent').value.trim()
  };

  if (!request.content) {
    _showToast('正文内容不能为空', true);
    return;
  }

  try {
    if (_currentMemoryId) {
      await api(`/memory/${_currentMemoryId}`, {
        method: 'PUT',
        body: request
      });
      _showToast('更新成功');
    } else {
      await api('/memory/remember', {
        method: 'POST',
        body: request
      });
      _showToast('创建成功');
    }
    await loadMemories();
  } catch (err) {
    _showToast('保存失败: ' + err.message, true);
  }
}

export async function deleteMemory() {
  if (!_currentMemoryId) return;

  const confirmed = await _showConfirmModal(
    '删除记忆',
    '确定要删除这条记忆吗？此操作不可恢复。'
  );
  if (!confirmed) return;

  try {
    await api(`/memory/${_currentMemoryId}`, { method: 'DELETE' });
    _showToast('删除成功');
    $('memoryEditorForm').style.display = 'none';
    await loadMemories();
  } catch (err) {
    _showToast('删除失败: ' + err.message, true);
  }
}

function _showConfirmModal(title, message) {
  return new Promise(resolve => {
    const overlay = document.createElement('div');
    overlay.className = 'ui-dialog-overlay';
    overlay.style.display = 'flex';
    overlay.style.position = 'fixed';
    overlay.style.inset = '0';
    overlay.style.zIndex = '9999';
    overlay.style.background = 'rgba(0,0,0,0.5)';
    overlay.style.alignItems = 'center';
    overlay.style.justifyContent = 'center';

    overlay.innerHTML = `
      <div style="background:var(--card-bg,#1e1e2e);border:1px solid var(--border-color,#333);border-radius:8px;padding:24px;max-width:400px;width:90%;">
        <h3 style="margin:0 0 12px;color:var(--text-primary,#e0e0e0)">${escapeHtml(title)}</h3>
        <p style="margin:0 0 20px;color:var(--text-secondary,#aaa)">${escapeHtml(message)}</p>
        <div style="display:flex;gap:8px;justify-content:flex-end">
          <button class="btn-cancel" style="padding:6px 16px;border:1px solid var(--border-color,#555);border-radius:4px;background:transparent;color:var(--text-secondary,#aaa);cursor:pointer">取消</button>
          <button class="btn-confirm" style="padding:6px 16px;border:none;border-radius:4px;background:#e74c3c;color:#fff;cursor:pointer">删除</button>
        </div>
      </div>`;

    const close = (result) => { overlay.remove(); resolve(result); };
    overlay.querySelector('.btn-cancel').onclick = () => close(false);
    overlay.querySelector('.btn-confirm').onclick = () => close(true);
    overlay.addEventListener('click', e => { if (e.target === overlay) close(false); });
    document.body.appendChild(overlay);
  });
}

function _showToast(msg, isError = false) {
  const el = document.createElement('div');
  el.textContent = msg;
  Object.assign(el.style, {
    position: 'fixed', bottom: '20px', right: '20px', zIndex: '10000',
    padding: '10px 20px', borderRadius: '6px', fontSize: '14px',
    color: '#fff', background: isError ? '#e74c3c' : '#27ae60',
    boxShadow: '0 2px 8px rgba(0,0,0,0.3)', transition: 'opacity 0.3s'
  });
  document.body.appendChild(el);
  setTimeout(() => { el.style.opacity = '0'; setTimeout(() => el.remove(), 300); }, 2500);
}

export function applyTemplate() {
  fillTemplate($('memLayer').value);
}

bindStructuredFieldListeners();
