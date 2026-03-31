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
    summary: 'Project 鍏ㄥ眬鎰挎櫙涓庤竟鐣?,
    background: '椤圭洰鏁翠綋鑳屾櫙',
    goal: '鏄庣‘椤圭洰鐩爣銆佽竟鐣屼笌绾︽潫',
    rules: ['鍟嗕笟鐩爣', '鏍稿績浣撻獙鐩爣', '棰勭畻杈圭晫', '鍚堣搴曠嚎', '鎶€鏈€婚€夊瀷'],
    steps: [],
    notes: ''
  },
  Department: {
    summary: 'Department 瑙勫垯娌荤悊',
    background: '閮ㄩ棬绾ф不鐞嗚鑼?,
    goal: '缁熶竴閮ㄩ棬鏍囧噯骞跺崗璋冭祫婧愬啿绐?,
    rules: ['璐ㄩ噺鏍囧噯', '娴佺▼瑙勮寖', '鍗忎綔绾︽潫', '璧勬簮浼樺厛绾?],
    steps: ['璁捐瑙勮寖', '璇勫閫氳繃', '鎵ц钀藉湴'],
    notes: ''
  },
  Technical: {
    summary: 'Technical 鎶€鏈粍瑙勮寖',
    background: '鎶€鏈粍璐熻矗鐨勪笟鍔″煙',
    goal: '鍥哄寲宸ヤ綔娴併€佹帴鍙ｄ笌璐ㄩ噺鏍囧噯',
    rules: ['鎺ュ彛鍗忚', '鎬ц兘瑙勬牸', '鏂囦欢璐ｄ换鍩?, '渚濊禆绾︽潫锛圖AG锛?],
    steps: ['鏂规璁捐', '瑙勮寖璇勫', '鍑嗗叆瀹℃牳'],
    notes: ''
  },
  Team: {
    summary: 'Team 鎵ц璁板綍',
    background: '鍏蜂綋浠诲姟鎵ц涓婁笅鏂?,
    goal: '浜や粯缁撴灉骞舵矇娣€澶嶇洏鐭ヨ瘑',
    rules: ['鎺堟潈鏂囦欢鍩?, '浜や粯楠屾敹鏍囧噯', '杩囩▼璁板繂娌夋穩'],
    steps: ['棰嗗彇浠诲姟', '鎵ц寮€鍙?, '鎻愬浜や粯', '澶嶇洏娌夋穩'],
    notes: ''
  }
};

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
    .map(i => i.value.trim())
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
  lines.push(`## ${draft.summary || '鏈懡鍚嶈蹇?}`);
  lines.push('');
  lines.push(`- 鑺傜偣绫诲瀷: ${nodeType}`);
  lines.push(`- 绫诲瀷: ${type}`);
  if (background) lines.push(`- 鑳屾櫙: ${background}`);
  if (goal) lines.push(`- 鐩爣: ${goal}`);
  lines.push('');

  if (rules.length > 0) {
    lines.push('### 瑙勫垯瑕佺偣');
    lines.push(...rules.map(r => `- ${r}`));
    lines.push('');
  }

  if (steps.length > 0) {
    lines.push('### 鎵ц姝ラ');
    lines.push(...steps.map((s, idx) => `${idx + 1}. ${s}`));
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
  const tpl = TEMPLATE_BY_NODE_TYPE[layerName] ?? TEMPLATE_BY_NODE_TYPE.Technical;
  fillStructuredTemplate($, tpl);
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
  } catch (err) {
    $('memoryList').innerHTML = `<div class="empty error">鍔犺浇澶辫触: ${err.message}</div>`;
  }
}

function renderMemoryList() {
  const container = $('memoryList');
  if (_memories.length === 0) {
    container.innerHTML = '<div class="empty">娌℃湁鎵惧埌璁板繂</div>';
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
  renderListEditor('memFeaturesList', memory.features || [], '渚嬪锛歝haracter');
  renderListEditor('memNodeIdField', memory.nodeId ? [memory.nodeId] : [], '渚嬪锛歯ode-id');
  renderListEditor('memTagsList', memory.tags || [], '渚嬪锛?lesson');

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
  $('memId').textContent = '鏂板缓璁板繂';
  $('memSummary').value = '';
  $('memType').value = 'Semantic';
  $('memLayer').value = 'Technical';
  $('memImportance').value = 0.8;

  setCheckedDisciplines([]);
  renderListEditor('memFeaturesList', [], '渚嬪锛歝haracter');
  renderListEditor('memNodeIdField', [], '渚嬪锛歯ode-id');
  renderListEditor('memTagsList', [], '渚嬪锛?lesson');

  clearStructuredFields();
  fillTemplate('Technical');

  $('btnDeleteMemory').style.display = 'none';
}

export function addFeature() {
  addListItem('memFeaturesList', '', '渚嬪锛歝haracter');
}

export function addNodeId() {
  addListItem('memNodeIdField', '', '渚嬪锛歯ode-id');
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
    _showToast('姝ｆ枃鍐呭涓嶈兘涓虹┖', true);
    return;
  }

  try {
    if (_currentMemoryId) {
      await api(`/memory/${_currentMemoryId}`, {
        method: 'PUT',
        body: request
      });
      _showToast('鏇存柊鎴愬姛');
    } else {
      await api('/memory/remember', {
        method: 'POST',
        body: request
      });
      _showToast('鍒涘缓鎴愬姛');
    }
    await loadMemories();
  } catch (err) {
    _showToast('淇濆瓨澶辫触: ' + err.message, true);
  }
}

export async function deleteMemory() {
  if (!_currentMemoryId) return;

  const confirmed = await _showConfirmModal(
    '鍒犻櫎璁板繂',
    '纭畾瑕佸垹闄よ繖鏉¤蹇嗗悧锛熸鎿嶄綔涓嶅彲鎭㈠銆?
  );
  if (!confirmed) return;

  try {
    await api(`/memory/${_currentMemoryId}`, { method: 'DELETE' });
    _showToast('鍒犻櫎鎴愬姛');
    $('memoryEditorForm').style.display = 'none';
    await loadMemories();
  } catch (err) {
    _showToast('鍒犻櫎澶辫触: ' + err.message, true);
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
          <button class="btn-cancel" style="padding:6px 16px;border:1px solid var(--border-color,#555);border-radius:4px;background:transparent;color:var(--text-secondary,#aaa);cursor:pointer">鍙栨秷</button>
          <button class="btn-confirm" style="padding:6px 16px;border:none;border-radius:4px;background:#e74c3c;color:#fff;cursor:pointer">鍒犻櫎</button>
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
syncNodeTypeSelectOptions();

