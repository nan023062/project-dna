import { $ } from './utils.js';
import { renderTopology } from './panels/topology.js';
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

async function readJson(url) {
  const response = await fetch(url);
  const text = await response.text();
  const payload = text ? JSON.parse(text) : {};
  if (!response.ok) {
    throw new Error(payload?.error || `${response.status} ${response.statusText}`);
  }
  return payload;
}

async function loadStatus() {
  try {
    const clientStatus = await readJson('/api/client/status');
    applyHealthState($('clientHealth'), clientStatus.client, '正常', '降级');
    $('clientPort').textContent = '本地端口 5052';
    $('targetServer').textContent = clientStatus.targetServer || '-';
    $('mcpAddress').textContent = `${window.location.origin}/mcp`;
    $('serverDashboardLink').href = clientStatus.targetServer || 'http://127.0.0.1:5051';

    const serverStatus = clientStatus.serverStatus || null;
    if (serverStatus && !clientStatus.error) {
      $('serverHealth').classList.remove('error');
      $('serverHealth').classList.add('healthy');
      $('serverHealth').textContent = '在线';
      $('serverUptime').textContent = `Uptime: ${serverStatus.uptime || '-'}`;
      $('overviewSummary').textContent =
        `Client 已连接到 ${clientStatus.targetServer}。Server 启动时间：${serverStatus.startedAt || '未知'}。`;
    } else {
      $('serverHealth').classList.remove('healthy');
      $('serverHealth').classList.add('error');
      $('serverHealth').textContent = '离线';
      $('serverUptime').textContent = clientStatus.error || '未能连接到 Server';
      $('overviewSummary').textContent =
        'Client 已启动，但当前还没有连上 Server。图谱和正式知识读取会受影响，提审也无法进入后端审核流。';
    }

    const memoryStats = await readJson('/api/memory/stats');
    $('memoryTotal').textContent = formatCompact(memoryStats.total ?? 0);
    const freshness = memoryStats.byFreshness || {};
    $('memoryFreshness').textContent =
      `Fresh ${freshness.Fresh ?? freshness.fresh ?? 0} / Aging ${freshness.Aging ?? freshness.aging ?? 0}`;

    setStatus('Client 状态已刷新');
    setRefreshInfo(new Date().toLocaleTimeString('zh-CN'));
  } catch (error) {
    $('clientHealth').classList.add('error');
    $('clientHealth').textContent = '异常';
    $('serverHealth').classList.add('error');
    $('serverHealth').textContent = '未知';
    $('overviewSummary').textContent = error.message;
    setStatus(`状态刷新失败: ${error.message}`);
  }
}

async function loadTopology() {
  try {
    setStatus('正在加载图谱...');
    const topoData = await readJson('/api/topology');
    renderTopology(topoData);

    const modulesCount = topoData.modules ? topoData.modules.length : 0;
    const edgesCount = topoData.edges ? topoData.edges.length : 0;
    const containmentCount = topoData.containmentEdges ? topoData.containmentEdges.length : 0;
    const collaborationCount = topoData.collaborationEdges ? topoData.collaborationEdges.length : 0;

    $('statModules').textContent = formatCompact(modulesCount);
    $('statEdges').textContent = formatCompact(edgesCount);
    $('statContainment').textContent = formatCompact(containmentCount);
    $('statCollaboration').textContent = formatCompact(collaborationCount);
    setStatus('图谱已刷新');
  } catch (error) {
    setStatus(`图谱加载失败: ${error.message}`);
    const sidebar = $('archSidebar');
    if (sidebar) {
      sidebar.style.display = 'flex';
      sidebar.innerHTML = `
        <div class="sidebar-section">
          <div class="sidebar-title">图谱加载失败</div>
          <div class="sidebar-text">${error.message}</div>
        </div>
      `;
    }
  }
}

async function loadKnowledge() {
  try {
    setStatus('正在加载知识工作台...');
    await Promise.all([loadMemories(), loadSubmissions()]);
    if ($('memoryEditorForm').style.display !== 'none') hideMemoryEmptyState();
    else showMemoryEmptyState();
    setStatus('知识工作台已刷新');
  } catch (error) {
    setStatus(`知识工作台加载失败: ${error.message}`);
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

document.addEventListener('DOMContentLoaded', async () => {
  setActiveTab('topology');
  await refreshAll();
});
