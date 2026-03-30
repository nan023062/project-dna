import { $, api, escapeHtml } from '../utils.js';

let _memories = [];
let _submissions = [];
let _currentMemoryId = null;
let _latestSubmissionId = null;

const NODE_TYPE_NAME_TO_VALUE = {
  Project: 0,
  Department: 1,
  Technical: 2,
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

const REVIEW_STATUS_META = {
  0: { key: 'draft', label: '草稿' },
  1: { key: 'pending', label: '待审核' },
  2: { key: 'approved', label: '已通过' },
  3: { key: 'rejected', label: '已驳回' },
  4: { key: 'published', label: '已发布' },
  5: { key: 'withdrawn', label: '已撤回' },
  6: { key: 'superseded', label: '已过期' }
};

const REVIEW_OPERATION_LABEL = {
  0: '新建',
  1: '修改',
  2: '删除'
};

const NODE_TYPE_OPTIONS = [
  { value: 'Project', label: 'Project（项目）' },
  { value: 'Department', label: 'Department（部门）' },
  { value: 'Technical', label: 'Technical（技术组）' },
  { value: 'Team', label: 'Team（执行团队）' }
];

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
    summary: 'Department 治理规则',
    background: '部门级治理背景',
    goal: '统一部门标准并协调资源冲突',
    rules: ['质量标准', '流程规范', '协作约束', '资源优先级'],
    steps: ['设计规范', '评审通过', '执行落地'],
    notes: ''
  },
  Technical: {
    summary: 'Technical 组规范',
    background: '技术组负责的业务域',
    goal: '固化工作流、接口与质量标准',
    rules: ['接口协议', '性能规格', '文件责任边界', '依赖约束（DAG）'],
    steps: ['方案设计', '规范评审', '准入审核'],
    notes: ''
  },
  Team: {
    summary: 'Team 执行记录',
    background: '具体任务执行上下文',
    goal: '沉淀交付结果与复盘知识',
    rules: ['授权文件边界', '交付验收标准', '过程记忆沉淀'],
    steps: ['领取任务', '执行开发', '提审交付', '复盘沉淀'],
    notes: ''
  }
};

function normalizeNodeTypeName(nodeType) {
  if (typeof nodeType === 'number') return NODE_TYPE_VALUE_TO_NAME[nodeType] ?? 'Technical';
  if (nodeType === 'ProjectVision') return 'Project';
  if (nodeType === 'DisciplineStandard') return 'Department';
  if (nodeType === 'CrossDiscipline') return 'Team';
  if (nodeType === 'FeatureSystem') return 'Technical';
  if (nodeType === 'Implementation') return 'Team';
  if (nodeType === 'Group') return 'Technical';
  return nodeType || 'Technical';
}

function normalizeTypeName(type) {
  if (typeof type === 'number') return TYPE_VALUE_TO_NAME[type] ?? 'Semantic';
  return type || 'Semantic';
}

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

function syncNodeTypeSelectOptions() {
  const filterSelect = $('memFilterLayer');
  if (filterSelect) {
    const current = normalizeNodeTypeName(filterSelect.value || '');
    filterSelect.innerHTML = [
      '<option value="">所有节点类型</option>',
      ...NODE_TYPE_OPTIONS.map(opt => `<option value="${opt.value}">${opt.label}</option>`)
    ].join('');
    filterSelect.value = NODE_TYPE_OPTIONS.some(opt => opt.value === current) ? current : '';
  }

  const editorSelect = $('memLayer');
  if (editorSelect) {
    const current = normalizeNodeTypeName(editorSelect.value || '');
    editorSelect.innerHTML = NODE_TYPE_OPTIONS
      .map(opt => `<option value="${opt.value}">${opt.label}</option>`)
      .join('');
    editorSelect.value = NODE_TYPE_OPTIONS.some(opt => opt.value === current) ? current : 'Technical';
  }
}

function parseMemoryTimestamp(memory) {
  const created = memory?.createdAt ?? memory?.created_at;
  if (!created) return 0;
  const timestamp = Date.parse(created);
  return Number.isFinite(timestamp) ? timestamp : 0;
}

