import { $, api, getAuthToken } from '../utils.js';
import { createUserAdminController } from '/dna-shared/js/panels/user-admin-common.js';

const controller = createUserAdminController({
  rootId: 'userAdminRoot',
  getRoot: () => $('userAdminRoot'),
  getAuthToken,
  loadCurrentUser: async () => api('/auth/me'),
  listUsers: async () => api('/auth/users'),
  createUser: async payload => api('/auth/users', { method: 'POST', body: payload }),
  updateUserRole: async (id, payload) => api(`/auth/users/${encodeURIComponent(id)}/role`, { method: 'PUT', body: payload }),
  resetUserPassword: async (id, payload) => api(`/auth/users/${encodeURIComponent(id)}/password`, { method: 'PUT', body: payload }),
  deleteUser: async id => api(`/auth/users/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  strings: {
    lockedCopy: '请先在“审核队列”中以管理员身份登录，再管理用户账号与角色。'
  }
});

export const loadUsers = () => controller.loadUsers();
export const selectUser = userId => controller.selectUser(userId);
export const createUser = () => controller.createUser();
export const updateSelectedUserRole = () => controller.updateSelectedUserRole();
export const resetSelectedUserPassword = () => controller.resetSelectedUserPassword();
export const deleteSelectedUser = () => controller.deleteSelectedUser();
