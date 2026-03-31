import {
  $,
  api,
  getAuthToken,
  getAuthUser,
  setAuthScope,
  setAuthToken,
  setAuthUser,
  clearAuthState
} from './utils.js';
import {
  applyMetricValues,
  formatCompactNumber,
  registerFullscreenTabs,
  renderSidebarMessage
} from '/dna-shared/js/core/host-shell.js';
import { bindDelegatedDocumentEvents } from '/dna-shared/js/core/dom-actions.js';
import { formatUserIdentity } from '/dna-shared/js/auth/user-session.js';
import { ui } from '/dna-shared/js/ui/ui-manager.js';
import { renderTopology } from '/dna-shared/js/panels/topology.js';
import { initAccountPanel, refreshAccountPanel } from './panels/account-panel.js';
import {
  loadMemories,
  loadSubmissions,
  createNew,
  saveMemory,
  deleteMemory,
  applyTemplate,
  onLayerTypeChanged,
  addFeature,
  addNodeId,
  addTag
} from './panels/memory-editor.js';
import {
  loadUsers,
  selectUser,
  createUser,
  updateSelectedUserRole,
  resetSelectedUserPassword,
  deleteSelectedUser
} from './panels/user-admin.js';
import {
  newChat,
  sendChatMessage,
  handleChatKeydown,
  autoResizeInput,
  initChatResize,
  switchChatMode,
  showSessionList,
  stopChat,
  openModelDropdown
} from './chat/chat-panel.js';
import { openLlmSettings, closeLlmSettings } from './chat/llm-settings.js';

let currentUser = null;
let workspaceSnapshot = null;
let selectedWorkspaceId = null;

const DEFAULT_AUTH_MESSAGE = '登录后可管理正式知识库。';

function initUI() {
  const mainArea = $('clientMain');
  if (!mainArea) return;

  ui.init(mainArea);

  registerFullscreenTabs(ui, [
    { id: 'overview', tabButtonSelector: '.workspace-tab[data-tab="overview"]' },
    { id: 'connections', tabButtonSelector: '.workspace-tab[data-tab="connections"]' },
    { id: 'account', tabButtonSelector: '.workspace-tab[data-tab="account"]' },
    { id: 'topology', tabButtonSelector: '.workspace-tab[data-tab="topology"]' },
    { id: 'memory', tabButtonSelector: '.workspace-tab[data-tab="memory"]' }
  ], $);
}

function formatWorkspaceMode(mode) {
  return mode === 'team' ? '团队' : '个人';
}

function formatRole(role) {
  switch (String(role || '').toLowerCase()) {
    case 'admin':
      return '管理员';
    case 'viewer':
      return '只读';
    case 'editor':
      return '编辑';
    default:
      return role || '未知角色';
  }
}

function buildWorkspaceAuthScope(workspace) {
  const workspaceId = workspace?.id || 'default';
  const serverBaseUrl = String(workspace?.serverBaseUrl || 'local').trim().toLowerCase();
  return `${workspaceId}@${serverBaseUrl}`;
}

function syncAuthScope(workspace) {
  if (!workspace) return false;

  const changed = setAuthScope(buildWorkspaceAuthScope(workspace));
  currentUser = getAuthUser();
  renderAuthState();
  return changed;
}

function handleWorkspaceAuthChanged(workspace, fallbackMessage) {
  syncAuthScope(workspace);

  if (getAuthToken() && currentUser) {
    setAuthMessage(`已切换到“${workspace?.name || '当前工作区'}”，已恢复该工作区的登录态。`, false);
    return;
  }

  setAuthMessage(fallbackMessage, false);
  resetProtectedOverview('需要登录');
  $('memoryEmptyState')?.classList.remove('hidden');
}

function createWorkspaceDraft(defaults = {}) {
  return {
    id: '',
    name: defaults.name || '',
    mode: defaults.mode || 'personal',
    serverBaseUrl: defaults.serverBaseUrl || '',
    workspaceRoot: defaults.workspaceRoot || ''
  };
}

function setStatus(text) {
  const statusText = $('statusText');
  if (statusText) statusText.textContent = text;
}

function setRefreshInfo(text) {
  const refreshInfo = $('refreshInfo');
  if (refreshInfo) refreshInfo.textContent = text;
}

