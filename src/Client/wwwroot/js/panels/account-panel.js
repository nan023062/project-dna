import { $, escapeHtml, getAuthScope, getAuthToken } from '../utils.js';
import { formatUserIdentity, isAdminUser } from '/dna-shared/js/auth/user-session.js';

let callbacks = {
  getCurrentUser: () => null,
  getCurrentWorkspace: () => null,
  loadUsers: async () => false
};

export function initAccountPanel(options) {
  callbacks = { ...callbacks, ...(options || {}) };
}

function renderScopeCard(user, workspace) {
  const loggedIn = Boolean(getAuthToken() && user);
  const sessionState = loggedIn ? '已登录' : '未登录';
  const sessionCopy = loggedIn
    ? `当前身份：${formatUserIdentity(user, '未登录')}`
    : '请先使用顶部登录框建立当前工作区的服务端会话。';

  return `
    <div class="account-summary-card">
      <div class="account-summary-label">服务端会话</div>
      <div class="account-summary-value">${sessionState}</div>
      <div class="account-summary-copy">${escapeHtml(sessionCopy)}</div>
      <div class="account-summary-meta">作用域：${escapeHtml(getAuthScope())}</div>
    </div>
    <div class="account-summary-card">
      <div class="account-summary-label">当前工作区</div>
      <div class="account-summary-value account-summary-value-sm">${escapeHtml(workspace?.name || '-')}</div>
      <div class="account-summary-copy">${escapeHtml(workspace?.serverBaseUrl || '-')}</div>
      <div class="account-summary-meta">${escapeHtml(workspace?.workspaceRoot || '-')}</div>
    </div>
    <div class="account-summary-card">
      <div class="account-summary-label">权限边界</div>
      <div class="account-summary-value account-summary-value-sm">${isAdminUser(user) ? '管理员' : '预审模式'}</div>
      <div class="account-summary-copy">
        ${isAdminUser(user)
          ? '当前账号可管理用户，并在服务端直接维护正式知识。'
          : '普通用户在 Client 中只能读取正式知识，修改走预审提交流程。'}
      </div>
      <div class="account-summary-meta">如需切换服务器或工作区，请前往“连接”页。</div>
    </div>
  `;
}

function renderPanel() {
  const root = $('clientAccountRoot');
  if (!root) return;

  const user = callbacks.getCurrentUser?.() || null;
  const workspace = callbacks.getCurrentWorkspace?.() || null;

  root.innerHTML = `
    <div class="account-panel-shell">
      <div class="section-heading">
        <div>
          <h2>账号与权限</h2>
          <p>在当前工作区中查看服务端登录态、鉴权作用域，以及管理员用户管理入口。</p>
        </div>
        <div class="quick-actions">
          <button class="btn btn-secondary btn-sm" data-action="switch-tab" data-tab-target="connections">打开连接</button>
          <button class="btn btn-secondary btn-sm" data-action="load-users">刷新权限</button>
        </div>
      </div>
      <div class="account-summary-grid">
        ${renderScopeCard(user, workspace)}
      </div>
      <div id="userAdminRoot" class="user-admin-page"></div>
    </div>
  `;
}

export async function refreshAccountPanel() {
  renderPanel();
  await callbacks.loadUsers?.();
}
