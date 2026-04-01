import { $, api, escapeHtml } from '../utils.js';
import {
  NODE_TYPE_NAME_TO_VALUE,
  SOURCE_NAME_TO_VALUE,
  TYPE_NAME_TO_VALUE,
  normalizeNodeTypeName,
  normalizeTypeName,
  parseMemoryTimestamp,
  sortMemoriesByRecent,
  syncNodeTypeSelectOptions
} from '/dna-shared/js/panels/memory-editor-common.js';
import {
  bindStructuredFieldListeners as bindSharedStructuredFieldListeners,
  clearStructuredFieldValues,
  fillStructuredTemplate,
  isStructuredDraftEmpty,
  readStructuredMemoryFields
} from '/dna-shared/js/panels/memory-editor-structured.js';

let _memories = [];
let _currentMemoryId = null;

const TEMPLATE_BY_NODE_TYPE = {
  Project: {
    summary: '项目结构与协作约定',
    background: '说明当前项目背景与上下文',
    goal: '描述这类知识希望支持的目标与结果',
    rules: ['关键边界与约束', '协作接口与输入输出', '验收标准或完成定义', '依赖与风险提醒', '需要同步给团队的注意事项'],
    steps: [],
    notes: ''
  },
  Department: {
    summary: '部门职责与协作方式',
    background: '说明该部门在项目中的定位',
    goal: '明确该部门产出、上下游和协作边界',
    rules: ['职责边界', '交付物清单', '接口人或协作对象', '常见风险与限制'],
    steps: ['梳理职责范围', '梳理输入输出', '沉淀协作规则'],
    notes: ''
  },
  Technical: {
    summary: '技术方案与实现约束',
    background: '说明当前技术背景、模块职责和上下文',
    goal: '明确实现目标、边界和质量要求',
    rules: ['接口与依赖约束', '兼容性与演进策略', '性能与稳定性要求', '与 Agent 协作时的注意点'],
    steps: ['识别关键上下文', '梳理接口与依赖', '整理实现步骤'],
    notes: ''
  },
  Team: {
    summary: '团队协作与运行机制',
    background: '记录团队成员、职责和协作背景',
    goal: '让团队沟通、交付和反馈机制更稳定',
    rules: ['沟通与同步频率', '需求流转和协作方式', '例会与评审约定'],
    steps: ['梳理协作角色', '明确同步节奏', '沉淀反馈机制', '持续复盘改进'],
    notes: ''
  }
};

function getCheckedDisciplines() {
  return Array.from(document.querySelectorAll('#memDisciplines input[type="checkbox"]:checked'))
    .map(input => input.value);
}

function setCheckedDisciplines(values = []) {
  const set = new Set(values);
  document.querySelectorAll('#memDisciplines input[type="checkbox"]').forEach(input => {
    input.checked = set.has(input.value);
  });
}

function renderListEditor(containerId, values = [], placeholder = '') {
  const container = $(containerId);
  container.innerHTML = '';
  values.forEach(value => addListItem(containerId, value, placeholder));
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
    .map(input => input.value.trim())
    .filter(Boolean);
}

