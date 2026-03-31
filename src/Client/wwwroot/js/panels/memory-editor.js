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
let _submissions = [];
let _currentMemoryId = null;
let _latestSubmissionId = null;

const REVIEW_STATUS_META = {
  0: { key: 'draft', label: '草稿' },
  1: { key: 'pending', label: '待审核' },
  2: { key: 'approved', label: '已通过' },
  3: { key: 'rejected', label: '已驳回' },
  4: { key: 'published', label: '已发布' },
  5: { key: 'withdrawn', label: '已撤回' },
  6: { key: 'superseded', label: '已替代' }
};

const REVIEW_OPERATION_LABEL = {
  0: '新增',
  1: '修改',
  2: '删除'
};

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

function normalizeStatusMeta(status) {
  if (typeof status === 'number' && REVIEW_STATUS_META[status]) {
    return REVIEW_STATUS_META[status];
  }

  const name = String(status ?? '').toLowerCase();
  return Object.values(REVIEW_STATUS_META).find(meta => meta.key === name) ?? REVIEW_STATUS_META[1];
}

function normalizeOperationLabel(operation) {
  if (typeof operation === 'number' && REVIEW_OPERATION_LABEL[operation]) {
    return REVIEW_OPERATION_LABEL[operation];
  }

  const name = String(operation ?? '').toLowerCase();
  if (name === 'create') return REVIEW_OPERATION_LABEL[0];
  if (name === 'update') return REVIEW_OPERATION_LABEL[1];
  if (name === 'delete') return REVIEW_OPERATION_LABEL[2];
  return '变更';
}

function unwrapListResult(result) {
  if (Array.isArray(result)) return result;
  if (Array.isArray(result?.value)) return result.value;
  return [];
}

function sortSubmissionsByRecent(submissions) {
  return submissions
    .slice()
    .sort((a, b) => {
      const aTime = Date.parse(a?.updatedAt ?? a?.createdAt ?? '') || parseMemoryTimestamp(a);
      const bTime = Date.parse(b?.updatedAt ?? b?.createdAt ?? '') || parseMemoryTimestamp(b);
      return bTime - aTime;
    });
}

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

function formatDateTime(value) {
  if (!value) return '未知时间';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  return date.toLocaleString('zh-CN', { hour12: false });
}

function formatSubmissionTitle(submission) {
  return submission?.title ||
    submission?.proposedPayload?.summary ||
    submission?.normalizedPayload?.summary ||
    `${normalizeOperationLabel(submission?.operation)}申请`;
}

function formatSubmissionMeta(submission) {
  const status = normalizeStatusMeta(submission?.status);
  if (status.key === 'published' && submission?.publishedTargetId) {
    return `已发布到正式知识 ID：${submission.publishedTargetId}`;
  }
  if (status.key === 'approved') {
    return '已通过审核，等待管理员发布到正式知识库。';
  }
  if (status.key === 'rejected') {
    return submission?.reviewNote ? `已驳回：${submission.reviewNote}` : '已驳回，请根据审核意见调整后重新提交。';
  }
  if (status.key === 'withdrawn') {
    return '该申请已由提交者撤回。';
  }
  return '该申请正在预审队列中，等待管理员审核与发布。';
}

function setEditorHint(text) {
  const hint = $('memoryEditorHint');
  if (hint) hint.textContent = text;
}

function setSubmissionNotice(submission) {
  const notice = $('submissionNotice');
  if (!notice) return;

  if (!submission) {
    notice.classList.add('hidden');
    notice.textContent = '';
    return;
  }

  const operation = normalizeOperationLabel(submission.operation);
  const status = normalizeStatusMeta(submission.status);
  const tail = submission.publishedTargetId
    ? `正式知识 ID：${submission.publishedTargetId}`
    : '正式知识库尚未发布，当前变更仍停留在预审队列。';

  notice.textContent =
    `${operation}申请当前状态：${status.label}。提交单号：${submission.id}。${tail}`;
  notice.classList.remove('hidden');
}

function renderMemoryList() {
  const container = $('memoryList');
  if (_memories.length === 0) {
    container.innerHTML = '<div class="empty">没有找到正式知识</div>';
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
    element.addEventListener('click', () => {
      selectMemory(element.dataset.id);
    });
  });
}

function renderSubmissionList() {
  const container = $('submissionList');
  if (!container) return;

  if (_submissions.length === 0) {
    container.innerHTML = '<div class="submission-empty">你还没有提交过预审申请。新的创建、修改和删除操作会显示在这里。</div>';
    return;
  }

  container.innerHTML = _submissions.map(submission => {
    const status = normalizeStatusMeta(submission.status);
    const operation = normalizeOperationLabel(submission.operation);
    return `
      <div class="submission-item ${_latestSubmissionId === submission.id ? 'active' : ''}">
        <div class="submission-item-header">
          <span class="submission-op">${escapeHtml(operation)}</span>
          <span class="submission-status ${escapeHtml(status.key)}">${escapeHtml(status.label)}</span>
        </div>
        <div class="submission-title">${escapeHtml(formatSubmissionTitle(submission))}</div>
        <div class="submission-meta">更新时间：${escapeHtml(formatDateTime(submission.updatedAt || submission.createdAt))}</div>
        <div class="submission-meta">${escapeHtml(formatSubmissionMeta(submission))}</div>
      </div>
    `;
  }).join('');
}