function renderWorkspaceSummary(workspace) {
  const targetServer = $('targetServer');
  const mcpAddress = $('mcpAddress');
  const currentBadge = $('workspaceCurrentBadge');
  const currentServer = $('workspaceCurrentServer');

  if (!workspace) {
    if (targetServer) targetServer.textContent = '-';
    if (mcpAddress) mcpAddress.textContent = '模式: -';
    if (currentBadge) currentBadge.textContent = '-';
    if (currentServer) currentServer.textContent = '-';
    return;
  }

  if (targetServer) targetServer.textContent = workspace.name || workspace.serverBaseUrl || '-';
  if (mcpAddress) mcpAddress.textContent = `${formatWorkspaceMode(workspace.mode)} · ${workspace.serverBaseUrl || '-'}`;
  if (currentBadge) currentBadge.textContent = `${workspace.name} (${formatWorkspaceMode(workspace.mode)})`;
  if (currentServer) currentServer.textContent = `${workspace.serverBaseUrl} · ${workspace.workspaceRoot}`;
}

function populateWorkspaceForm(workspace) {
  $('workspaceId').value = workspace?.id || '';
  $('workspaceName').value = workspace?.name || '';
  $('workspaceMode').value = workspace?.mode || 'personal';
  $('workspaceServerBaseUrl').value = workspace?.serverBaseUrl || '';
  $('workspaceRoot').value = workspace?.workspaceRoot || '';
}

function getSelectedWorkspace() {
  if (!workspaceSnapshot?.workspaces?.length) return null;

  return workspaceSnapshot.workspaces.find(item => item.id === selectedWorkspaceId)
    || workspaceSnapshot.workspaces.find(item => item.id === workspaceSnapshot.currentWorkspaceId)
    || workspaceSnapshot.workspaces[0];
}

function getCurrentWorkspace() {
  return workspaceSnapshot?.currentWorkspace || getSelectedWorkspace() || null;
}

async function refreshAccountPanelIfVisible() {
  if (!ui.activeTabId || ui.activeTabId === 'account') {
    await refreshAccountPanel();
  }
}

function renderWorkspaceList() {
  const container = $('workspaceList');
  if (!container) return;

  const workspaces = workspaceSnapshot?.workspaces || [];
  if (!workspaces.length) {
    container.innerHTML = '<div class="empty">还没有保存的工作区。</div>';
    return;
  }

  container.innerHTML = workspaces.map(workspace => {
    const selected = workspace.id === selectedWorkspaceId ? ' selected' : '';
    const current = workspace.id === workspaceSnapshot.currentWorkspaceId ? ' current' : '';
    const badge = workspace.id === workspaceSnapshot.currentWorkspaceId
      ? '<span class="workspace-badge">当前</span>'
      : '';

    return `
      <button
        type="button"
        class="workspace-list-item${selected}${current}"
        data-action="select-workspace"
        data-workspace-id="${workspace.id}">
        <div class="workspace-list-title">
          <span>${workspace.name}</span>
          ${badge}
        </div>
        <div class="workspace-list-meta">${formatWorkspaceMode(workspace.mode)} · ${workspace.serverBaseUrl}</div>
        <div class="workspace-list-path">${workspace.workspaceRoot}</div>
      </button>
    `;
  }).join('');
}

function didCurrentWorkspaceIdentityChange(previousWorkspace, nextWorkspace) {
  if (!previousWorkspace && nextWorkspace) return true;
  if (!previousWorkspace || !nextWorkspace) return false;

  return previousWorkspace.id !== nextWorkspace.id ||
    previousWorkspace.serverBaseUrl !== nextWorkspace.serverBaseUrl;
}

function resetProtectedOverview(message = '需要登录') {
  $('memoryTotal').textContent = '-';
  $('memoryFreshness').textContent = message;
  $('statModules').textContent = '-';
  $('statEdges').textContent = '-';
  $('statContainment').textContent = '-';
  $('statCollaboration').textContent = '-';
}