function composeMarkdown() {
  const draft = readStructuredMemoryFields($);
  const nodeType = draft.nodeType;
  const type = draft.type;
  const background = draft.background;
  const goal = draft.goal;
  const rules = draft.rules;
  const steps = draft.steps;
  const notes = draft.notes;

  const lines = [];
  lines.push(`## ${draft.summary || '未命名知识'}`);
  lines.push('');
  lines.push(`- 节点类型: ${nodeType}`);
  lines.push(`- 知识类型: ${type}`);
  if (background) lines.push(`- 背景: ${background}`);
  if (goal) lines.push(`- 目标: ${goal}`);
  lines.push('');

  if (rules.length > 0) {
    lines.push('### 规则');
    lines.push(...rules.map(rule => `- ${rule}`));
    lines.push('');
  }

  if (steps.length > 0) {
    lines.push('### 步骤');
    lines.push(...steps.map((step, index) => `${index + 1}. ${step}`));
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
  bindSharedStructuredFieldListeners({
    getById: $,
    queryAll: selector => document.querySelectorAll(selector),
    onUpdate: updateGeneratedContent
  });
}

function clearStructuredFields() {
  clearStructuredFieldValues($);
}

function fillTemplate(layerName) {
  const template = TEMPLATE_BY_NODE_TYPE[layerName] ?? TEMPLATE_BY_NODE_TYPE.Technical;
  fillStructuredTemplate($, template);
  updateGeneratedContent();
}

function isStructuredFieldsEmpty() {
  return isStructuredDraftEmpty($);
}

export function onLayerTypeChanged() {
  if (isStructuredFieldsEmpty()) {
    fillTemplate($('memLayer').value);
  }
}

export async function loadMemories() {
  syncNodeTypeSelectOptions();
  const nodeType = $('memFilterLayer').value;
  const type = $('memFilterType').value;

  let url = '/memory/query?limit=100';
  if (nodeType) url += `&nodeTypes=${nodeType}`;
  if (type) url += `&types=${type}`;

  try {
    const result = await api(url);
    const memories = Array.isArray(result) ? result : [];
    _memories = sortMemoriesByRecent(memories);
    renderMemoryList();
  } catch (error) {
    $('memoryList').innerHTML = `<div class="empty error">加载知识失败: ${escapeHtml(error.message)}</div>`;
  }
}

function renderMemoryList() {
  const container = $('memoryList');
  if (_memories.length === 0) {
    container.innerHTML = '<div class="empty">没有找到知识</div>';
    return;
  }

  container.innerHTML = _memories.map(memory => `
    <div class="memory-item ${_currentMemoryId === memory.id ? 'active' : ''}" data-id="${memory.id}">
      <div class="memory-item-title">${escapeHtml(memory.summary || `${(memory.content || '').substring(0, 30)}...`)}</div>
      <div class="memory-item-meta">
        <span>[${escapeHtml(normalizeNodeTypeName(memory.nodeType ?? memory.layer))}] ${escapeHtml(normalizeTypeName(memory.type))}</span>
        <span>${escapeHtml(memory.freshness)}</span>
      </div>
    </div>
  `).join('');

  container.querySelectorAll('.memory-item').forEach(element => {
    element.addEventListener('click', () => selectMemory(element.dataset.id));
  });
}

export function selectMemory(id) {
  _currentMemoryId = id;
  renderMemoryList();

  const memory = _memories.find(item => item.id === id);
  if (!memory) return;

  $('memoryEditorForm').style.display = 'block';
  $('memId').textContent = memory.id;
  $('memSummary').value = memory.summary || '';
  $('memType').value = normalizeTypeName(memory.type);
  $('memLayer').value = normalizeNodeTypeName(memory.nodeType ?? memory.layer);
  $('memImportance').value = memory.importance || 0.5;

  setCheckedDisciplines(memory.disciplines || []);
  renderListEditor('memFeaturesList', memory.features || [], '例如 character');
  renderListEditor('memNodeIdField', memory.nodeId ? [memory.nodeId] : [], '例如 node-id');
  renderListEditor('memTagsList', memory.tags || [], '例如 #lesson');

  clearStructuredFields();
  $('memFieldNotes').value = memory.content || '';
  updateGeneratedContent();

  $('btnDeleteMemory').style.display = 'inline-block';
}

export function createNew() {
  syncNodeTypeSelectOptions();
  _currentMemoryId = null;
  renderMemoryList();

  $('memoryEditorForm').style.display = 'block';
  $('memId').textContent = '新增知识';
  $('memSummary').value = '';
  $('memType').value = 'Semantic';
  $('memLayer').value = 'Technical';
  $('memImportance').value = 0.8;

  setCheckedDisciplines([]);
  renderListEditor('memFeaturesList', [], '例如 character');
  renderListEditor('memNodeIdField', [], '例如 node-id');
  renderListEditor('memTagsList', [], '例如 #lesson');

  clearStructuredFields();
  fillTemplate('Technical');

  $('btnDeleteMemory').style.display = 'none';
}

export function addFeature() {
  addListItem('memFeaturesList', '', '例如 character');
}

export function addNodeId() {
  addListItem('memNodeIdField', '', '例如 node-id');
}

export function addTag() {
  addListItem('memTagsList', '', '例如 #lesson');
}

export async function saveMemory() {
  updateGeneratedContent();

  const draft = readStructuredMemoryFields($);
  const typeName = $('memType').value;
  const nodeTypeName = $('memLayer').value;

  const request = {
    source: SOURCE_NAME_TO_VALUE.Human,
    summary: draft.summary || null,
    type: TYPE_NAME_TO_VALUE[typeName] ?? TYPE_NAME_TO_VALUE.Semantic,
    nodeType: NODE_TYPE_NAME_TO_VALUE[nodeTypeName] ?? NODE_TYPE_NAME_TO_VALUE.Technical,
    disciplines: getCheckedDisciplines(),
    features: getListValues('memFeaturesList'),
    nodeId: getListValues('memNodeIdField')[0] || null,
    tags: getListValues('memTagsList'),
    importance: parseFloat($('memImportance').value) || 0.5,
    content: $('memContent').value.trim()
  };

  if (!request.content) {
    _showToast('正文内容不能为空。', true);
    return;
  }

  try {
    if (_currentMemoryId) {
      await api(`/memory/${_currentMemoryId}`, {
        method: 'PUT',
        body: request
      });
      _showToast('知识已更新。');
    } else {
      await api('/memory/remember', {
        method: 'POST',
        body: request
      });
      _showToast('知识已创建。');
    }
    await loadMemories();
  } catch (error) {
    _showToast(`保存失败: ${error.message}`, true);
  }
}

export async function deleteMemory() {
  if (!_currentMemoryId) return;

  const confirmed = await _showConfirmModal(
    '删除知识',
    '确定要删除这条正式知识吗？这个操作会直接影响正式知识库。'
  );
  if (!confirmed) return;

  try {
    await api(`/memory/${_currentMemoryId}`, { method: 'DELETE' });
    _showToast('知识已删除。');
    $('memoryEditorForm').style.display = 'none';
    await loadMemories();
  } catch (error) {
    _showToast(`删除失败: ${error.message}`, true);
  }
}

function _showConfirmModal(title, message) {
  return new Promise(resolve => {
    const overlay = document.createElement('div');
    overlay.className = 'ui-inline-modal';

    overlay.innerHTML = `
      <div class="ui-inline-modal-card">
        <h3 class="ui-inline-modal-title">${escapeHtml(title)}</h3>
        <p class="ui-inline-modal-copy">${escapeHtml(message)}</p>
        <div class="ui-inline-modal-actions">
          <button class="btn btn-secondary btn-sm ui-inline-cancel btn-cancel">取消</button>
          <button class="btn btn-danger btn-sm ui-inline-confirm btn-confirm">确认删除</button>
        </div>
      </div>`;

    const close = result => {
      overlay.remove();
      resolve(result);
    };
    overlay.querySelector('.btn-cancel').onclick = () => close(false);
    overlay.querySelector('.btn-confirm').onclick = () => close(true);
    overlay.addEventListener('click', event => {
      if (event.target === overlay) close(false);
    });
    document.body.appendChild(overlay);
  });
}

function _showToast(message, isError = false) {
  const element = document.createElement('div');
  element.className = `ui-toast ${isError ? 'error' : 'success'}`;
  element.textContent = message;
  document.body.appendChild(element);
  setTimeout(() => {
    element.style.opacity = '0';
    setTimeout(() => element.remove(), 300);
  }, 2500);
}

export function applyTemplate() {
  fillTemplate($('memLayer').value);
}

bindStructuredFieldListeners();
syncNodeTypeSelectOptions();