async function refreshLists() {
  await Promise.all([loadMemories(), loadSubmissions()]);
}

function rememberLatestSubmission(submission) {
  _latestSubmissionId = submission?.id ?? null;
  setSubmissionNotice(submission);
}

function hideEmptyState() {
  $('memoryEmptyState')?.classList.add('hidden');
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
    _memories = sortMemoriesByRecent(unwrapListResult(result));
    renderMemoryList();
  } catch (error) {
    $('memoryList').innerHTML = `<div class="empty error">加载正式知识失败: ${escapeHtml(error.message)}</div>`;
    throw error;
  }
}

export async function loadSubmissions() {
  try {
    const result = await api('/review/memory/submissions/mine');
    _submissions = sortSubmissionsByRecent(unwrapListResult(result));
    renderSubmissionList();
  } catch (error) {
    $('submissionList').innerHTML = `<div class="submission-empty error">加载预审记录失败: ${escapeHtml(error.message)}</div>`;
    throw error;
  }
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
  renderListEditor('memFeaturesList', memory.features || [], '例如 review-flow');
  renderListEditor('memNodeIdField', memory.nodeId ? [memory.nodeId] : [], '例如 Dna.Server');
  renderListEditor('memTagsList', memory.tags || [], '例如 #decision');

  clearStructuredFields();
  $('memFieldNotes').value = memory.content || '';
  updateGeneratedContent();

  $('btnDeleteMemory').style.display = 'inline-block';
  setEditorHint('编辑正式知识不会直接写入正式库，保存后会生成一条对应的预审申请，等待管理员审核与发布。');
  setSubmissionNotice(null);
  hideEmptyState();
}

export function createNew() {
  syncNodeTypeSelectOptions();
  _currentMemoryId = null;
  renderMemoryList();

  $('memoryEditorForm').style.display = 'block';
  $('memId').textContent = '新建预审申请';
  $('memSummary').value = '';
  $('memType').value = 'Semantic';
  $('memLayer').value = 'Technical';
  $('memImportance').value = 0.8;

  setCheckedDisciplines([]);
  renderListEditor('memFeaturesList', [], '例如 review-flow');
  renderListEditor('memNodeIdField', [], '例如 Dna.Server');
  renderListEditor('memTagsList', [], '例如 #lesson');

  clearStructuredFields();
  fillTemplate('Technical');

  $('btnDeleteMemory').style.display = 'none';
  setEditorHint('新建内容会先进入预审队列。审核发布前，正式知识库中不会立即出现这条知识。');
  setSubmissionNotice(null);
  hideEmptyState();
}

export function addFeature() {
  addListItem('memFeaturesList', '', '例如 review-flow');
}

export function addNodeId() {
  addListItem('memNodeIdField', '', '例如 Dna.Server');
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
    const submission = _currentMemoryId
      ? await api('/review/memory/submissions', {
          method: 'POST',
          body: {
            operation: 'update',
            targetId: _currentMemoryId,
            memory: request
          }
        })
      : await api('/review/memory/submissions', {
          method: 'POST',
          body: {
            operation: 'create',
            memory: request
          }
        });

    rememberLatestSubmission(submission);
    await refreshLists();
    _showToast(
      _currentMemoryId
        ? '修改申请已提交，正式知识不会立即变更。'
        : '新建申请已提交，正式知识将在审核发布后可见。'
    );
  } catch (error) {
    _showToast(`提交预审失败: ${error.message}`, true);
  }
}

export async function deleteMemory() {
  if (!_currentMemoryId) return;

  const confirmed = await _showConfirmModal(
    '提交删除申请',
    '确定要把这条正式知识提交为“删除申请”吗？在管理员审核并发布前，正式知识不会立即删除。'
  );
  if (!confirmed) return;

  try {
    const submission = await api('/review/memory/submissions', {
      method: 'POST',
      body: {
        operation: 'delete',
        targetId: _currentMemoryId
      }
    });

    rememberLatestSubmission(submission);
    await refreshLists();
    _showToast('删除申请已提交，等待管理员审核。');
  } catch (error) {
    _showToast(`提交删除申请失败: ${error.message}`, true);
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
      <div style="background:var(--card-bg,#1e1e2e);border:1px solid var(--border-color,#333);border-radius:8px;padding:24px;max-width:420px;width:90%;">
        <h3 style="margin:0 0 12px;color:var(--text-primary,#e0e0e0)">${escapeHtml(title)}</h3>
        <p style="margin:0 0 20px;color:var(--text-secondary,#aaa);line-height:1.6">${escapeHtml(message)}</p>
        <div style="display:flex;gap:8px;justify-content:flex-end">
          <button class="btn-cancel" style="padding:6px 16px;border:1px solid var(--border-color,#555);border-radius:4px;background:transparent;color:var(--text-secondary,#aaa);cursor:pointer">取消</button>
          <button class="btn-confirm" style="padding:6px 16px;border:none;border-radius:4px;background:#e74c3c;color:#fff;cursor:pointer">确认提交</button>
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
  element.textContent = message;
  Object.assign(element.style, {
    position: 'fixed',
    bottom: '20px',
    right: '20px',
    zIndex: '10000',
    padding: '10px 20px',
    borderRadius: '6px',
    fontSize: '14px',
    color: '#fff',
    background: isError ? '#e74c3c' : '#27ae60',
    boxShadow: '0 2px 8px rgba(0,0,0,0.3)',
    transition: 'opacity 0.3s'
  });
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