async function loadWorkspaces(options = {}) {
  const preserveSelection = options.preserveSelection ?? true;
  const snapshot = await api('/client/workspaces', { skipAuth: true });
  workspaceSnapshot = snapshot;

  if (!preserveSelection || !selectedWorkspaceId || !snapshot.workspaces.some(item => item.id === selectedWorkspaceId)) {
    selectedWorkspaceId = snapshot.currentWorkspaceId;
  }

  syncAuthScope(snapshot.currentWorkspace);
  renderWorkspaceSummary(snapshot.currentWorkspace);
  renderWorkspaceList();
  populateWorkspaceForm(getSelectedWorkspace() || createWorkspaceDraft(snapshot.defaults));
  await refreshAccountPanelIfVisible();
}

function newWorkspace() {
  selectedWorkspaceId = null;
  const defaults = workspaceSnapshot?.defaults || {};
  populateWorkspaceForm(createWorkspaceDraft({
    name: defaults.mode === 'team' ? '新团队工作区' : '新个人工作区',
    mode: defaults.mode || 'personal',
    serverBaseUrl: defaults.serverBaseUrl || '',
    workspaceRoot: defaults.workspaceRoot || ''
  }));
  renderWorkspaceList();
  setStatus('正在编辑新的工作区草稿。');
}

function selectWorkspace(workspaceId) {
  selectedWorkspaceId = workspaceId;
  renderWorkspaceList();
  populateWorkspaceForm(getSelectedWorkspace() || createWorkspaceDraft(workspaceSnapshot?.defaults));
}

function buildWorkspacePayload() {
  return {
    name: $('workspaceName')?.value?.trim(),
    mode: $('workspaceMode')?.value || 'personal',
    serverBaseUrl: $('workspaceServerBaseUrl')?.value?.trim(),
    workspaceRoot: $('workspaceRoot')?.value?.trim()
  };
}

async function saveWorkspace() {
  const workspaceId = $('workspaceId')?.value?.trim() || '';
  const previousCurrent = workspaceSnapshot?.currentWorkspace || null;
  const payload = buildWorkspacePayload();

  const result = workspaceId
    ? await api(`/client/workspaces/${encodeURIComponent(workspaceId)}`, {
      method: 'PUT',
      skipAuth: true,
      body: payload
    })
    : await api('/client/workspaces', {
      method: 'POST',
      skipAuth: true,
      body: payload
    });

  workspaceSnapshot = result.snapshot;
  selectedWorkspaceId = result.workspace?.id || workspaceSnapshot.currentWorkspaceId;
  renderWorkspaceSummary(workspaceSnapshot.currentWorkspace);
  renderWorkspaceList();
  populateWorkspaceForm(getSelectedWorkspace());

  if (didCurrentWorkspaceIdentityChange(previousCurrent, workspaceSnapshot.currentWorkspace)) {
    handleWorkspaceAuthChanged(workspaceSnapshot.currentWorkspace, '当前工作区已更新，请登录对应服务器账号。');
  }

  setStatus(`工作区已保存：${result.workspace?.name || '未命名工作区'}`);
  await loadStatus();
}

async function setCurrentWorkspace() {
  const workspaceId = $('workspaceId')?.value?.trim() || selectedWorkspaceId || '';
  if (!workspaceId) {
    setStatus('请先选择一个工作区。');
    return;
  }

  const result = await api('/client/workspaces/current', {
    method: 'PUT',
    skipAuth: true,
    body: { workspaceId }
  });

  workspaceSnapshot = result.snapshot;
  selectedWorkspaceId = result.workspace?.id || workspaceSnapshot.currentWorkspaceId;
  renderWorkspaceSummary(workspaceSnapshot.currentWorkspace);
  renderWorkspaceList();
  populateWorkspaceForm(getSelectedWorkspace());
  handleWorkspaceAuthChanged(workspaceSnapshot.currentWorkspace, '当前工作区已切换，请登录对应服务器账号。');
  setStatus(`当前工作区：${result.workspace?.name || workspaceId}`);
  await loadStatus();
}

