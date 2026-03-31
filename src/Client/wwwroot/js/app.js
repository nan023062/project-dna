import {
  $,
  api,
  getAuthToken,
  getAuthUser,
  setAuthToken,
  setAuthUser,
  clearAuthState
} from './utils.js';
import { renderTopology } from '/dna-shared/js/panels/topology.js';
import {
  loadMemories,
  loadSubmissions,
  selectMemory,
  createNew,
  saveMemory,
  deleteMemory,
  applyTemplate,
  onLayerTypeChanged,
  addFeature,
  addNodeId,
  addTag
} from './panels/memory-editor.js';

function hideMemoryEmptyState() {
  $('memoryEmptyState')?.classList.add('hidden');
}

function showMemoryEmptyState() {
  $('memoryEmptyState')?.classList.remove('hidden');
}

window.MemoryEditor = {
  loadMemories,
  loadSubmissions,
  selectMemory: id => {
    hideMemoryEmptyState();
    return selectMemory(id);
  },
  createNew: () => {
    hideMemoryEmptyState();
    return createNew();
  },
  saveMemory,
  deleteMemory,
  applyTemplate,
  onLayerTypeChanged,
  addFeature,
  addNodeId,
  addTag
};

let activeTab = 'topology';
let currentUser = null;

function applyToolState(elementId, installed) {
  const element = $(elementId);
  if (!element) return;
  element.classList.remove('healthy', 'degraded', 'error');
  if (installed) {
    element.classList.add('healthy');
    element.textContent = 'Installed';
    return;
  }

  element.classList.add('degraded');
  element.textContent = 'Not installed';
}

async function loadToolingStatus() {
  try {
    const tooling = await api('/client/tooling/list', { skipAuth: true });
    $('toolingWorkspace').textContent = `Workspace: ${tooling.workspaceRoot || '-'}`;

    const cursor = (tooling.targets || []).find(target => target.id === 'cursor');
    const codex = (tooling.targets || []).find(target => target.id === 'codex');
    applyToolState('cursorToolState', Boolean(cursor?.installed));
    applyToolState('codexToolState', Boolean(codex?.installed));
  } catch (error) {
    $('toolingWorkspace').textContent = `Workspace: unavailable (${error.message})`;
    const cursor = $('cursorToolState');
    const codex = $('codexToolState');
    if (cursor) {
      cursor.classList.remove('healthy', 'degraded');
      cursor.classList.add('error');
      cursor.textContent = 'Error';
    }
    if (codex) {
      codex.classList.remove('healthy', 'degraded');
      codex.classList.add('error');
      codex.textContent = 'Error';
    }
  }
}

async function installTooling(target) {
  try {
    setStatus(`Installing ${target} tooling...`);
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
    setStatus(`Tooling installed: written ${written}, skipped ${skipped}, warnings ${warnings}`);
    setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
    await loadToolingStatus();
  } catch (error) {
    setStatus(`Tooling install failed: ${error.message}`);
  }
}

function setStatus(text) {
  const statusText = $('statusText');
  if (statusText) statusText.textContent = text;
}

function setRefreshInfo(text) {
  const refreshInfo = $('refreshInfo');
  if (refreshInfo) refreshInfo.textContent = text;
}

function formatCompact(value) {
  if (!Number.isFinite(value)) return '-';
  try {
    return new Intl.NumberFormat('zh-CN', {
      notation: 'compact',
      maximumFractionDigits: 1
    }).format(value);
  } catch {
    return String(value);
  }
}

function applyHealthState(element, value, okText = 'OK', degradedText = 'Degraded') {
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
    if (authStateText) authStateText.textContent = 'Not signed in';
    if (authUserInfo) authUserInfo.textContent = '-';
    return;
  }

  if (authStateText) authStateText.textContent = `Signed in as ${user.role}`;
  if (authUserInfo) authUserInfo.textContent = `${user.username} (${user.role})`;
}