function sortMemoriesByRecent(memories) {
  return memories
    .map((memory, index) => ({ memory, index, timestamp: parseMemoryTimestamp(memory) }))
    .sort((a, b) => {
      if (b.timestamp !== a.timestamp) return b.timestamp - a.timestamp;
      return a.index - b.index;
    })
    .map(item => item.memory);
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

function splitLines(value) {
  return (value || '')
    .split('\n')
    .map(item => item.trim())
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
  lines.push(`## ${$('memSummary').value.trim() || '未命名知识'}`);
  lines.push('');
  lines.push(`- 节点类型: ${nodeType}`);
  lines.push(`- 记忆类型: ${type}`);
  if (background) lines.push(`- 背景: ${background}`);
  if (goal) lines.push(`- 目标: ${goal}`);
  lines.push('');

  if (rules.length > 0) {
    lines.push('### 规则要点');
    lines.push(...rules.map(rule => `- ${rule}`));
    lines.push('');
  }

  if (steps.length > 0) {
    lines.push('### 执行步骤');
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
  [
    'memSummary',
    'memLayer',
    'memType',
    'memFieldBackground',
    'memFieldGoal',
    'memFieldRules',
    'memFieldSteps',
    'memFieldNotes'
  ].forEach(id => {
    const element = $(id);
    if (!element) return;
    element.addEventListener('input', updateGeneratedContent);
    if (element.tagName === 'SELECT') element.addEventListener('change', updateGeneratedContent);
  });

  document.querySelectorAll('#memDisciplines input[type="checkbox"]').forEach(element => {
    element.addEventListener('change', updateGeneratedContent);
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
  const template = TEMPLATE_BY_NODE_TYPE[layerName] ?? TEMPLATE_BY_NODE_TYPE.Technical;
  $('memSummary').value = template.summary;
  $('memFieldBackground').value = template.background;
  $('memFieldGoal').value = template.goal;
  $('memFieldRules').value = template.rules.join('\n');
  $('memFieldSteps').value = template.steps.join('\n');
  $('memFieldNotes').value = template.notes;
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
  ].some(value => String(value || '').trim().length > 0);
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
    `${normalizeOperationLabel(submission?.operation)}提审`;
}

function formatSubmissionMeta(submission) {
  const status = normalizeStatusMeta(submission?.status);
  if (status.key === 'published' && submission?.publishedTargetId) {
    return `已发布到正式库：${submission.publishedTargetId}`;
  }
  if (status.key === 'approved') {
    return '已审核通过，等待发布。';
  }
  if (status.key === 'rejected') {
    return submission?.reviewNote ? `已驳回：${submission.reviewNote}` : '已被管理员驳回。';
  }
  if (status.key === 'withdrawn') {
    return '该提审已被撤回。';
  }
  return '正式库不会立即变更，请等待管理员处理。';
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
    : '可在“我的提审”中继续跟踪状态。';

  notice.textContent =
    `${operation}提审已创建，当前状态：${status.label}。提交编号：${submission.id}。${tail}`;
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
      <div class="memory-item-title">${escapeHtml(memory.summary || `${memory.content.substring(0, 30)}...`)}</div>
      <div class="memory-item-meta">
        <span>[${escapeHtml(normalizeNodeTypeName(memory.nodeType ?? memory.layer))}] ${escapeHtml(normalizeTypeName(memory.type))}</span>
        <span>${escapeHtml(memory.freshness)}</span>
      </div>
    </div>
  `).join('');

  container.querySelectorAll('.memory-item').forEach(element => {
    element.addEventListener('click', () => {
      if (window.MemoryEditor?.selectMemory) {
        window.MemoryEditor.selectMemory(element.dataset.id);
        return;
      }
      selectMemory(element.dataset.id);
    });
  });
}

function renderSubmissionList() {
  const container = $('submissionList');
  if (!container) return;

  if (_submissions.length === 0) {
    container.innerHTML = '<div class="submission-empty">还没有任何提审记录。新建、修改、删除正式知识后，会在这里看到状态。</div>';
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
        <div class="submission-meta">提交时间：${escapeHtml(formatDateTime(submission.updatedAt || submission.createdAt))}</div>
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
    $('memoryList').innerHTML = `<div class="empty error">加载失败: ${escapeHtml(error.message)}</div>`;
    throw error;
  }
}

export async function loadSubmissions() {
  try {
    const result = await api('/review/memory/submissions/mine');
    _submissions = sortSubmissionsByRecent(unwrapListResult(result));
    renderSubmissionList();
  } catch (error) {
    $('submissionList').innerHTML = `<div class="submission-empty error">提审列表加载失败: ${escapeHtml(error.message)}</div>`;
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
  renderListEditor('memFeaturesList', memory.features || [], '例如：review-flow');
  renderListEditor('memNodeIdField', memory.nodeId ? [memory.nodeId] : [], '例如：Dna.Server');
  renderListEditor('memTagsList', memory.tags || [], '例如：#decision');

  clearStructuredFields();
  $('memFieldNotes').value = memory.content || '';
  updateGeneratedContent();

  $('btnDeleteMemory').style.display = 'inline-block';
  setEditorHint('你正在查看正式知识。点击“提交审核”会生成一条修改提案，不会直接覆盖正式库。');
  setSubmissionNotice(null);
}

export function createNew() {
  syncNodeTypeSelectOptions();
  _currentMemoryId = null;
  renderMemoryList();

  $('memoryEditorForm').style.display = 'block';
  $('memId').textContent = '新建提审';
  $('memSummary').value = '';
  $('memType').value = 'Semantic';
  $('memLayer').value = 'Technical';
  $('memImportance').value = 0.8;

  setCheckedDisciplines([]);
  renderListEditor('memFeaturesList', [], '例如：review-flow');
  renderListEditor('memNodeIdField', [], '例如：Dna.Server');
  renderListEditor('memTagsList', [], '例如：#lesson');

  clearStructuredFields();
  fillTemplate('Technical');

  $('btnDeleteMemory').style.display = 'none';
  setEditorHint('新建内容会进入预审队列。正式库在审核发布前不会出现这条知识。');
  setSubmissionNotice(null);
}

export function addFeature() {
  addListItem('memFeaturesList', '', '例如：review-flow');
}

export function addNodeId() {
  addListItem('memNodeIdField', '', '例如：Dna.Server');
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
        ? '修改提审已提交，正式知识不会立即变更。'
        : '新建提审已提交，可在“我的提审”中跟踪状态。'
    );
  } catch (error) {
    _showToast(`提交审核失败: ${error.message}`, true);
  }
}

export async function deleteMemory() {
  if (!_currentMemoryId) return;

  const confirmed = await _showConfirmModal(
    '提交删除申请',
    '确定要把这条正式知识提交为“删除申请”吗？在管理员审核并发布之前，正式知识不会立即删除。'
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
    _showToast('删除申请已提交审核。');
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
          <button class="btn-confirm" style="padding:6px 16px;border:none;border-radius:4px;background:#e74c3c;color:#fff;cursor:pointer">继续提交</button>
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
