import { escapeHtml } from '../core/web-utils.js';
import { formatUserIdentity, isAdminUser } from '../auth/user-session.js';

const defaultStrings = {
  lockedTitle: '用户管理',
  lockedCopy: '请先以管理员身份登录，再管理用户账号与角色。',
  lockedMessage: '当前尚未建立管理员会话。',
  toolbarTitle: '用户权限管理',
  toolbarSubtitle: '统一管理管理员、编辑者和查看者账号，保持服务端权限边界收口。',
  refreshButton: '刷新用户',
  listTitle: '用户列表',
  emptyList: '还没有可管理的用户。',
  createTitle: '创建用户',
  createCopy: '管理员可直接创建 viewer / editor / admin 账号。',
  createUsernameLabel: '用户名',
  createUsernamePlaceholder: '例如：alice',
  createRoleLabel: '角色',
  createPasswordLabel: '初始密码',
  createPasswordPlaceholder: '至少 4 个字符',
  createButton: '创建用户',
  detailTitle: '用户详情',
  detailEmpty: '从左侧选择一个用户后，可修改角色、重置密码或删除账号。',
  detailUsername: '用户名',
  detailId: '用户 ID',
  detailCreatedAt: '创建时间',
  detailIdentity: '当前身份',
  editRoleLabel: '修改角色',
  resetPasswordLabel: '重置密码',
  resetPasswordPlaceholder: '输入新密码',
  saveRoleButton: '保存角色',
  resetPasswordButton: '重置密码',
  deleteButton: '删除用户',
  selfDeleteHint: '当前登录管理员不能删除自己，也不建议在当前会话里降级自己。',
  deleteHint: '删除用户后不可恢复；若目标是唯一管理员，后端会拒绝删除。',
  totalLabel: '账号总数',
  adminLabel: '管理员',
  editorLabel: '编辑者',
  viewerLabel: '查看者',
  loginRequiredMessage: '请先登录管理员账号。',
  loadFailedMessage: '加载用户失败。',
  selectRequiredMessage: '请先选择一个用户。',
  createInputRequiredMessage: '请输入用户名和初始密码。',
  createFailedMessage: '创建用户失败',
  updateRoleFailedMessage: '修改角色失败',
  resetPasswordRequiredMessage: '请输入新的密码。',
  resetPasswordFailedMessage: '重置密码失败',
  deleteFailedMessage: '删除用户失败',
  deleteConfirm: user => `确定删除用户“${user.username}”吗？`,
  createdMessage: user => `用户已创建：${user.username}`,
  updatedRoleMessage: user => `角色已更新为 ${user.role}`,
  resetPasswordMessage: user => `密码已重置：${user.username}`,
  deletedMessage: user => `用户已删除：${user.username}`
};

function normalizeStrings(strings) {
  return { ...defaultStrings, ...(strings || {}) };
}

function normalizeUserPayload(result) {
  return result?.user || result || null;
}

function listUsers(result) {
  if (Array.isArray(result)) return result;
  if (Array.isArray(result?.users)) return result.users;
  return [];
}