function resetProtectedOverview(message = 'Sign in required') {
  $('memoryTotal').textContent = '-';
  $('memoryFreshness').textContent = message;
  $('statModules').textContent = '-';
  $('statEdges').textContent = '-';
  $('statContainment').textContent = '-';
  $('statCollaboration').textContent = '-';
}

async function ensureAuthenticatedUser(options = {}) {
  if (!getAuthToken()) {
    currentUser = null;
    renderAuthState();
    return null;
  }

  try {
    const result = await api('/auth/me');
    currentUser = result.user || result;
    setAuthUser(currentUser);
    renderAuthState();
    return currentUser;
  } catch (error) {
    if (error.status === 401 || error.status === 403) {
      currentUser = null;
      clearAuthState();
      renderAuthState();
      setAuthMessage('Session expired. Please sign in again.', true);
      if (!options.silent) {
        setStatus('Session expired. Please sign in again.');
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
    setAuthMessage('Enter both username and password.', true);
    return;
  }

  try {
    setAuthMessage('Signing in...', false);
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
    setAuthMessage(`Signed in as ${currentUser?.username || username}`, false);
    await refreshAll();
  } catch (error) {
    setAuthMessage(error.message, true);
    setStatus(`Sign-in failed: ${error.message}`);
  }
}

async function logout() {
  currentUser = null;
  clearAuthState();
  renderAuthState();
  setAuthMessage('Signed out. Protected data is now hidden.', false);
  resetProtectedOverview();
  showMemoryEmptyState();
  setStatus('Signed out');
  await loadStatus();
}

async function loadStatus() {
  try {
    const clientStatus = await api('/client/status', { skipAuth: true });
    applyHealthState($('clientHealth'), clientStatus.client, 'OK', 'Degraded');
    $('clientPort').textContent = 'Local port 5052';
    $('targetServer').textContent = clientStatus.targetServer || '-';
    $('mcpAddress').textContent = `${window.location.origin}/mcp`;
    $('serverDashboardLink').href = clientStatus.targetServer || 'http://127.0.0.1:5051';
    await loadToolingStatus();

    const serverStatus = clientStatus.serverStatus || null;
    if (serverStatus && !clientStatus.error) {
      $('serverHealth').classList.remove('error');
      $('serverHealth').classList.add('healthy');
      $('serverHealth').textContent = 'Online';
      $('serverUptime').textContent = `Uptime: ${serverStatus.uptime || '-'}`;
      $('overviewSummary').textContent =
        `Client is connected to ${clientStatus.targetServer}. Server started at ${serverStatus.startedAt || 'unknown'}.`;
    } else {
      $('serverHealth').classList.remove('healthy');
      $('serverHealth').classList.add('error');
      $('serverHealth').textContent = 'Offline';
      $('serverUptime').textContent = clientStatus.error || 'Unable to reach server';
      $('overviewSummary').textContent =
        'Client is running, but the shared server is currently unavailable.';
      resetProtectedOverview('Waiting for server');
      setStatus('Client is running, but server is offline.');
      setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
      return;
    }

    const user = await ensureAuthenticatedUser({ silent: true });
    if (!user) {
      resetProtectedOverview('Sign in required');
      $('overviewSummary').textContent =
        'Client is connected to the server, but no server identity is active yet.';
      setStatus('Sign in to read formal knowledge and submit reviews.');
      setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
      return;
    }

    const memoryStats = await api('/memory/stats');
    $('memoryTotal').textContent = formatCompact(memoryStats.total ?? 0);
    const freshness = memoryStats.byFreshness || {};
    $('memoryFreshness').textContent =
      `Fresh ${freshness.Fresh ?? freshness.fresh ?? 0} / Aging ${freshness.Aging ?? freshness.aging ?? 0}`;

    setStatus('Client status refreshed');
    setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
  } catch (error) {
    $('clientHealth').classList.add('error');
    $('clientHealth').textContent = 'Error';
    $('serverHealth').classList.add('error');
    $('serverHealth').textContent = 'Unknown';
    $('overviewSummary').textContent = error.message;
    setStatus(`Status refresh failed: ${error.message}`);
  }
}

async function loadTopology() {
  try {
    const user = await ensureAuthenticatedUser({ silent: true });
    if (!user) {
      resetProtectedOverview('Sign in required');
      const sidebar = $('archSidebar');
      if (sidebar) {
        sidebar.style.display = 'flex';
        sidebar.innerHTML = `
          <div class="sidebar-section">
            <div class="sidebar-title">Sign in required</div>
            <div class="sidebar-text">Use a server account to load the formal topology.</div>
          </div>
        `;
      }
      setStatus('Sign in before loading topology.');
      return;
    }

    setStatus('Loading topology...');
    const topoData = await api('/topology');
    renderTopology(topoData);

    const modulesCount = topoData.modules ? topoData.modules.length : 0;
    const edgesCount = topoData.edges ? topoData.edges.length : 0;
    const containmentCount = topoData.containmentEdges ? topoData.containmentEdges.length : 0;
    const collaborationCount = topoData.collaborationEdges ? topoData.collaborationEdges.length : 0;

    $('statModules').textContent = formatCompact(modulesCount);
    $('statEdges').textContent = formatCompact(edgesCount);
    $('statContainment').textContent = formatCompact(containmentCount);
    $('statCollaboration').textContent = formatCompact(collaborationCount);
    setStatus('Topology refreshed');
  } catch (error) {
    setStatus(`Topology load failed: ${error.message}`);
    const sidebar = $('archSidebar');
    if (sidebar) {
      sidebar.style.display = 'flex';
      sidebar.innerHTML = `
        <div class="sidebar-section">
          <div class="sidebar-title">Topology load failed</div>
          <div class="sidebar-text">${error.message}</div>
        </div>
      `;
    }
  }
}

async function loadKnowledge() {
  try {
    const user = await ensureAuthenticatedUser({ silent: true });
    if (!user) {
      showMemoryEmptyState();
      setStatus('Sign in before using the knowledge workspace.');
      return;
    }

    setStatus('Loading knowledge workspace...');
    await Promise.all([loadMemories(), loadSubmissions()]);
    if ($('memoryEditorForm').style.display !== 'none') hideMemoryEmptyState();
    else showMemoryEmptyState();
    setStatus('Knowledge workspace refreshed');
  } catch (error) {
    setStatus(`Knowledge workspace failed: ${error.message}`);
  }
}

function setActiveTab(tabId) {
  activeTab = tabId;
  document.querySelectorAll('.workspace-tab').forEach(tab => {
    tab.classList.toggle('active', tab.dataset.tab === tabId);
  });
  $('panelTopology').classList.toggle('active', tabId === 'topology');
  $('panelMemory').classList.toggle('active', tabId === 'memory');
}

async function refreshActiveTab() {
  if (activeTab === 'memory') {
    await loadKnowledge();
    return;
  }

  await loadTopology();
}

async function refreshAll() {
  await loadStatus();
  await refreshActiveTab();
}

async function switchTab(tabId) {
  setActiveTab(tabId);
  await refreshActiveTab();
}

window.switchTab = switchTab;
window.refreshAll = refreshAll;
window.refreshTopology = loadTopology;
window.refreshMemories = loadKnowledge;
window.refreshToolingStatus = loadToolingStatus;
window.installTooling = installTooling;
window.login = login;
window.logout = logout;

document.addEventListener('DOMContentLoaded', async () => {
  currentUser = getAuthUser();
  renderAuthState();
  $('authPassword')?.addEventListener('keydown', event => {
    if (event.key === 'Enter') {
      event.preventDefault();
      login();
    }
  });
  setActiveTab('topology');
  await refreshAll();
});