async function deleteWorkspace() {
  const workspaceId = $('workspaceId')?.value?.trim() || selectedWorkspaceId || '';
  if (!workspaceId) {
    setStatus('请先选择一个工作区。');
    return;
  }

  const workspace = getSelectedWorkspace();
  if (!workspace) {
    setStatus('未找到该工作区。');
    return;
  }

  if (!window.confirm(`确定删除工作区“${workspace.name}”吗？`)) {
    return;
  }

  const wasCurrent = workspaceId === workspaceSnapshot?.currentWorkspaceId;
  const result = await api(`/client/workspaces/${encodeURIComponent(workspaceId)}`, {
    method: 'DELETE',
    skipAuth: true
  });

  workspaceSnapshot = result.snapshot;
  selectedWorkspaceId = workspaceSnapshot.currentWorkspaceId;
  renderWorkspaceSummary(workspaceSnapshot.currentWorkspace);
  renderWorkspaceList();
  populateWorkspaceForm(getSelectedWorkspace());

  if (wasCurrent) {
    handleWorkspaceAuthChanged(workspaceSnapshot.currentWorkspace, '当前工作区已删除，请登录新的活动工作区。');
  }

  setStatus(`工作区已删除：${result.removedWorkspace?.name || workspaceId}`);
  await loadStatus();
}

function applyToolState(elementId, installed) {
  const element = $(elementId);
  if (!element) return;

  element.classList.remove('healthy', 'degraded', 'error');
  if (installed) {
    element.classList.add('healthy');
    element.textContent = '已安装';
    return;
  }

  element.classList.add('degraded');
  element.textContent = '未安装';
}

async function loadToolingStatus() {
  try {
    const tooling = await api('/client/tooling/list', { skipAuth: true });
    $('toolingWorkspace').textContent = `工作区: ${tooling.workspaceRoot || '-'}`;

    const cursor = (tooling.targets || []).find(target => target.id === 'cursor');
    const codex = (tooling.targets || []).find(target => target.id === 'codex');
    applyToolState('cursorToolState', Boolean(cursor?.installed));
    applyToolState('codexToolState', Boolean(codex?.installed));
  } catch (error) {
    $('toolingWorkspace').textContent = `工作区不可用（${error.message}）`;
    const cursor = $('cursorToolState');
    const codex = $('codexToolState');
    if (cursor) {
      cursor.classList.remove('healthy', 'degraded');
      cursor.classList.add('error');
      cursor.textContent = '错误';
    }
    if (codex) {
      codex.classList.remove('healthy', 'degraded');
      codex.classList.add('error');
      codex.textContent = '错误';
    }
  }
}

async function installTooling(target) {
  try {
    setStatus(`正在安装 ${target} 工具链...`);
    const result = await api('/client/tooling/install', {
      method: 'POST',
      skipAuth: true,
      body: {
        target,
        replaceExisting: true
      }
    });

    const reports = result.reports || [];
    const written = reports.reduce((sum, report) => sum + (report.writtenFiles?.length || 0), 0);
    const skipped = reports.reduce((sum, report) => sum + (report.skippedFiles?.length || 0), 0);
    const warnings = reports.reduce((sum, report) => sum + (report.warnings?.length || 0), 0);
    setStatus(`工具安装完成：写入 ${written}，跳过 ${skipped}，警告 ${warnings}`);
    setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
    await loadToolingStatus();
  } catch (error) {
    setStatus(`工具安装失败：${error.message}`);
  }
}

function applyHealthState(element, value, okText = '正常', degradedText = '降级') {
  if (!element) return;
  element.classList.remove('healthy', 'degraded', 'error');
  if (value === 'ok') {
    element.classList.add('healthy');
    element.textContent = okText;
    return;
  }

  element.classList.add('degraded');
  element.textContent = degradedText;
}

function setAuthMessage(text, isError = false) {
  const element = $('authMessage');
  if (!element) return;
  element.textContent = text;
  element.classList.toggle('error', isError);
}

function renderAuthState() {
  const user = currentUser || getAuthUser();
  const hasToken = Boolean(getAuthToken());
  const loginForm = $('authLoginForm');
  const userBar = $('authUserBar');
  const authStateText = $('authStateText');
  const authUserInfo = $('authUserInfo');

  if (loginForm) loginForm.classList.toggle('hidden', hasToken && Boolean(user));
  if (userBar) userBar.classList.toggle('hidden', !(hasToken && user));

  if (!hasToken || !user) {
    if (authStateText) authStateText.textContent = '未登录';
    if (authUserInfo) authUserInfo.textContent = '-';
    return;
  }

  if (authStateText) authStateText.textContent = `已登录为${formatRole(user.role)}`;
  if (authUserInfo) authUserInfo.textContent = formatUserIdentity(user);
}