export function createUserAdminController(options) {
  const strings = normalizeStrings(options?.strings);
  const state = {
    currentUser: null,
    users: [],
    selectedUserId: null,
    message: ''
  };

  function getRoot() {
    if (typeof options?.getRoot === 'function') {
      return options.getRoot();
    }

    if (options?.rootId) {
      return document.getElementById(options.rootId);
    }

    return document.getElementById('userAdminRoot');
  }

  function getSelectedUser() {
    return state.users.find(user => user.id === state.selectedUserId) || null;
  }

  function setMessage(nextMessage = '') {
    state.message = nextMessage;
  }

  async function ensureAdminSession() {
    if (!options?.getAuthToken?.()) {
      state.currentUser = options?.getCurrentUser?.() || null;
      return false;
    }

    try {
      const result = await options.loadCurrentUser();
      state.currentUser = normalizeUserPayload(result);
      options?.onCurrentUser?.(state.currentUser);
      return isAdminUser(state.currentUser);
    } catch {
      state.currentUser = null;
      options?.onCurrentUser?.(null);
      return false;
    }
  }

  function renderStats() {
    const total = state.users.length;
    const admins = state.users.filter(user => user.role === 'admin').length;
    const editors = state.users.filter(user => user.role === 'editor').length;
    const viewers = state.users.filter(user => user.role === 'viewer').length;

    return `
      <div class="user-admin-stats">
        <div class="user-admin-stat-card">
          <div class="user-admin-stat-value">${total}</div>
          <div class="user-admin-stat-label">${strings.totalLabel}</div>
        </div>
        <div class="user-admin-stat-card">
          <div class="user-admin-stat-value">${admins}</div>
          <div class="user-admin-stat-label">${strings.adminLabel}</div>
        </div>
        <div class="user-admin-stat-card">
          <div class="user-admin-stat-value">${editors}</div>
          <div class="user-admin-stat-label">${strings.editorLabel}</div>
        </div>
        <div class="user-admin-stat-card">
          <div class="user-admin-stat-value">${viewers}</div>
          <div class="user-admin-stat-label">${strings.viewerLabel}</div>
        </div>
      </div>
    `;
  }

  function renderUserList() {
    if (!state.users.length) {
      return `<div class="user-admin-empty">${escapeHtml(strings.emptyList)}</div>`;
    }

    return state.users.map(user => `
      <button
        type="button"
        class="user-admin-list-item${user.id === state.selectedUserId ? ' active' : ''}"
        data-action="select-user"
        data-user-id="${escapeHtml(user.id)}">
        <div class="user-admin-list-top">
          <span class="user-admin-list-name">${escapeHtml(user.username)}</span>
          <span class="user-admin-role role-${escapeHtml(user.role)}">${escapeHtml(user.role)}</span>
        </div>
        <div class="user-admin-list-meta">${escapeHtml(user.id)}</div>
        <div class="user-admin-list-meta">${escapeHtml(new Date(user.createdAt).toLocaleString('zh-CN', { hour12: false }))}</div>
      </button>
    `).join('');
  }

  function renderCreateCard() {
    return `
      <div class="user-admin-card">
        <div class="user-admin-card-title">${strings.createTitle}</div>
        <div class="user-admin-card-copy">${strings.createCopy}</div>
        <div class="user-admin-form-grid">
          <div class="user-admin-form-group">
            <label for="userCreateUsername">${strings.createUsernameLabel}</label>
            <input id="userCreateUsername" type="text" placeholder="${escapeHtml(strings.createUsernamePlaceholder)}" />
          </div>
          <div class="user-admin-form-group">
            <label for="userCreateRole">${strings.createRoleLabel}</label>
            <select id="userCreateRole">
              <option value="viewer">viewer</option>
              <option value="editor" selected>editor</option>
              <option value="admin">admin</option>
            </select>
          </div>
        </div>
        <div class="user-admin-form-group">
          <label for="userCreatePassword">${strings.createPasswordLabel}</label>
          <input id="userCreatePassword" type="password" placeholder="${escapeHtml(strings.createPasswordPlaceholder)}" />
        </div>
        <div class="user-admin-inline-actions">
          <button class="btn btn-primary btn-sm" data-action="create-user">${strings.createButton}</button>
        </div>
      </div>
    `;
  }

  function renderSelectedUserCard() {
    const user = getSelectedUser();
    if (!user) {
      return `
        <div class="user-admin-card">
          <div class="user-admin-card-title">${strings.detailTitle}</div>
          <div class="user-admin-empty">${strings.detailEmpty}</div>
        </div>
      `;
    }

    const isCurrentUser = state.currentUser?.id === user.id;
    return `
      <div class="user-admin-card">
        <div class="user-admin-card-title">${strings.detailTitle}</div>
        <div class="user-admin-detail-grid">
          <div class="user-admin-detail-line"><span>${strings.detailUsername}</span><strong>${escapeHtml(user.username)}</strong></div>
          <div class="user-admin-detail-line"><span>${strings.detailId}</span><strong>${escapeHtml(user.id)}</strong></div>
          <div class="user-admin-detail-line"><span>${strings.detailCreatedAt}</span><strong>${escapeHtml(new Date(user.createdAt).toLocaleString('zh-CN', { hour12: false }))}</strong></div>
          <div class="user-admin-detail-line"><span>${strings.detailIdentity}</span><strong>${escapeHtml(formatUserIdentity(user))}</strong></div>
        </div>

        <div class="user-admin-divider"></div>

        <div class="user-admin-form-grid">
          <div class="user-admin-form-group">
            <label for="userEditRole">${strings.editRoleLabel}</label>
            <select id="userEditRole">
              <option value="viewer"${user.role === 'viewer' ? ' selected' : ''}>viewer</option>
              <option value="editor"${user.role === 'editor' ? ' selected' : ''}>editor</option>
              <option value="admin"${user.role === 'admin' ? ' selected' : ''}>admin</option>
            </select>
          </div>
          <div class="user-admin-form-group">
            <label for="userResetPassword">${strings.resetPasswordLabel}</label>
            <input id="userResetPassword" type="password" placeholder="${escapeHtml(strings.resetPasswordPlaceholder)}" />
          </div>
        </div>

        <div class="user-admin-inline-actions">
          <button class="btn btn-primary btn-sm" data-action="update-user-role">${strings.saveRoleButton}</button>
          <button class="btn btn-secondary btn-sm" data-action="reset-user-password">${strings.resetPasswordButton}</button>
          <button class="btn btn-secondary btn-sm user-admin-danger" data-action="delete-user"${isCurrentUser ? ' disabled' : ''}>${strings.deleteButton}</button>
        </div>
        <div class="user-admin-card-copy">
          ${isCurrentUser ? strings.selfDeleteHint : strings.deleteHint}
        </div>
      </div>
    `;
  }

  function renderLockedState() {
    return `
      <div class="user-admin-page">
        <div class="user-admin-card">
          <div class="user-admin-card-title">${strings.lockedTitle}</div>
          <div class="user-admin-card-copy">${strings.lockedCopy}</div>
          <div class="user-admin-message">${escapeHtml(state.message || strings.lockedMessage)}</div>
        </div>
      </div>
    `;
  }

  function renderReadyState() {
    return `
      <div class="user-admin-page">
        <div class="user-admin-toolbar">
          <div>
            <div class="user-admin-title">${strings.toolbarTitle}</div>
            <div class="user-admin-subtitle">${strings.toolbarSubtitle}</div>
          </div>
          <div class="user-admin-toolbar-actions">
            <span class="user-admin-current">${escapeHtml(formatUserIdentity(state.currentUser, '-'))}</span>
            <button class="btn btn-secondary btn-sm" data-action="load-users">${strings.refreshButton}</button>
          </div>
        </div>
        ${renderStats()}
        <div class="user-admin-message">${escapeHtml(state.message)}</div>
        <div class="user-admin-layout">
          <div class="user-admin-sidebar">
            <div class="user-admin-sidebar-title">${strings.listTitle}</div>
            <div class="user-admin-list">${renderUserList()}</div>
          </div>
          <div class="user-admin-content">
            ${renderCreateCard()}
            ${renderSelectedUserCard()}
          </div>
        </div>
      </div>
    `;
  }

  function render() {
    const root = getRoot();
    if (!root) return;

    root.innerHTML = state.currentUser && isAdminUser(state.currentUser)
      ? renderReadyState()
      : renderLockedState();
  }

  async function loadUsers() {
    const ready = await ensureAdminSession();
    if (!ready) {
      state.users = [];
      state.selectedUserId = null;
      setMessage(state.message || strings.loginRequiredMessage);
      render();
      return false;
    }

    try {
      const result = await options.listUsers();
      state.users = listUsers(result);
      if (state.selectedUserId && !state.users.some(user => user.id === state.selectedUserId)) {
        state.selectedUserId = null;
      }
      if (!state.selectedUserId && state.users.length > 0) {
        state.selectedUserId = state.users[0].id;
      }
      setMessage('');
      render();
      return true;
    } catch (error) {
      state.users = [];
      state.selectedUserId = null;
      setMessage(error.message || strings.loadFailedMessage);
      render();
      return false;
    }
  }

  function selectUser(userId) {
    state.selectedUserId = userId;
    render();
  }

  async function createUser() {
    const username = document.getElementById('userCreateUsername')?.value?.trim() || '';
    const password = document.getElementById('userCreatePassword')?.value || '';
    const role = document.getElementById('userCreateRole')?.value || 'editor';

    if (!username || !password) {
      setMessage(strings.createInputRequiredMessage);
      render();
      return;
    }

    try {
      const result = await options.createUser({ username, password, role });
      const user = normalizeUserPayload(result) || { username, role };
      state.selectedUserId = user.id || state.selectedUserId;
      setMessage(strings.createdMessage(user));
      await loadUsers();
    } catch (error) {
      setMessage(`${strings.createFailedMessage}：${error.message}`);
      render();
    }
  }

  async function updateSelectedUserRole() {
    const user = getSelectedUser();
    if (!user) {
      setMessage(strings.selectRequiredMessage);
      render();
      return;
    }

    const role = document.getElementById('userEditRole')?.value || user.role;
    try {
      const result = await options.updateUserRole(user.id, { role });
      const nextUser = normalizeUserPayload(result) || { ...user, role };
      state.selectedUserId = nextUser.id || user.id;
      setMessage(strings.updatedRoleMessage(nextUser));
      await loadUsers();
    } catch (error) {
      setMessage(`${strings.updateRoleFailedMessage}：${error.message}`);
      render();
    }
  }

  async function resetSelectedUserPassword() {
    const user = getSelectedUser();
    if (!user) {
      setMessage(strings.selectRequiredMessage);
      render();
      return;
    }

    const password = document.getElementById('userResetPassword')?.value || '';
    if (!password) {
      setMessage(strings.resetPasswordRequiredMessage);
      render();
      return;
    }

    try {
      await options.resetUserPassword(user.id, { password });
      setMessage(strings.resetPasswordMessage(user));
      await loadUsers();
    } catch (error) {
      setMessage(`${strings.resetPasswordFailedMessage}：${error.message}`);
      render();
    }
  }

  async function deleteSelectedUser() {
    const user = getSelectedUser();
    if (!user) {
      setMessage(strings.selectRequiredMessage);
      render();
      return;
    }

    if (!window.confirm(strings.deleteConfirm(user))) {
      return;
    }

    try {
      await options.deleteUser(user.id);
      state.selectedUserId = null;
      setMessage(strings.deletedMessage(user));
      await loadUsers();
    } catch (error) {
      setMessage(`${strings.deleteFailedMessage}：${error.message}`);
      render();
    }
  }

  return {
    render,
    loadUsers,
    selectUser,
    createUser,
    updateSelectedUserRole,
    resetSelectedUserPassword,
    deleteSelectedUser
  };
}
