import {
  $,
  api,
  escapeHtml,
  setAuthScope
} from './utils.js';
import {
  applyMetricValues,
  formatCompactNumber,
  registerFullscreenTabs,
  renderSidebarMessage
} from '/dna-shared/js/core/host-shell.js';
import { bindDelegatedDocumentEvents } from '/dna-shared/js/core/dom-actions.js';
import { ui } from '/dna-shared/js/ui/ui-manager.js';
import { renderTopology } from '/dna-shared/js/panels/topology.js';
import {
  loadMemories,
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

let workspaceSnapshot = null;
let selectedWorkspaceId = null;

const DEFAULT_CONNECTION_MESSAGE = '无账号模式：由 Server 白名单控制连接权限。';
const PROJECT_LOCKED_MODE = new URLSearchParams(window.location.search).get('mode') === 'project';
const PROJECT_LOCKED_MESSAGE = '项目锁定模式：服务器连接由 .project.dna 指定，客户端不提供切换入口。';

function applyProjectLockedModeUI() {
  if (!PROJECT_LOCKED_MODE) return;

  document.body.classList.add('project-locked-mode');

  document.querySelectorAll('[data-project-lock-hidden]').forEach(element => {
    element.classList.add('hidden');
  });

  const subtitle = document.querySelector('.client-subtitle');
  if (subtitle) {
    subtitle.textContent = '本地 MCP 宿主与知识工作区入口。当前模式：项目锁定连接（.project.dna）。';
  }

  setText('availableWorkspaceHint', PROJECT_LOCKED_MESSAGE);
}

function initUI() {
  const mainArea = $('clientMain');
  if (!mainArea) return;

  ui.init(mainArea);

  registerFullscreenTabs(ui, [
    { id: 'overview', tabButtonSelector: '.workspace-tab[data-tab="overview"]' },
    { id: 'topology', tabButtonSelector: '.workspace-tab[data-tab="topology"]' },
    { id: 'memory', tabButtonSelector: '.workspace-tab[data-tab="memory"]' }
  ], $);
}

function formatWorkspaceMode(mode) {
  return mode === 'team' ? '团队' : '个人';
}

function getHostName(baseUrl) {
  try {
    return new URL(baseUrl).hostname;
  } catch {
    return baseUrl || '-';
  }
}

function buildWorkspaceAuthScope(workspace) {
  const workspaceId = workspace?.id || 'default';
  const serverBaseUrl = String(workspace?.serverBaseUrl || 'local').trim().toLowerCase();
  return `${workspaceId}@${serverBaseUrl}`;
}

function syncAuthScope(workspace) {
  if (!workspace) return false;
  return setAuthScope(buildWorkspaceAuthScope(workspace));
}

function handleWorkspaceAuthChanged(workspace, fallbackMessage) {
  syncAuthScope(workspace);
  if (PROJECT_LOCKED_MODE) {
    setAuthMessage(PROJECT_LOCKED_MESSAGE, false);
  } else {
    setAuthMessage(`已切换到“${workspace?.name || '当前工作区'}”`, false);
  }
  resetProtectedOverview('等待连接');
  $('memoryEmptyState')?.classList.remove('hidden');
  if (fallbackMessage) setStatus(fallbackMessage);
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

function setText(id, value) {
  const element = $(id);
  if (element) element.textContent = value;
}

function getClientAddressMeta() {
  const origin = window.location.origin;
  return `${origin} · MCP ${origin}/mcp`;
}

function getMcpAddress() {
  return `${window.location.origin}/mcp`;
}

async function copyMcpAddress() {
  const text = getMcpAddress();
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
    } else {
      const input = document.createElement('textarea');
      input.value = text;
      input.setAttribute('readonly', 'readonly');
      input.style.position = 'fixed';
      input.style.left = '-9999px';
      document.body.appendChild(input);
      input.select();
      document.execCommand('copy');
      input.remove();
    }
    setStatus(`MCP 地址已复制：${text}`);
  } catch (error) {
    setStatus(`复制失败，请手动复制：${text}`);
  }
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

function syncManualServerAddress(workspace) {
  const input = $('manualServerAddress');
  if (!input) return;
  input.value = workspace?.serverBaseUrl || '';
}

function renderWorkspaceQuickSelect() {
  const select = $('availableWorkspaceSelect') || $('workspaceQuickSelect');
  if (!select) return;

  const workspaces = getVisibleWorkspaces();
  select.innerHTML = '';
  setText('availableWorkspaceCount', String(workspaces.length));

  if (!workspaces.length) {
    const option = document.createElement('option');
    option.value = '';
    option.textContent = '暂无工作区';
    select.appendChild(option);
    select.disabled = true;
    setText('availableWorkspaceHint', '请先手动填写 Server 地址并连接');
    return;
  }

  for (const workspace of workspaces) {
    const option = document.createElement('option');
    option.value = workspace.id;
    option.textContent = getHostName(workspace.serverBaseUrl);
    if (workspace.id === workspaceSnapshot?.currentWorkspaceId) {
      option.selected = true;
    }
    select.appendChild(option);
  }
  select.disabled = false;
  setText('availableWorkspaceHint', '下拉切换已保存工作区，或手动填写地址连接');
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

function renderWorkspaceList() {
  const container = $('workspaceList');
  if (!container) return;

  const workspaces = getVisibleWorkspaces();
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
          <span>${getHostName(workspace.serverBaseUrl) || workspace.name}</span>
          ${badge}
        </div>
        <div class="workspace-list-meta">${formatWorkspaceMode(workspace.mode)}</div>
      </button>
    `;
  }).join('');
}

function getVisibleWorkspaces() {
  const workspaces = workspaceSnapshot?.workspaces || [];
  if (!PROJECT_LOCKED_MODE) return workspaces;

  const currentId = workspaceSnapshot?.currentWorkspaceId;
  return workspaces.filter(item => item.id === currentId);
}

function formatPermissionLabel(role) {
  switch (String(role || '').toLowerCase()) {
    case 'admin':
      return '管理员';
    case 'editor':
      return '编辑';
    case 'viewer':
      return '只读';
    default:
      return '未知';
  }
}

function renderAccessProfile(access, workspace) {
  if (!workspace) {
    setText('accessProfileRole', '-');
    setText('accessProfileIp', '-');
    setText('accessProfileName', '-');
    setText('accessProfileNote', '-');
    setText('accessProfileRule', '尚未选择服务器。');
    return;
  }

  if (!access) {
    setText('accessProfileRole', '-');
    setText('accessProfileIp', '-');
    setText('accessProfileName', '-');
    setText('accessProfileNote', '-');
    setText('accessProfileRule', '正在读取当前客户端权限...');
    return;
  }

  if (access.allowed === false) {
    setText('accessProfileRole', '未授权');
    setText('accessProfileIp', access.remoteIp || '-');
    setText('accessProfileName', '-');
    setText('accessProfileNote', '-');
    setText('accessProfileRule', access.reason || '当前客户端 IP 不在白名单中。');
    return;
  }

  setText('accessProfileRole', formatPermissionLabel(access.role));
  setText('accessProfileIp', access.remoteIp || '-');
  setText('accessProfileName', access.entryName || '-');
  setText('accessProfileNote', access.note || '-');
  setText('accessProfileRule', '权限由服务器白名单分配，客户端仅展示不可修改。');
}

function didCurrentWorkspaceIdentityChange(previousWorkspace, nextWorkspace) {
  if (!previousWorkspace && nextWorkspace) return true;
  if (!previousWorkspace || !nextWorkspace) return false;

  return previousWorkspace.id !== nextWorkspace.id ||
    previousWorkspace.serverBaseUrl !== nextWorkspace.serverBaseUrl;
}

function resetProtectedOverview(message = '暂不可用') {
  setText('availableWorkspaceHint', message);
  $('statModules').textContent = '-';
  $('statEdges').textContent = '-';
  $('statContainment').textContent = '-';
  $('statCollaboration').textContent = '-';
}

async function loadWorkspaces(options = {}) {
  const preserveSelection = options.preserveSelection ?? true;
  workspaceSnapshot = await api('/client/workspaces', { skipAuth: true });

  if (!preserveSelection || !selectedWorkspaceId || !workspaceSnapshot.workspaces.some(item => item.id === selectedWorkspaceId)) {
    selectedWorkspaceId = workspaceSnapshot.currentWorkspaceId;
  }

  syncAuthScope(workspaceSnapshot.currentWorkspace);
  renderWorkspaceSummary(workspaceSnapshot.currentWorkspace);
  renderWorkspaceQuickSelect();
  renderWorkspaceList();
  syncManualServerAddress(workspaceSnapshot.currentWorkspace);
  populateWorkspaceForm(getSelectedWorkspace() || createWorkspaceDraft(workspaceSnapshot.defaults));
  renderAccessProfile(null, workspaceSnapshot.currentWorkspace || null);
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
  syncManualServerAddress(workspaceSnapshot.currentWorkspace);
  renderWorkspaceList();
  populateWorkspaceForm(getSelectedWorkspace());

  if (didCurrentWorkspaceIdentityChange(previousCurrent, workspaceSnapshot.currentWorkspace)) {
    handleWorkspaceAuthChanged(workspaceSnapshot.currentWorkspace, '当前工作区已更新。');
  }

  setStatus(`工作区已保存：${result.workspace?.name || '未命名工作区'}`);
  await loadStatus();
}

async function setCurrentWorkspace() {
  const workspaceId = $('workspaceId')?.value?.trim() || selectedWorkspaceId || '';
  await setCurrentWorkspaceById(workspaceId);
}

async function connectManualServer() {
  if (PROJECT_LOCKED_MODE) {
    setStatus(PROJECT_LOCKED_MESSAGE);
    return;
  }

  const input = $('manualServerAddress');
  const rawAddress = input?.value?.trim() || '';

  if (!rawAddress) {
    setStatus('请先填写 Server 地址。');
    input?.focus();
    return;
  }

  const previousCurrent = workspaceSnapshot?.currentWorkspace || null;
  const result = await api('/client/workspaces/current-server', {
    method: 'PUT',
    skipAuth: true,
    body: { serverBaseUrl: rawAddress }
  });

  workspaceSnapshot = result.snapshot;
  selectedWorkspaceId = workspaceSnapshot.currentWorkspaceId;
  renderWorkspaceSummary(workspaceSnapshot.currentWorkspace);
  syncManualServerAddress(workspaceSnapshot.currentWorkspace);
  renderWorkspaceQuickSelect();
  renderWorkspaceList();
  populateWorkspaceForm(getSelectedWorkspace());

  if (didCurrentWorkspaceIdentityChange(previousCurrent, workspaceSnapshot.currentWorkspace)) {
    handleWorkspaceAuthChanged(workspaceSnapshot.currentWorkspace, '已切换到手动连接的服务器。');
  }

  setStatus(`已连接：${result.selected?.displayName || result.selected?.baseUrl || rawAddress}`);
  await loadStatus();
}

async function setCurrentWorkspaceById(workspaceId) {
  if (PROJECT_LOCKED_MODE) {
    setStatus(PROJECT_LOCKED_MESSAGE);
    return;
  }

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
  syncManualServerAddress(workspaceSnapshot.currentWorkspace);
  renderWorkspaceQuickSelect();
  renderWorkspaceList();
  populateWorkspaceForm(getSelectedWorkspace());
  handleWorkspaceAuthChanged(workspaceSnapshot.currentWorkspace, '当前工作区已切换。');
  setStatus(`当前工作区：${result.workspace?.name || workspaceId}`);
  await loadStatus();
}

async function deleteWorkspace() {
  if (PROJECT_LOCKED_MODE) {
    setStatus(PROJECT_LOCKED_MESSAGE);
    return;
  }

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
  syncManualServerAddress(workspaceSnapshot.currentWorkspace);
  renderWorkspaceList();
  populateWorkspaceForm(getSelectedWorkspace());

  if (wasCurrent) {
    handleWorkspaceAuthChanged(workspaceSnapshot.currentWorkspace, '当前工作区已删除，已切换到新的活动工作区。');
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
  await loadToolingStatusByWorkspaceRoot();
}

function buildToolingListPath(workspaceRoot) {
  if (!workspaceRoot) return '/client/tooling/list';
  return `/client/tooling/list?workspaceRoot=${encodeURIComponent(workspaceRoot)}`;
}

function formatToolingTargetName(target) {
  return String(target || '').toLowerCase() === 'cursor' ? 'Cursor' : 'Codex';
}

async function loadToolingStatusByWorkspaceRoot(workspaceRoot = '') {
  try {
    const tooling = await api(buildToolingListPath(workspaceRoot), { skipAuth: true });
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

async function pickToolingWorkspaceRoot(target) {
  const defaultWorkspaceRoot = getCurrentWorkspace()?.workspaceRoot
    || workspaceSnapshot?.defaults?.workspaceRoot
    || '';

  const result = await api('/client/tooling/select-folder', {
    method: 'POST',
    skipAuth: true,
    body: {
      defaultWorkspaceRoot,
      prompt: `选择要安装 ${formatToolingTargetName(target)} 工作流配置的项目目录`
    }
  });

  if (!result?.selected || !result?.workspaceRoot) {
    setStatus('已取消目录选择。');
    return null;
  }

  return result.workspaceRoot;
}

async function installTooling(target) {
  try {
    setStatus(`正在选择 ${formatToolingTargetName(target)} 安装目录...`);
    const workspaceRoot = await pickToolingWorkspaceRoot(target);
    if (!workspaceRoot) return;

    setStatus(`正在安装 ${formatToolingTargetName(target)} 工具链...`);
    const result = await api('/client/tooling/install', {
      method: 'POST',
      skipAuth: true,
      body: {
        target,
        replaceExisting: true,
        workspaceRoot
      }
    });

    const reports = result.reports || [];
    const written = reports.reduce((sum, report) => sum + (report.writtenFiles?.length || 0), 0);
    const skipped = reports.reduce((sum, report) => sum + (report.skippedFiles?.length || 0), 0);
    const warnings = reports.reduce((sum, report) => sum + (report.warnings?.length || 0), 0);
    setStatus(`工具安装完成：${formatToolingTargetName(target)} -> ${workspaceRoot}（写入 ${written}，跳过 ${skipped}，警告 ${warnings}）`);
    setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
    await loadToolingStatusByWorkspaceRoot(workspaceRoot);
  } catch (error) {
    setStatus(`工具安装失败：${error.message}`);
  }
}

function renderMcpToolParameters(parameters) {
  if (!Array.isArray(parameters) || parameters.length === 0) {
    return '<div class="mcp-tool-param">参数：无</div>';
  }

  return parameters.map(parameter => {
    const required = parameter.required ? '必填' : '可选';
    const description = parameter.description ? ` - ${escapeHtml(parameter.description)}` : '';
    const defaultValue = parameter.defaultValue != null ? `，默认=${escapeHtml(String(parameter.defaultValue))}` : '';
    return `<div class="mcp-tool-param"><code>${escapeHtml(parameter.name)}</code>: <code>${escapeHtml(parameter.type)}</code> (${required}${defaultValue})${description}</div>`;
  }).join('');
}

function renderMcpToolCatalog(data) {
  const summary = $('mcpToolSummary');
  const list = $('mcpToolList');
  if (!summary || !list) return;

  const tools = Array.isArray(data?.tools) ? data.tools : [];
  const endpoint = data?.mcpEndpoint || getMcpAddress();
  summary.textContent = `MCP 入口：${endpoint} · 共 ${tools.length} 个工具`;

  if (!tools.length) {
    list.innerHTML = '<div class="mcp-tool-item"><div class="mcp-tool-desc">暂无可展示工具。</div></div>';
    return;
  }

  list.innerHTML = tools.map(tool => `
    <div class="mcp-tool-item">
      <div class="mcp-tool-title">
        <code>${escapeHtml(tool.name || '-')}</code>
        <span class="mcp-tool-group">${escapeHtml(tool.group || 'General')}</span>
      </div>
      <div class="mcp-tool-desc">${escapeHtml(tool.description || '无描述')}</div>
      <div class="mcp-tool-params">${renderMcpToolParameters(tool.parameters)}</div>
    </div>
  `).join('');
}

async function loadMcpToolCatalog() {
  const summary = $('mcpToolSummary');
  const list = $('mcpToolList');
  if (!summary || !list) return;

  try {
    summary.textContent = '加载中...';
    const catalog = await api('/client/mcp/tools', { skipAuth: true });
    renderMcpToolCatalog(catalog);
  } catch (error) {
    summary.textContent = `加载失败：${error.message}`;
    list.innerHTML = '<div class="mcp-tool-item"><div class="mcp-tool-desc">无法读取 MCP 工具清单。</div></div>';
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

async function loadStatus() {
  try {
    const clientStatus = await api('/client/status', { skipAuth: true });
    const workspace = clientStatus.currentWorkspace || null;
    syncAuthScope(workspace);

    applyHealthState($('clientHealth'), clientStatus.client, '正常', '降级');
    $('clientPort').textContent = getClientAddressMeta();
    $('serverAddress').textContent = `地址: ${clientStatus.targetServer || '-'}`;
    renderWorkspaceSummary(workspace);
    syncManualServerAddress(workspace);
    await loadToolingStatus();

    const serverStatus = clientStatus.serverStatus || null;
    const access = clientStatus.access || null;
    renderAccessProfile(access, workspace);
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
      $('serverAddress').textContent = `地址: ${clientStatus.targetServer || '-'}`;
      $('overviewSummary').textContent =
        `${workspace?.name || '当前工作区'} 已配置完成，但目标 Server 当前不可用。`;
      resetProtectedOverview('等待服务器可用');
      setStatus('Client 已启动，但 Server 当前离线。');
      setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
      return;
    }

    if (access && access.allowed === false) {
      resetProtectedOverview('未在白名单');
      $('overviewSummary').textContent =
        `${workspace?.name || '当前工作区'} 可发现目标服务器，但当前客户端 IP 不在白名单中。`;
      setStatus(access.reason || '当前客户端 IP 不在白名单中。');
      setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
      return;
    }

    setStatus('客户端状态已刷新');
    setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
  } catch (error) {
    renderAccessProfile(null, getCurrentWorkspace());
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
    setStatus('正在加载记忆工作区...');
    await loadMemories();
    if ($('memoryEditorForm').style.display !== 'none') $('memoryEmptyState')?.classList.add('hidden');
    else $('memoryEmptyState')?.classList.remove('hidden');
    setStatus('记忆工作区已刷新');
  } catch (error) {
    setStatus(`记忆工作区加载失败：${error.message}`);
  }
}

async function refreshTab(tabId) {
  if (tabId === 'overview') {
    await loadStatus();
    await loadMcpToolCatalog();
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
  if (activeTabId !== 'overview') {
    await refreshTab(activeTabId);
  }
}

function resolveInitialTab() {
  const hash = window.location.hash.replace(/^#/, '').trim();
  if (!hash) return 'overview';
  return ['overview', 'topology', 'memory'].includes(hash) ? hash : 'overview';
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
    case 'copy-mcp-address':
      await copyMcpAddress();
      break;
    case 'refresh-mcp-tools':
      await loadMcpToolCatalog();
      break;
    case 'switch-tab':
      if (element?.dataset.tabTarget) await switchTab(element.dataset.tabTarget);
      break;
    case 'connect-manual-server':
      await connectManualServer();
      break;
    case 'refresh-workspaces':
      if (PROJECT_LOCKED_MODE) {
        setStatus(PROJECT_LOCKED_MESSAGE);
        break;
      }
      await loadWorkspaces();
      break;
    case 'new-workspace':
      if (PROJECT_LOCKED_MODE) {
        setStatus(PROJECT_LOCKED_MESSAGE);
        break;
      }
      newWorkspace();
      break;
    case 'select-workspace':
      if (element?.dataset.workspaceId) selectWorkspace(element.dataset.workspaceId);
      break;
    case 'save-workspace':
      if (PROJECT_LOCKED_MODE) {
        setStatus(PROJECT_LOCKED_MESSAGE);
        break;
      }
      await saveWorkspace();
      break;
    case 'set-current-workspace':
      if (PROJECT_LOCKED_MODE) {
        setStatus(PROJECT_LOCKED_MESSAGE);
        break;
      }
      await setCurrentWorkspace();
      break;
    case 'delete-workspace':
      await deleteWorkspace();
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
      break;
  }
}

async function handleChangeAction(action) {
  switch (action) {
    case 'quick-switch-workspace':
    {
      if (PROJECT_LOCKED_MODE) {
        setStatus(PROJECT_LOCKED_MESSAGE);
        break;
      }
      const workspaceId = $('availableWorkspaceSelect')?.value || $('workspaceQuickSelect')?.value || '';
      if (workspaceId) await setCurrentWorkspaceById(workspaceId);
      break;
    }
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
  applyProjectLockedModeUI();
  initChatResize();
  await loadWorkspaces({ preserveSelection: false });
  setAuthMessage(PROJECT_LOCKED_MODE ? PROJECT_LOCKED_MESSAGE : DEFAULT_CONNECTION_MESSAGE, false);
  await loadStatus();
  await switchTab(resolveInitialTab());
});