async function ensureAuthenticatedUser(options = {}) {
  if (!getAuthToken()) {
    currentUser = null;
    renderAuthState();
    await refreshAccountPanelIfVisible();
    return null;
  }

  try {
    const result = await api('/auth/me');
    currentUser = result.user || result;
    setAuthUser(currentUser);
    renderAuthState();
    await refreshAccountPanelIfVisible();
    return currentUser;
  } catch (error) {
    if (error.status === 401 || error.status === 403) {
      currentUser = null;
      clearAuthState();
      renderAuthState();
      await refreshAccountPanelIfVisible();
      setAuthMessage('登录态已过期，请重新登录。', true);
      if (!options.silent) {
        setStatus('登录态已过期，请重新登录。');
      }
      return null;
    }

    throw error;
  }
}

async function login() {
  const username = $('authUsername')?.value?.trim() || '';
  const password = $('authPassword')?.value || '';

  if (!username || !password) {
    setAuthMessage('请输入用户名和密码。', true);
    return;
  }

  try {
    setAuthMessage('正在登录...', false);
    const result = await api('/auth/login', {
      method: 'POST',
      body: { username, password },
      skipAuth: true
    });

    setAuthToken(result.token);
    currentUser = result.user || null;
    setAuthUser(currentUser);
    if ($('authPassword')) $('authPassword').value = '';
    renderAuthState();
    setAuthMessage(`已登录：${currentUser?.username || username}`, false);
    await refreshAccountPanelIfVisible();
    await refreshAll();
  } catch (error) {
    setAuthMessage(error.message, true);
    setStatus(`登录失败：${error.message}`);
  }
}

async function logout() {
  currentUser = null;
  clearAuthState();
  renderAuthState();
  setAuthMessage('已退出登录，受保护的数据已隐藏。', false);
  resetProtectedOverview();
  $('memoryEmptyState')?.classList.remove('hidden');
  setStatus('已退出登录');
  await refreshAccountPanelIfVisible();
  await loadStatus();
}

async function loadStatus() {
  try {
    const clientStatus = await api('/client/status', { skipAuth: true });
    const workspace = clientStatus.currentWorkspace || null;
    syncAuthScope(workspace);

    applyHealthState($('clientHealth'), clientStatus.client, '正常', '降级');
    $('clientPort').textContent = '本地端口 5052';
    renderWorkspaceSummary(workspace);
    $('serverDashboardLink').href = clientStatus.targetServer || 'http://127.0.0.1:5051';
    await loadToolingStatus();

    const serverStatus = clientStatus.serverStatus || null;
    if (serverStatus && !clientStatus.error) {
      $('serverHealth').classList.remove('error');
      $('serverHealth').classList.add('healthy');
      $('serverHealth').textContent = '在线';
      $('serverUptime').textContent = `运行时长: ${serverStatus.uptime || '-'}`;
      $('overviewSummary').textContent =
        `${workspace?.name || '当前工作区'} 已连接到 ${clientStatus.targetServer}。Server 启动时间：${serverStatus.startedAt || '未知'}。`;
    } else {
      $('serverHealth').classList.remove('healthy');
      $('serverHealth').classList.add('error');
      $('serverHealth').textContent = '离线';
      $('serverUptime').textContent = clientStatus.error || '无法连接服务器';
      $('overviewSummary').textContent =
        `${workspace?.name || '当前工作区'} 已配置完成，但目标 Server 当前不可用。`;
      resetProtectedOverview('等待服务器可用');
      setStatus('Client 已启动，但 Server 当前离线。');
      setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
      return;
    }

    const user = await ensureAuthenticatedUser({ silent: true });
    if (!user) {
      resetProtectedOverview('需要登录');
      $('overviewSummary').textContent =
        `${workspace?.name || '当前工作区'} 已连接，但当前还没有可用的服务器身份。`;
      setStatus(DEFAULT_AUTH_MESSAGE);
      setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
      return;
    }

    const memoryStats = await api('/memory/stats');
    $('memoryTotal').textContent = formatCompactNumber(memoryStats.total ?? 0);
    const freshness = memoryStats.byFreshness || {};
    $('memoryFreshness').textContent =
      `新鲜 ${freshness.Fresh ?? freshness.fresh ?? 0} / 待整理 ${freshness.Aging ?? freshness.aging ?? 0}`;

    setStatus('客户端状态已刷新');
    setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
  } catch (error) {
    $('clientHealth').classList.add('error');
    $('clientHealth').textContent = '错误';
    $('serverHealth').classList.add('error');
    $('serverHealth').textContent = '未知';
    $('overviewSummary').textContent = error.message;
    setStatus(`状态刷新失败：${error.message}`);
  }
}

