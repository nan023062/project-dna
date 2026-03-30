import { $, api, escapeHtml, getAuthToken, setAuthToken, clearAuthToken } from '../utils.js';

let currentUser = null;
let authMessage = '';
let submissions = [];
let selectedSubmissionId = null;
let selectedSubmission = null;

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

function normalizeStatus(status) {
  if (typeof status === 'number') {
    return ['draft', 'pending', 'approved', 'rejected', 'published', 'withdrawn', 'superseded'][status] || 'pending';
  }

  return String(status || 'pending').toLowerCase();
}

function statusMeta(status) {
  return STATUS_META[normalizeStatus(status)] || STATUS_META.pending;
}

function normalizeOperation(operation) {
  if (typeof operation === 'number') {
    return ['create', 'update', 'delete'][operation] || 'create';
  }

  return String(operation || 'create').toLowerCase();
}

function operationLabel(operation) {
  return OPERATION_LABEL[normalizeOperation(operation)] || '变更';
}

function isAdminUser(user) {
  return String(user?.role || '').toLowerCase() === 'admin';
}

function formatTime(value) {
  if (!value) return '未知时间';

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return String(value);

  return date.toLocaleString('zh-CN', { hour12: false });
}

function formatJson(value) {
  if (value == null) return '无';

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

function renderAuthSection() {
  const host = $('reviewAuthSection');
  if (!host) return;

  if (isAdminUser(currentUser)) {
    host.innerHTML = `
      <div class="review-auth-card is-authenticated">
        <div class="review-auth-main">
          <div class="review-auth-title">管理员已登录</div>
          <div class="review-auth-copy">
            当前账号：${escapeHtml(currentUser.username || currentUser.id || 'admin')}。
            这里可以查看待审核提交，并执行审核通过、驳回和发布。
          </div>
        </div>
        <div class="review-auth-actions">
          <button class="btn btn-secondary btn-sm" onclick="window.ReviewAdmin.logout()">退出登录</button>
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
          审核队列仅对管理员开放。登录后可查看提交、审阅快照，并直接执行审核和发布。
        </div>
      </div>
      <div class="review-auth-form">
        <input id="reviewLoginUsername" type="text" placeholder="用户名" value="admin" />
        <input id="reviewLoginPassword" type="password" placeholder="密码" value="admin" />
        <button class="btn btn-primary btn-sm" onclick="window.ReviewAdmin.login()">登录</button>
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
    const title = item.title || item.proposedPayload?.summary || `${operationLabel(item.operation)}提交`;
    const isActive = selectedSubmissionId === item.id ? 'active' : '';

    return `
      <div class="review-list-item ${isActive}" data-id="${item.id}">
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

  host.querySelectorAll('.review-list-item').forEach(element => {
    element.addEventListener('click', () => {
      window.ReviewAdmin.selectSubmission(element.dataset.id);
    });
  });
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
      <button class="btn btn-secondary btn-sm" onclick="window.ReviewAdmin.reloadSelection()">刷新详情</button>
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
          <button class="btn btn-primary btn-sm" ${canApprove ? '' : 'disabled'} onclick="window.ReviewAdmin.approve()">审核通过</button>
          <button class="btn btn-secondary btn-sm" ${canReject ? '' : 'disabled'} onclick="window.ReviewAdmin.reject()">驳回</button>
          <button class="btn btn-primary btn-sm" ${canPublish ? '' : 'disabled'} onclick="window.ReviewAdmin.publish()">发布</button>
        </div>
        <div class="review-action-hint">
          普通提交需要先通过审核，再进入正式知识库发布。管理员直写正式库是另一条审计通道，不经过这里。
        </div>
      </div>
    </div>

    <div class="review-payload-grid">
      <div class="review-detail-card">
        <div class="review-detail-card-title">正式快照</div>
        <pre class="review-json-block">${escapeHtml(formatJson(item.targetSnapshot))}</pre>
      </div>
      <div class="review-detail-card">
        <div class="review-detail-card-title">候选内容</div>
        <pre class="review-json-block">${escapeHtml(formatJson(item.proposedPayload || item.normalizedPayload))}</pre>
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
