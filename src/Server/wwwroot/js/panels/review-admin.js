import { $, api, escapeHtml, getAuthToken, setAuthToken, clearAuthToken } from '../utils.js';
import { bindDelegatedDocumentEvents } from '/dna-shared/js/core/dom-actions.js';
import { formatUserIdentity, isAdminUser } from '/dna-shared/js/auth/user-session.js';

let currentUser = null;
let authMessage = '';
let submissions = [];
let selectedSubmissionId = null;
let selectedSubmission = null;
let reviewUiEventsBound = false;

const STATUS_META = {
  draft: { label: '草稿', cls: 'draft' },
  pending: { label: '待审核', cls: 'pending' },
  approved: { label: '已通过', cls: 'approved' },
  rejected: { label: '已驳回', cls: 'rejected' },
  published: { label: '已发布', cls: 'published' },
  withdrawn: { label: '已撤回', cls: 'withdrawn' },
  superseded: { label: '已过期', cls: 'superseded' }
};

const OPERATION_LABEL = {
  create: '新建',
  update: '修改',
  delete: '删除'
};

const ACTION_LABEL = {
  approve: '审核通过',
  reject: '驳回',
  publish: '发布'
};

const MEMORY_TYPE_ORDER = ['structural', 'semantic', 'episodic', 'working', 'procedural'];
const MEMORY_TYPE_LABEL = {
  structural: '结构化',
  semantic: '语义',
  episodic: '事件',
  working: '工作',
  procedural: '流程'
};

const MEMORY_SOURCE_ORDER = ['system', 'ai', 'human', 'external', 'confluence', 'jira'];
const MEMORY_SOURCE_LABEL = {
  system: '系统',
  ai: 'AI',
  human: '人工',
  external: '外部',
  confluence: 'Confluence',
  jira: 'Jira'
};

const NODE_TYPE_ORDER = ['project', 'department', 'technical', 'team'];
const NODE_TYPE_LABEL = {
  project: '项目',
  department: '部门',
  technical: '技术',
  team: '团队'
};

const DIFF_FIELDS = [
  { key: 'summary', label: '摘要' },
  { key: 'type', label: '记忆类型' },
  { key: 'nodeType', label: '节点类型' },
  { key: 'source', label: '来源' },
  { key: 'nodeId', label: '模块 ID' },
  { key: 'disciplines', label: 'Discipline' },
  { key: 'features', label: 'Feature' },
  { key: 'tags', label: '标签' },
  { key: 'pathPatterns', label: '路径模式' },
  { key: 'parentId', label: '父记忆' },
  { key: 'importance', label: '重要度' },
  { key: 'externalSourceUrl', label: '外部来源 URL' },
  { key: 'externalSourceId', label: '外部来源 ID' }
];

function normalizeStatus(status) {
  if (typeof status === 'number') {
    return ['draft', 'pending', 'approved', 'rejected', 'published', 'withdrawn', 'superseded'][status] || 'pending';
  }

  return String(status || 'pending').trim().toLowerCase();
}

function statusMeta(status) {
  return STATUS_META[normalizeStatus(status)] || STATUS_META.pending;
}

function normalizeOperation(operation) {
  if (typeof operation === 'number') {
    return ['create', 'update', 'delete'][operation] || 'create';
  }

  return String(operation || 'create').trim().toLowerCase();
}

function operationLabel(operation) {
  return OPERATION_LABEL[normalizeOperation(operation)] || '变更';
}

function formatTime(value) {
  if (!value) return '未知时间';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);

  return date.toLocaleString('zh-CN', { hour12: false });
}