async function loadTopology() {
  try {
    const user = await ensureAuthenticatedUser({ silent: true });
    if (!user) {
      resetProtectedOverview('需要登录');
      if ($('archSidebar')) {
        renderSidebarMessage($, 'archSidebar', '需要登录', '请先登录服务器账号，再加载正式知识拓扑。');
      }
      setStatus('请先登录再加载拓扑。');
      return;
    }

    setStatus('正在加载拓扑...');
    const topoData = await api('/topology');
    renderTopology(topoData);

    const modulesCount = topoData.modules ? topoData.modules.length : 0;
    const edgesCount = topoData.edges ? topoData.edges.length : 0;
    const containmentCount = topoData.containmentEdges ? topoData.containmentEdges.length : 0;
    const collaborationCount = topoData.collaborationEdges ? topoData.collaborationEdges.length : 0;

    applyMetricValues($, [
      { id: 'statModules', value: modulesCount },
      { id: 'statEdges', value: edgesCount },
      { id: 'statContainment', value: containmentCount },
      { id: 'statCollaboration', value: collaborationCount }
    ], formatCompactNumber);
    setStatus('拓扑已刷新');
  } catch (error) {
    setStatus(`拓扑加载失败：${error.message}`);
    renderSidebarMessage($, 'archSidebar', '拓扑加载失败', error.message);
  }
}

async function loadKnowledge() {
  try {
    const user = await ensureAuthenticatedUser({ silent: true });
    if (!user) {
      $('memoryEmptyState')?.classList.remove('hidden');
      setStatus('请先登录再使用知识工作区。');
      return;
    }

    setStatus('正在加载知识工作区...');
    await loadMemories();
    if ($('memoryEditorForm').style.display !== 'none') $('memoryEmptyState')?.classList.add('hidden');
    else $('memoryEmptyState')?.classList.remove('hidden');
    setStatus('知识工作区已刷新');
  } catch (error) {
    setStatus(`知识工作区加载失败：${error.message}`);
  }
}

async function refreshTab(tabId) {
  if (tabId === 'overview') {
    await loadStatus();
    return;
  }

  if (tabId === 'connections') {
    await loadWorkspaces();
    return;
  }

  if (tabId === 'account') {
    await refreshAccountPanel();
    return;
  }

  if (tabId === 'memory') {
    await loadKnowledge();
    return;
  }

  await loadTopology();
}

async function refreshAll() {
  await loadWorkspaces();
  await loadStatus();

  const activeTabId = ui.activeTabId || 'overview';
  if (activeTabId !== 'overview' && activeTabId !== 'connections') {
    await refreshTab(activeTabId);
  }
}

