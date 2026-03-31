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
  0: { key: 'draft', label: '鑽夌' },
  1: { key: 'pending', label: '寰呭鏍? },
  2: { key: 'approved', label: '宸查€氳繃' },
  3: { key: 'rejected', label: '宸查┏鍥? },
  4: { key: 'published', label: '宸插彂甯? },
  5: { key: 'withdrawn', label: '宸叉挙鍥? },
  6: { key: 'superseded', label: '宸茶繃鏈? }
};

const REVIEW_OPERATION_LABEL = {
  0: '鏂板缓',
  1: '淇敼',
  2: '鍒犻櫎'
};

const TEMPLATE_BY_NODE_TYPE = {
  Project: {
    summary: 'Project 鍏ㄥ眬鎰挎櫙涓庤竟鐣?,
    background: '椤圭洰鏁翠綋鑳屾櫙',
    goal: '鏄庣‘椤圭洰鐩爣銆佽竟鐣屼笌绾︽潫',
    rules: ['鍟嗕笟鐩爣', '鏍稿績浣撻獙鐩爣', '棰勭畻杈圭晫', '鍚堣搴曠嚎', '鎶€鏈€婚€夊瀷'],
    steps: [],
    notes: ''
  },
  Department: {
    summary: 'Department 娌荤悊瑙勫垯',
    background: '閮ㄩ棬绾ф不鐞嗚儗鏅?,
    goal: '缁熶竴閮ㄩ棬鏍囧噯骞跺崗璋冭祫婧愬啿绐?,
    rules: ['璐ㄩ噺鏍囧噯', '娴佺▼瑙勮寖', '鍗忎綔绾︽潫', '璧勬簮浼樺厛绾?],
    steps: ['璁捐瑙勮寖', '璇勫閫氳繃', '鎵ц钀藉湴'],
    notes: ''
  },
  Technical: {
    summary: 'Technical 缁勮鑼?,
    background: '鎶€鏈粍璐熻矗鐨勪笟鍔″煙',
    goal: '鍥哄寲宸ヤ綔娴併€佹帴鍙ｄ笌璐ㄩ噺鏍囧噯',
    rules: ['鎺ュ彛鍗忚', '鎬ц兘瑙勬牸', '鏂囦欢璐ｄ换杈圭晫', '渚濊禆绾︽潫锛圖AG锛?],
    steps: ['鏂规璁捐', '瑙勮寖璇勫', '鍑嗗叆瀹℃牳'],
    notes: ''
  },
  Team: {
    summary: 'Team 鎵ц璁板綍',
    background: '鍏蜂綋浠诲姟鎵ц涓婁笅鏂?,
    goal: '娌夋穩浜や粯缁撴灉涓庡鐩樼煡璇?,
    rules: ['鎺堟潈鏂囦欢杈圭晫', '浜や粯楠屾敹鏍囧噯', '杩囩▼璁板繂娌夋穩'],
    steps: ['棰嗗彇浠诲姟', '鎵ц寮€鍙?, '鎻愬浜や粯', '澶嶇洏娌夋穩'],
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
  return '鍙樻洿';
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
    <button class="btn btn-secondary btn-sm" type="button">鍒犻櫎</button>
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
  lines.push(`## ${draft.summary || '鏈懡鍚嶇煡璇?}`);
  lines.push('');
  lines.push(`- 鑺傜偣绫诲瀷: ${nodeType}`);
  lines.push(`- 璁板繂绫诲瀷: ${type}`);
  if (background) lines.push(`- 鑳屾櫙: ${background}`);
  if (goal) lines.push(`- 鐩爣: ${goal}`);
  lines.push('');

  if (rules.length > 0) {
    lines.push('### 瑙勫垯瑕佺偣');
    lines.push(...rules.map(rule => `- ${rule}`));
    lines.push('');
  }

  if (steps.length > 0) {
    lines.push('### 鎵ц姝ラ');
    lines.push(...steps.map((step, index) => `${index + 1}. ${step}`));
    lines.push('');
  }

  if (notes) {
    lines.push('### 澶囨敞');
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
  if (!value) return '鏈煡鏃堕棿';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);
  return date.toLocaleString('zh-CN', { hour12: false });
}

function formatSubmissionTitle(submission) {
  return submission?.title ||
    submission?.proposedPayload?.summary ||
    submission?.normalizedPayload?.summary ||
    `${normalizeOperationLabel(submission?.operation)}鎻愬`;
}

function formatSubmissionMeta(submission) {
  const status = normalizeStatusMeta(submission?.status);
  if (status.key === 'published' && submission?.publishedTargetId) {
    return `宸插彂甯冨埌姝ｅ紡搴擄細${submission.publishedTargetId}`;
  }
  if (status.key === 'approved') {
    return '宸插鏍搁€氳繃锛岀瓑寰呭彂甯冦€?;
  }
  if (status.key === 'rejected') {
    return submission?.reviewNote ? `宸查┏鍥烇細${submission.reviewNote}` : '宸茶绠＄悊鍛橀┏鍥炪€?;
  }
  if (status.key === 'withdrawn') {
    return '璇ユ彁瀹″凡琚挙鍥炪€?;
  }
  return '姝ｅ紡搴撲笉浼氱珛鍗冲彉鏇达紝璇风瓑寰呯鐞嗗憳澶勭悊銆?;
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
    ? `姝ｅ紡鐭ヨ瘑 ID锛?{submission.publishedTargetId}`
    : '鍙湪鈥滄垜鐨勬彁瀹♀€濅腑缁х画璺熻釜鐘舵€併€?;

  notice.textContent =
    `${operation}鎻愬宸插垱寤猴紝褰撳墠鐘舵€侊細${status.label}銆傛彁浜ょ紪鍙凤細${submission.id}銆?{tail}`;
  notice.classList.remove('hidden');
}

function renderMemoryList() {
  const container = $('memoryList');
  if (_memories.length === 0) {
    container.innerHTML = '<div class="empty">娌℃湁鎵惧埌姝ｅ紡鐭ヨ瘑</div>';
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
      selectMemory(element.dataset.id);
    });
  });
}

function renderSubmissionList() {
  const container = $('submissionList');
  if (!container) return;

  if (_submissions.length === 0) {
    container.innerHTML = '<div class="submission-empty">杩樻病鏈変换浣曟彁瀹¤褰曘€傛柊寤恒€佷慨鏀广€佸垹闄ゆ寮忕煡璇嗗悗锛屼細鍦ㄨ繖閲岀湅鍒扮姸鎬併€?/div>';
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
        <div class="submission-meta">鎻愪氦鏃堕棿锛?{escapeHtml(formatDateTime(submission.updatedAt || submission.createdAt))}</div>
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
    $('memoryList').innerHTML = `<div class="empty error">鍔犺浇澶辫触: ${escapeHtml(error.message)}</div>`;
    throw error;
  }
}

export async function loadSubmissions() {
  try {
    const result = await api('/review/memory/submissions/mine');
    _submissions = sortSubmissionsByRecent(unwrapListResult(result));
    renderSubmissionList();
  } catch (error) {
    $('submissionList').innerHTML = `<div class="submission-empty error">鎻愬鍒楄〃鍔犺浇澶辫触: ${escapeHtml(error.message)}</div>`;
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
  renderListEditor('memFeaturesList', memory.features || [], '渚嬪锛歳eview-flow');
  renderListEditor('memNodeIdField', memory.nodeId ? [memory.nodeId] : [], '渚嬪锛欴na.Server');
  renderListEditor('memTagsList', memory.tags || [], '渚嬪锛?decision');

  clearStructuredFields();
  $('memFieldNotes').value = memory.content || '';
  updateGeneratedContent();

  $('btnDeleteMemory').style.display = 'inline-block';
  setEditorHint('浣犳鍦ㄦ煡鐪嬫寮忕煡璇嗐€傜偣鍑烩€滄彁浜ゅ鏍糕€濅細鐢熸垚涓€鏉′慨鏀规彁妗堬紝涓嶄細鐩存帴瑕嗙洊姝ｅ紡搴撱€?);
  setSubmissionNotice(null);
  hideEmptyState();
}

export function createNew() {
  syncNodeTypeSelectOptions();
  _currentMemoryId = null;
  renderMemoryList();

  $('memoryEditorForm').style.display = 'block';
  $('memId').textContent = '鏂板缓鎻愬';
  $('memSummary').value = '';
  $('memType').value = 'Semantic';
  $('memLayer').value = 'Technical';
  $('memImportance').value = 0.8;

  setCheckedDisciplines([]);
  renderListEditor('memFeaturesList', [], '渚嬪锛歳eview-flow');
  renderListEditor('memNodeIdField', [], '渚嬪锛欴na.Server');
  renderListEditor('memTagsList', [], '渚嬪锛?lesson');

  clearStructuredFields();
  fillTemplate('Technical');

  $('btnDeleteMemory').style.display = 'none';
  setEditorHint('鏂板缓鍐呭浼氳繘鍏ラ瀹￠槦鍒椼€傛寮忓簱鍦ㄥ鏍稿彂甯冨墠涓嶄細鍑虹幇杩欐潯鐭ヨ瘑銆?);
  setSubmissionNotice(null);
  hideEmptyState();
}

export function addFeature() {
  addListItem('memFeaturesList', '', '渚嬪锛歳eview-flow');
}

export function addNodeId() {
  addListItem('memNodeIdField', '', '渚嬪锛欴na.Server');
}

export function addTag() {
  addListItem('memTagsList', '', '渚嬪锛?lesson');
}

export async function saveMemory() {
  updateGeneratedContent();

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
    _showToast('姝ｆ枃鍐呭涓嶈兘涓虹┖銆?, true);
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
        ? '淇敼鎻愬宸叉彁浜わ紝姝ｅ紡鐭ヨ瘑涓嶄細绔嬪嵆鍙樻洿銆?
        : '鏂板缓鎻愬宸叉彁浜わ紝鍙湪鈥滄垜鐨勬彁瀹♀€濅腑璺熻釜鐘舵€併€?
    );
  } catch (error) {
    _showToast(`鎻愪氦瀹℃牳澶辫触: ${error.message}`, true);
  }
}

export async function deleteMemory() {
  if (!_currentMemoryId) return;

  const confirmed = await _showConfirmModal(
    '鎻愪氦鍒犻櫎鐢宠',
    '纭畾瑕佹妸杩欐潯姝ｅ紡鐭ヨ瘑鎻愪氦涓衡€滃垹闄ょ敵璇封€濆悧锛熷湪绠＄悊鍛樺鏍稿苟鍙戝竷涔嬪墠锛屾寮忕煡璇嗕笉浼氱珛鍗冲垹闄ゃ€?
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
    _showToast('鍒犻櫎鐢宠宸叉彁浜ゅ鏍搞€?);
  } catch (error) {
    _showToast(`鎻愪氦鍒犻櫎鐢宠澶辫触: ${error.message}`, true);
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
          <button class="btn-cancel" style="padding:6px 16px;border:1px solid var(--border-color,#555);border-radius:4px;background:transparent;color:var(--text-secondary,#aaa);cursor:pointer">鍙栨秷</button>
          <button class="btn-confirm" style="padding:6px 16px;border:none;border-radius:4px;background:#e74c3c;color:#fff;cursor:pointer">缁х画鎻愪氦</button>
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