function formatJson(value) {
  if (value == null) return 'null';

  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

function listResult(result) {
  if (Array.isArray(result)) return result;
  if (Array.isArray(result?.value)) return result.value;
  return [];
}

function buildReviewPath() {
  const query = new URLSearchParams();
  const status = $('reviewFilterStatus')?.value || '';
  const submitter = $('reviewFilterSubmitter')?.value?.trim() || '';

  if (status) query.set('status', status);
  if (submitter) query.set('submitter', submitter);

  const suffix = query.toString();
  return `/admin/review/submissions${suffix ? `?${suffix}` : ''}`;
}

function setActionComment(value = '') {
  const input = $('reviewActionComment');
  if (input) input.value = value;
}

function getSelectedActionComment() {
  return $('reviewActionComment')?.value?.trim() || null;
}

function setMessage(message = '') {
  authMessage = message;
  const host = $('reviewAuthMessage');
  if (host) host.textContent = message;
}

function normalizeText(value) {
  if (value == null) return null;
  const text = String(value).trim();
  return text ? text : null;
}

function normalizeNumber(value) {
  if (value == null || value === '') return null;
  const num = Number(value);
  return Number.isFinite(num) ? num : null;
}

function normalizeList(value) {
  if (!Array.isArray(value)) return [];

  const seen = new Map();
  for (const item of value) {
    const text = normalizeText(item);
    if (!text) continue;
    const key = text.toLowerCase();
    if (!seen.has(key)) seen.set(key, text);
  }

  return [...seen.values()].sort((a, b) => a.localeCompare(b, 'zh-CN'));
}

function normalizeEnumValue(value, orderedKeys) {
  if (value == null || value === '') return null;

  if (typeof value === 'number') {
    return orderedKeys[value] || String(value);
  }

  return String(value).trim().toLowerCase();
}

function formatMemoryType(value) {
  const key = normalizeEnumValue(value, MEMORY_TYPE_ORDER);
  return key ? (MEMORY_TYPE_LABEL[key] || key) : null;
}

function formatMemorySource(value) {
  const key = normalizeEnumValue(value, MEMORY_SOURCE_ORDER);
  return key ? (MEMORY_SOURCE_LABEL[key] || key) : null;
}

function formatNodeType(value) {
  const key = normalizeEnumValue(value, NODE_TYPE_ORDER);
  return key ? (NODE_TYPE_LABEL[key] || key) : null;
}

function toComparablePayload(raw) {
  if (!raw || typeof raw !== 'object') return null;

  const payload = {
    summary: normalizeText(raw.summary),
    type: formatMemoryType(raw.type),
    nodeType: formatNodeType(raw.nodeType),
    source: formatMemorySource(raw.source),
    nodeId: normalizeText(raw.nodeId),
    disciplines: normalizeList(raw.disciplines),
    features: normalizeList(raw.features),
    tags: normalizeList(raw.tags),
    pathPatterns: normalizeList(raw.pathPatterns),
    parentId: normalizeText(raw.parentId),
    importance: normalizeNumber(raw.importance),
    externalSourceUrl: normalizeText(raw.externalSourceUrl),
    externalSourceId: normalizeText(raw.externalSourceId),
    content: normalizeText(raw.content)
  };

  return compactObject(payload);
}

function compactObject(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return value;

  const result = {};
  for (const [key, current] of Object.entries(value)) {
    if (current == null) continue;
    if (Array.isArray(current) && current.length === 0) continue;
    if (typeof current === 'string' && current.length === 0) continue;
    result[key] = current;
  }
  return result;
}

function getCandidatePayload(item) {
  return item?.normalizedPayload || item?.proposedPayload || null;
}

function hasValue(value) {
  if (value == null) return false;
  if (Array.isArray(value)) return value.length > 0;
  if (typeof value === 'string') return value.length > 0;
  return true;
}

function toStableValue(value) {
  if (Array.isArray(value)) return value.map(toStableValue);
  if (value && typeof value === 'object') {
    return Object.keys(value)
      .sort((a, b) => a.localeCompare(b, 'en'))
      .reduce((acc, key) => {
        acc[key] = toStableValue(value[key]);
        return acc;
      }, {});
  }
  return value ?? null;
}

function areValuesEqual(left, right) {
  return JSON.stringify(toStableValue(left)) === JSON.stringify(toStableValue(right));
}

function formatImportance(value) {
  if (!Number.isFinite(value)) return null;
  return value.toFixed(2).replace(/\.00$/, '');
}

function renderFieldValue(value, key) {
  if (!hasValue(value)) return '<span class="review-diff-empty">空</span>';

  if (Array.isArray(value)) {
    return `
      <div class="review-diff-pill-list">
        ${value.map(item => `<span class="review-diff-pill">${escapeHtml(String(item))}</span>`).join('')}
      </div>
    `;
  }

  if (typeof value === 'number') {
    return `<span>${escapeHtml(formatImportance(value) || String(value))}</span>`;
  }

  const isLongText = key === 'summary' && String(value).length > 80;
  const cls = isLongText ? 'review-diff-value is-multiline' : 'review-diff-value';
  return `<span class="${cls}">${escapeHtml(String(value))}</span>`;
}

function buildFieldDiffs(operation, targetPayload, candidatePayload) {
  const diffs = [];

  for (const field of DIFF_FIELDS) {
    const before = targetPayload?.[field.key] ?? null;
    const after = candidatePayload?.[field.key] ?? null;
    const same = areValuesEqual(before, after);

    if (operation === 'update' && same) continue;
    if (operation === 'create' && !hasValue(after)) continue;
    if (operation === 'delete' && !hasValue(before)) continue;

    let kind = 'change';
    if (!hasValue(before) && hasValue(after)) kind = 'add';
    else if (hasValue(before) && !hasValue(after)) kind = 'del';
    else if (same) kind = 'same';

    diffs.push({
      ...field,
      before,
      after,
      kind
    });
  }

  return diffs;
}

function renderFieldDiffSection(item, targetPayload, candidatePayload) {
  const operation = normalizeOperation(item.operation);
  const diffs = buildFieldDiffs(operation, targetPayload, candidatePayload);

  const addCount = diffs.filter(diff => diff.kind === 'add').length;
  const delCount = diffs.filter(diff => diff.kind === 'del').length;
  const changeCount = diffs.filter(diff => diff.kind === 'change').length;

  return `
    <div class="review-detail-card">
      <div class="review-detail-card-title">结构化 Diff</div>
      <div class="review-diff-stats">
        <span class="review-diff-stat">操作：${escapeHtml(operationLabel(operation))}</span>
        <span class="review-diff-stat">新增 ${addCount}</span>
        <span class="review-diff-stat">删除 ${delCount}</span>
        <span class="review-diff-stat">修改 ${changeCount}</span>
      </div>
      ${diffs.length === 0 ? `
        <div class="review-history-empty">当前提交没有结构化字段变化。</div>
      ` : `
        <div class="review-diff-table">
          <div class="review-diff-head">
            <div>字段</div>
            <div>正式知识</div>
            <div>候选内容</div>
          </div>
          ${diffs.map(diff => `
            <div class="review-diff-row is-${diff.kind}">
              <div class="review-diff-field">${escapeHtml(diff.label)}</div>
              <div class="review-diff-cell before">
                <div class="review-diff-side-label">正式知识</div>
                ${renderFieldValue(diff.before, diff.key)}
              </div>
              <div class="review-diff-cell after">
                <div class="review-diff-side-label">候选内容</div>
                ${renderFieldValue(diff.after, diff.key)}
              </div>
            </div>
          `).join('')}
        </div>
      `}
    </div>
  `;
}

function renderContentDiffSection(targetPayload, candidatePayload) {
  const before = targetPayload?.content || '';
  const after = candidatePayload?.content || '';

  return `
    <div class="review-detail-card">
      <div class="review-detail-card-title">正文 Diff</div>
      ${before || after
        ? renderDiff(before, after)
        : '<div class="review-history-empty">当前提交不包含正文内容。</div>'}
    </div>
  `;
}

function renderJsonDiffSection(targetPayload, candidatePayload) {
  const before = targetPayload ? JSON.stringify(targetPayload, null, 2) : '';
  const after = candidatePayload ? JSON.stringify(candidatePayload, null, 2) : '';

  return `
    <div class="review-detail-card">
      <div class="review-detail-card-title">结构化 JSON Diff</div>
      ${before || after
        ? renderDiff(before, after)
        : '<div class="review-history-empty">当前提交没有可比较的结构化数据。</div>'}
    </div>
  `;
}

function renderSubmissionDiff(item) {
  const targetPayload = toComparablePayload(item?.targetSnapshot);
  const candidatePayload = toComparablePayload(getCandidatePayload(item));

  return `
    <div class="review-diff-grid">
      ${renderFieldDiffSection(item, targetPayload, candidatePayload)}
      ${renderContentDiffSection(targetPayload, candidatePayload)}
    </div>
    <div class="review-diff-grid single-column">
      ${renderJsonDiffSection(targetPayload, candidatePayload)}
    </div>
  `;
}

async function handleReviewAction(action, element) {
  switch (action) {
    case 'login':
      await login();
      break;
    case 'logout':
      logout();
      break;
    case 'select-submission': {
      const submissionId = element?.dataset.submissionId
        ? decodeURIComponent(element.dataset.submissionId)
        : '';
      if (submissionId) {
        await selectSubmission(submissionId);
      }
      break;
    }
    case 'reload-selection':
      await reloadSelection();
      break;
    case 'approve':
      await approve();
      break;
    case 'reject':
      await reject();
      break;
    case 'publish':
      await publish();
      break;
    default:
      break;
  }
}

function bindReviewUiEvents() {
  if (reviewUiEventsBound || typeof document === 'undefined') {
    return;
  }

  reviewUiEventsBound = true;

  bindDelegatedDocumentEvents([
    {
      eventName: 'click',
      selector: '[data-review-action]',
      preventDefault: true,
      shouldHandle: ({ element }) => Boolean(element.closest('#reviewAuthSection, #reviewSubmissionList, #reviewDetailContent')),
      handler: ({ element }) => void handleReviewAction(element.dataset.reviewAction, element)
    }
  ]);
}

function renderAuthSection() {
  const host = $('reviewAuthSection');
  if (!host) return;

  if (isAdminUser(currentUser)) {
    host.innerHTML = `
      <div class="review-auth-card is-authenticated">
        <div class="review-auth-main">
          <div class="review-auth-title">管理员已登录</div>
          <div class="review-auth-copy">
            当前账号：${escapeHtml(formatUserIdentity(currentUser, 'admin'))}。
            这里可以查看待审核提交，并执行审核通过、驳回和发布。
          </div>
        </div>
        <div class="review-auth-actions">
          <button class="btn btn-secondary btn-sm" data-review-action="logout">退出登录</button>
        </div>
      </div>
      <div id="reviewAuthMessage" class="review-auth-message">${escapeHtml(authMessage)}</div>
    `;
    return;
  }

  host.innerHTML = `
    <div class="review-auth-card">
      <div class="review-auth-main">
        <div class="review-auth-title">登录管理员账号</div>
        <div class="review-auth-copy">
          审核队列仅对管理员开放。登录后可查看提交、对比差异，并直接执行审核和发布。
        </div>
      </div>
      <div class="review-auth-form">
        <input id="reviewLoginUsername" type="text" placeholder="用户名" value="admin" />
        <input id="reviewLoginPassword" type="password" placeholder="密码" value="admin" />
        <button class="btn btn-primary btn-sm" data-review-action="login">登录</button>
      </div>
    </div>
    <div id="reviewAuthMessage" class="review-auth-message">${escapeHtml(authMessage)}</div>
  `;
}

function renderStats() {
  const host = $('reviewStats');
  if (!host) return;

  const counts = {
    total: submissions.length,
    pending: 0,
    approved: 0,
    published: 0
  };

  for (const item of submissions) {
    const key = normalizeStatus(item.status);
    if (key in counts) counts[key] += 1;
  }

  host.innerHTML = `
    <div class="review-stat-card">
      <div class="review-stat-value">${counts.total}</div>
      <div class="review-stat-label">总提交</div>
    </div>
    <div class="review-stat-card">
      <div class="review-stat-value">${counts.pending}</div>
      <div class="review-stat-label">待审核</div>
    </div>
    <div class="review-stat-card">
      <div class="review-stat-value">${counts.approved}</div>
      <div class="review-stat-label">待发布</div>
    </div>
    <div class="review-stat-card">
      <div class="review-stat-value">${counts.published}</div>
      <div class="review-stat-label">已发布</div>
    </div>
  `;
}

function renderList(message = '') {
  const host = $('reviewSubmissionList');
  if (!host) return;

  if (!isAdminUser(currentUser)) {
    host.innerHTML = '<div class="empty">请先以管理员身份登录，再查看审核队列。</div>';
    return;
  }

  if (message) {
    host.innerHTML = `<div class="empty error-msg">${escapeHtml(message)}</div>`;
    return;
  }

  if (submissions.length === 0) {
    host.innerHTML = '<div class="empty">当前没有符合筛选条件的审核提交。</div>';
    return;
  }

  host.innerHTML = submissions.map(item => {
    const status = statusMeta(item.status);
    const title = item.title || getCandidatePayload(item)?.summary || `${operationLabel(item.operation)}提交`;
    const isActive = selectedSubmissionId === item.id ? 'active' : '';

    return `
      <div
        class="review-list-item ${isActive}"
        data-review-action="select-submission"
        data-submission-id="${encodeURIComponent(item.id)}"
      >
        <div class="review-list-item-top">
          <span class="review-status-badge ${status.cls}">${status.label}</span>
          <span class="review-operation">${escapeHtml(operationLabel(item.operation))}</span>
        </div>
        <div class="review-list-title">${escapeHtml(title)}</div>
        <div class="review-list-meta">提交人：${escapeHtml(item.submitter?.username || '-')}</div>
        <div class="review-list-meta">更新时间：${escapeHtml(formatTime(item.updatedAt || item.createdAt))}</div>
      </div>
    `;
  }).join('');

}

function renderDetail(message = '') {
  const host = $('reviewDetailContent');
  if (!host) return;

  if (!isAdminUser(currentUser)) {
    host.innerHTML = '<div class="empty">登录后可查看提交详情、审核动作和发布入口。</div>';
    return;
  }

  if (message) {
    host.innerHTML = `<div class="empty error-msg">${escapeHtml(message)}</div>`;
    return;
  }

  if (!selectedSubmission) {
    host.innerHTML = '<div class="empty">从左侧选择一条提交，查看详情与审核操作。</div>';
    return;
  }

  const item = selectedSubmission;
  const status = statusMeta(item.status);
  const canApprove = normalizeStatus(item.status) === 'pending';
  const canReject = !['published', 'withdrawn'].includes(normalizeStatus(item.status));
  const canPublish = normalizeStatus(item.status) === 'approved';
  const actions = Array.isArray(item.actions) ? item.actions : [];

  host.innerHTML = `
    <div class="review-detail-header">
      <div>
        <div class="review-detail-title">${escapeHtml(item.title || `${operationLabel(item.operation)}提交`)}</div>
        <div class="review-detail-subtitle">
          <span class="review-status-badge ${status.cls}">${status.label}</span>
          <span>提交编号：${escapeHtml(item.id)}</span>
        </div>
      </div>
      <button class="btn btn-secondary btn-sm" data-review-action="reload-selection">刷新详情</button>
    </div>

    <div class="review-detail-grid">
      <div class="review-detail-card">
        <div class="review-detail-card-title">基础信息</div>
        <div class="review-detail-line"><span>操作</span><strong>${escapeHtml(operationLabel(item.operation))}</strong></div>
        <div class="review-detail-line"><span>提交人</span><strong>${escapeHtml(item.submitter?.username || '-')}</strong></div>
        <div class="review-detail-line"><span>角色</span><strong>${escapeHtml(item.submitter?.role || '-')}</strong></div>
        <div class="review-detail-line"><span>目标 ID</span><strong>${escapeHtml(item.targetId || item.publishedTargetId || '-')}</strong></div>
        <div class="review-detail-line"><span>提交原因</span><strong>${escapeHtml(item.reason || '-')}</strong></div>
        <div class="review-detail-line"><span>创建时间</span><strong>${escapeHtml(formatTime(item.createdAt))}</strong></div>
        <div class="review-detail-line"><span>更新时间</span><strong>${escapeHtml(formatTime(item.updatedAt))}</strong></div>
        <div class="review-detail-line"><span>审核意见</span><strong>${escapeHtml(item.reviewNote || '-')}</strong></div>
      </div>

      <div class="review-detail-card">
        <div class="review-detail-card-title">审核操作</div>
        <textarea
          id="reviewActionComment"
          class="review-action-comment"
          placeholder="可选：填写审核意见、驳回原因或发布备注。"
        >${escapeHtml(item.reviewNote || '')}</textarea>
        <div class="review-action-row">
          <button class="btn btn-primary btn-sm" ${canApprove ? '' : 'disabled'} data-review-action="approve">审核通过</button>
          <button class="btn btn-secondary btn-sm" ${canReject ? '' : 'disabled'} data-review-action="reject">驳回</button>
          <button class="btn btn-primary btn-sm" ${canPublish ? '' : 'disabled'} data-review-action="publish">发布</button>
        </div>
        <div class="review-action-hint">
          普通提交需要先通过审核，再进入正式知识库发布。管理员直写正式库是另一条审计通道，不经过这里。
        </div>
      </div>
    </div>

    ${renderSubmissionDiff(item)}

    <div class="review-payload-grid">
      <div class="review-detail-card">
        <div class="review-detail-card-title">正式快照原始 JSON</div>
        <pre class="review-json-block">${escapeHtml(formatJson(item.targetSnapshot))}</pre>
      </div>
      <div class="review-detail-card">
        <div class="review-detail-card-title">候选内容原始 JSON</div>
        <pre class="review-json-block">${escapeHtml(formatJson(getCandidatePayload(item)))}</pre>
      </div>
    </div>

    <div class="review-detail-card">
      <div class="review-detail-card-title">操作历史</div>
      ${actions.length === 0 ? '<div class="review-history-empty">暂无历史动作。</div>' : `
        <div class="review-history-list">
          ${actions.map(action => `
            <div class="review-history-item">
              <div class="review-history-top">
                <strong>${escapeHtml(action.action)}</strong>
                <span>${escapeHtml(formatTime(action.createdAt))}</span>
              </div>
              <div class="review-history-meta">执行人：${escapeHtml(action.actor?.username || '-')} / ${escapeHtml(action.actor?.role || '-')}</div>
              <div class="review-history-meta">状态：${escapeHtml(normalizeStatus(action.beforeStatus || 'draft'))} -> ${escapeHtml(normalizeStatus(action.afterStatus || 'draft'))}</div>
              <div class="review-history-meta">${escapeHtml(action.comment || '无备注')}</div>
            </div>
          `).join('')}
        </div>
      `}
    </div>
  `;
}

export async function ensureAdminSession() {
  if (isAdminUser(currentUser)) return true;

  if (!getAuthToken()) {
    currentUser = null;
    renderAuthSection();
    return false;
  }

  try {
    const me = await api('/auth/me');
    currentUser = me;

    if (!isAdminUser(me)) {
      setMessage('当前登录用户不是管理员，无法查看审核队列。');
      renderAuthSection();
      return false;
    }

    setMessage('');
    renderAuthSection();
    return true;
  } catch (error) {
    clearAuthToken();
    currentUser = null;
    setMessage(error?.status === 401 ? '登录已过期，请重新登录。' : `认证失败：${error.message}`);
    renderAuthSection();
    return false;
  }
}

async function handleDecision(action) {
  if (!selectedSubmissionId) return;

  try {
    const result = await api(`/admin/review/submissions/${encodeURIComponent(selectedSubmissionId)}/${action}`, {
      method: 'POST',
      body: { comment: getSelectedActionComment() }
    });

    const next = result?.submission || result;
    selectedSubmissionId = next?.id || selectedSubmissionId;
    setMessage(`${ACTION_LABEL[action] || action}已完成。`);
    renderAuthSection();

    await loadReviewQueue();
    await selectSubmission(selectedSubmissionId, true);
  } catch (error) {
    setMessage(`操作失败：${error.message}`);
    renderAuthSection();
  }
}

export async function login() {
  const username = $('reviewLoginUsername')?.value?.trim();
  const password = $('reviewLoginPassword')?.value || '';

  if (!username || !password) {
    setMessage('请输入管理员用户名和密码。');
    renderAuthSection();
    return;
  }

  try {
    const result = await api('/auth/login', {
      method: 'POST',
      body: { username, password },
      skipAuth: true
    });

    if (!isAdminUser(result?.user)) {
      clearAuthToken();
      currentUser = result?.user || null;
      setMessage('当前账号不是管理员，不能进入审核队列。');
      renderAuthSection();
      return;
    }

    setAuthToken(result.token);
    currentUser = result.user;
    setMessage('');
    renderAuthSection();
    await loadReviewQueue();
  } catch (error) {
    clearAuthToken();
    currentUser = null;
    setMessage(`登录失败：${error.message}`);
    renderAuthSection();
  }
}

export function logout() {
  clearAuthToken();
  currentUser = null;
  submissions = [];
  selectedSubmissionId = null;
  selectedSubmission = null;
  setMessage('已退出管理员登录。');
  renderAuthSection();
  renderStats();
  renderList();
  renderDetail();
}

export async function loadReviewQueue() {
  renderAuthSection();

  const ready = await ensureAdminSession();
  if (!ready) {
    renderStats();
    renderList();
    renderDetail();
    return false;
  }

  try {
    const result = await api(buildReviewPath());
    submissions = listResult(result);

    if (selectedSubmissionId && !submissions.some(item => item.id === selectedSubmissionId)) {
      selectedSubmissionId = null;
      selectedSubmission = null;
    }

    renderStats();
    renderList();

    if (selectedSubmissionId) {
      await selectSubmission(selectedSubmissionId, true);
    } else if (submissions.length > 0) {
      await selectSubmission(submissions[0].id, true);
    } else {
      renderDetail();
    }

    return true;
  } catch (error) {
    renderStats();
    renderList(error.message);
    renderDetail(error.message);
    return false;
  }
}

export async function selectSubmission(id, forceReload = false) {
  selectedSubmissionId = id;
  renderList();

  const ready = await ensureAdminSession();
  if (!ready) {
    renderDetail();
    return;
  }

  if (!forceReload && selectedSubmission?.id === id) {
    renderDetail();
    return;
  }

  try {
    selectedSubmission = await api(`/admin/review/submissions/${encodeURIComponent(id)}`);
    setActionComment(selectedSubmission.reviewNote || '');
    renderDetail();
  } catch (error) {
    renderDetail(error.message);
  }
}

export async function reloadSelection() {
  if (!selectedSubmissionId) {
    await loadReviewQueue();
    return;
  }

  await selectSubmission(selectedSubmissionId, true);
}

export async function approve() {
  await handleDecision('approve');
}

export async function reject() {
  await handleDecision('reject');
}

export async function publish() {
  await handleDecision('publish');
}

function renderDiff(oldStr, newStr) {
  const oldLines = String(oldStr || '').split('\n');
  const newLines = String(newStr || '').split('\n');
  const ops = buildGreedyDiffOps(oldLines, newLines);
  const hasChanges = ops.some(op => op.type !== 'context');
  if (!hasChanges) {
    return '<div class="diff-view unified"><div class="diff-empty">无可视化差异。</div></div>';
  }

  let oldNo = 1;
  let newNo = 1;
  for (const op of ops) {
    if (op.type === 'add') {
      op.oldNo = null;
      op.newNo = newNo++;
      continue;
    }
    if (op.type === 'del') {
      op.oldNo = oldNo++;
      op.newNo = null;
      continue;
    }
    op.oldNo = oldNo++;
    op.newNo = newNo++;
  }

  const ranges = buildDiffRanges(ops, 3);
  const maxRenderLines = 900;
  let renderedLines = 0;
  let html = '<div class="diff-view unified">';
  let lastEnd = -1;

  for (const range of ranges) {
    if (renderedLines >= maxRenderLines) break;
    if (range.start > lastEnd + 1) {
      html += '<div class="diff-gap">…</div>';
    }

    const header = buildHunkHeader(ops, range.start, range.end);
    html += `<div class="diff-hunk-header">${header}</div>`;

    for (let i = range.start; i <= range.end; i++) {
      if (renderedLines >= maxRenderLines) break;
      html += renderUnifiedDiffLine(ops[i]);
      renderedLines++;
    }
    lastEnd = range.end;
  }

  if (renderedLines >= maxRenderLines) {
    html += '<div class="diff-gap">…（diff 过长，已截断）</div>';
  }

  html += '</div>';
  return html;
}

function buildGreedyDiffOps(oldLines, newLines) {
  const ops = [];
  let i = 0;
  let j = 0;

  while (i < oldLines.length || j < newLines.length) {
    const oldLine = i < oldLines.length ? oldLines[i] : null;
    const newLine = j < newLines.length ? newLines[j] : null;

    if (oldLine !== null && newLine !== null && oldLine === newLine) {
      ops.push({ type: 'context', text: oldLine });
      i++;
      j++;
      continue;
    }

    const nextOldMatches = i + 1 < oldLines.length && newLine !== null && oldLines[i + 1] === newLine;
    const nextNewMatches = j + 1 < newLines.length && oldLine !== null && oldLine === newLines[j + 1];

    if (nextOldMatches && !nextNewMatches) {
      ops.push({ type: 'del', text: oldLine || '' });
      i++;
      continue;
    }

    if (nextNewMatches && !nextOldMatches) {
      ops.push({ type: 'add', text: newLine || '' });
      j++;
      continue;
    }

    if (oldLine !== null) {
      ops.push({ type: 'del', text: oldLine });
      i++;
    }
    if (newLine !== null) {
      ops.push({ type: 'add', text: newLine });
      j++;
    }
  }

  return ops;
}

function buildDiffRanges(ops, contextSize) {
  const changedIndexes = [];
  for (let i = 0; i < ops.length; i++) {
    if (ops[i].type !== 'context') changedIndexes.push(i);
  }

  if (changedIndexes.length === 0) {
    return [{ start: 0, end: Math.max(0, Math.min(ops.length - 1, contextSize * 2)) }];
  }

  const ranges = [];
  for (const idx of changedIndexes) {
    const start = Math.max(0, idx - contextSize);
    const end = Math.min(ops.length - 1, idx + contextSize);
    const last = ranges[ranges.length - 1];
    if (last && start <= last.end + 1) {
      last.end = Math.max(last.end, end);
    } else {
      ranges.push({ start, end });
    }
  }

  return ranges;
}

function buildHunkHeader(ops, start, end) {
  let oldStart = 0;
  let newStart = 0;
  let oldCount = 0;
  let newCount = 0;
  let started = false;

  for (let i = start; i <= end; i++) {
    const op = ops[i];
    if (!started) {
      oldStart = op.oldNo ?? (op.newNo || 0);
      newStart = op.newNo ?? (op.oldNo || 0);
      started = true;
    }
    if (op.type !== 'add') oldCount++;
    if (op.type !== 'del') newCount++;
  }

  const oldInfo = `${Math.max(1, oldStart || 1)},${Math.max(0, oldCount)}`;
  const newInfo = `${Math.max(1, newStart || 1)},${Math.max(0, newCount)}`;
  return `@@ -${oldInfo} +${newInfo} @@`;
}

function renderUnifiedDiffLine(op) {
  const oldNo = op.oldNo == null ? '' : String(op.oldNo);
  const newNo = op.newNo == null ? '' : String(op.newNo);
  const sign = op.type === 'add' ? '+' : (op.type === 'del' ? '-' : ' ');
  const cls = op.type === 'add' ? 'add' : (op.type === 'del' ? 'del' : 'context');

  return `<div class="diff-line diff-${cls}">
    <span class="diff-ln old">${oldNo}</span>
    <span class="diff-ln new">${newNo}</span>
    <span class="diff-sign">${sign}</span>
    <span class="diff-text">${escapeHtml(op.text || '')}</span>
  </div>`;
}

bindReviewUiEvents();