function resolveInitialTab() {
  const hash = window.location.hash.replace(/^#/, '').trim();
  return hash || 'overview';
}

async function switchTab(tabId) {
  ui.switchTab(tabId);
  if (window.location.hash !== `#${tabId}`) {
    history.replaceState(null, '', `#${tabId}`);
  }
  await refreshTab(tabId);
}

async function handleAction(action, element) {
  switch (action) {
    case 'login':
      await login();
      break;
    case 'logout':
      await logout();
      break;
    case 'refresh-all':
      await refreshAll();
      break;
    case 'switch-tab':
      if (element?.dataset.tabTarget) await switchTab(element.dataset.tabTarget);
      break;
    case 'refresh-workspaces':
      await loadWorkspaces();
      break;
    case 'new-workspace':
      newWorkspace();
      break;
    case 'select-workspace':
      if (element?.dataset.workspaceId) selectWorkspace(element.dataset.workspaceId);
      break;
    case 'save-workspace':
      await saveWorkspace();
      break;
    case 'set-current-workspace':
      await setCurrentWorkspace();
      break;
    case 'delete-workspace':
      await deleteWorkspace();
      break;
    case 'load-users':
      await loadUsers();
      break;
    case 'create-user':
      await createUser();
      break;
    case 'update-user-role':
      await updateSelectedUserRole();
      break;
    case 'reset-user-password':
      await resetSelectedUserPassword();
      break;
    case 'delete-user':
      await deleteSelectedUser();
      break;
    case 'refresh-tooling-status':
      await loadToolingStatus();
      break;
    case 'install-tooling':
      if (element?.dataset.target) await installTooling(element.dataset.target);
      break;
    case 'refresh-topology':
      await loadTopology();
      break;
    case 'refresh-memories':
      await loadKnowledge();
      break;
    case 'create-memory':
      createNew();
      break;
    case 'load-submissions':
      await loadSubmissions();
      break;
    case 'apply-memory-template':
      applyTemplate();
      break;
    case 'add-feature':
      addFeature();
      break;
    case 'add-node-id':
      addNodeId();
      break;
    case 'add-tag':
      addTag();
      break;
    case 'save-memory':
      await saveMemory();
      break;
    case 'delete-memory':
      await deleteMemory();
      break;
    case 'show-session-list':
      await showSessionList();
      break;
    case 'new-chat':
      newChat();
      break;
    case 'send-chat-message':
      await sendChatMessage();
      break;
    case 'stop-chat':
      stopChat();
      break;
    case 'switch-chat-mode':
      switchChatMode(element?.dataset.mode || 'agent');
      break;
    case 'open-model-dropdown':
      openModelDropdown();
      break;
    case 'open-llm-settings':
      await openLlmSettings();
      break;
    case 'close-llm-settings':
      closeLlmSettings();
      break;
    default:
      if (action === 'select-user' && element?.dataset.userId) {
        selectUser(element.dataset.userId);
        break;
      }
      break;
  }
}

async function handleChangeAction(action) {
  switch (action) {
    case 'load-memories':
      await loadMemories();
      break;
    case 'memory-layer-type-changed':
      onLayerTypeChanged();
      break;
    default:
      break;
  }
}

function bindUiEvents() {
  bindDelegatedDocumentEvents([
    {
      eventName: 'click',
      selector: '#llmSettingsOverlay',
      handler: ({ event, element }) => {
        if (event.target === element) {
          closeLlmSettings();
        }
      }
    },
    {
      eventName: 'click',
      selector: '[data-action]',
      preventDefault: true,
      handler: ({ element }) => void handleAction(element.dataset.action, element)
    },
    {
      eventName: 'change',
      selector: '[data-change-action]',
      handler: ({ element }) => void handleChangeAction(element.dataset.changeAction)
    },
    {
      eventName: 'keydown',
      selector: '#authPassword',
      handler: ({ event }) => {
        if (event.key === 'Enter') {
          event.preventDefault();
          void login();
        }
      }
    },
    {
      eventName: 'keydown',
      selector: '[data-keydown-action]',
      handler: ({ event, element }) => {
        if (element.dataset.keydownAction === 'chat-keydown') {
          handleChatKeydown(event);
        }
      }
    },
    {
      eventName: 'input',
      selector: '[data-input-action]',
      handler: ({ element }) => {
        if (element.dataset.inputAction === 'chat-auto-resize') {
          autoResizeInput();
        }
      }
    }
  ]);
}

document.addEventListener('DOMContentLoaded', async () => {
  bindUiEvents();
  initUI();
  initAccountPanel({
    getCurrentUser: () => currentUser || getAuthUser(),
    getCurrentWorkspace,
    loadUsers
  });
  initChatResize();
  await loadWorkspaces({ preserveSelection: false });
  currentUser = getAuthUser();
  renderAuthState();
  setAuthMessage(DEFAULT_AUTH_MESSAGE, false);
  await loadStatus();
  await switchTab(resolveInitialTab());
});
