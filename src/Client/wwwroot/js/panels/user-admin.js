import { $, api, getAuthToken, getAuthUser, setAuthUser } from '../utils.js';
import { createUserAdminController } from '/dna-shared/js/panels/user-admin-common.js';

const controller = createUserAdminController({
  rootId: 'userAdminRoot',
  getRoot: () => $('userAdminRoot'),
  getAuthToken,
  getCurrentUser: () => getAuthUser(),
  onCurrentUser: user => setAuthUser(user),
  loadCurrentUser: async () => api('/auth/me'),
  listUsers: async () => api('/auth/users'),
  createUser: async payload => api('/auth/users', { method: 'POST', body: payload }),
  updateUserRole: async (id, payload) => api(`/auth/users/${encodeURIComponent(id)}/role`, { method: 'PUT', body: payload }),
  resetUserPassword: async (id, payload) => api(`/auth/users/${encodeURIComponent(id)}/password`, { method: 'PUT', body: payload }),
  deleteUser: async id => api(`/auth/users/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  strings: {
    lockedCopy: '请先使用当前工作区对应的管理员账号登录，再管理用户账号与角色。',
    toolbarSubtitle: '通过 Client 透传服务端权限接口，在不离开当前工作区的前提下完成账号管理。'
  }
});

export const loadUsers = () => controller.loadUsers();
export const selectUser = userId => controller.selectUser(userId);
export const createUser = () => controller.createUser();
export const updateSelectedUserRole = () => controller.updateSelectedUserRole();
export const resetSelectedUserPassword = () => controller.resetSelectedUserPassword();
export const deleteSelectedUser = () => controller.deleteSelectedUser();
