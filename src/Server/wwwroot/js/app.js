/**
 * App entry for the Server admin UI.
 * Keeps the shell lifecycle, tab routing, and refresh flow in one place.
 */

import { $, api } from './utils.js';
import { registerRefreshHandler } from './app-runtime.js';
import {
  applyMetricValues,
  formatCompactNumber,
  registerFullscreenTabs,
  renderSidebarMessage,
  resetMetricValues
} from '/dna-shared/js/core/host-shell.js';
import { bindDelegatedDocumentEvents } from '/dna-shared/js/core/dom-actions.js';
import { ui } from '/dna-shared/js/ui/ui-manager.js';
import { initSetup } from './setup.js';
import { renderTopology } from '/dna-shared/js/panels/topology.js';
import { loadServiceOverview } from './panels/service-overview.js';
import {
  loadGovernanceStats,
  checkFreshness,
  detectConflicts,
  archiveStale,
  condenseNodeKnowledge,
  condenseAllKnowledge,
  configureCondenseSchedule
} from './panels/governance.js';
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
  loadWhitelist,
  selectWhitelistEntry,
  newWhitelistEntry,
  saveWhitelistEntry,
  deleteWhitelistEntry
} from './panels/connection-admin.js';
import { initDetail } from './panels/detail.js';
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

let refreshTimer = null;
let isRefreshing = false;
let visibilityHandlerBound = false;

function initUI() {
  const mainApp = $('mainApp');
  if (!mainApp) return;

  ui.init(mainApp);

  registerFullscreenTabs(ui, [
    { id: 'overview', tabButtonSelector: '.tab[data-tab="overview"]', onActivate: () => loadServiceOverview() },
    { id: 'topology', tabButtonSelector: '.tab[data-tab="topology"]', onActivate: () => refreshTopologyOnly() },
    { id: 'memoryMgmt', tabButtonSelector: '.tab[data-tab="memoryMgmt"]', onActivate: () => Promise.allSettled([loadGovernanceStats(), loadMemories()]) },
    { id: 'users', tabButtonSelector: '.tab[data-tab="users"]', onActivate: () => loadWhitelist() }
  ], $);
}

function resetTopologySummary() {
  resetMetricValues($, ['statModules', 'statEdges', 'statContainment', 'statCollaboration']);
}

function showTopologyMessage(title, message) {
  renderSidebarMessage($, 'archSidebar', title, message);
}

function getProtectedPanelMessage(error) {
  if (error?.status === 401) {
    return '请先登录管理员账号后再访问这个面板。';
  }

  if (error?.status === 403) {
    return '当前账号没有权限加载这个面板。';
  }

  return error?.message || '无法加载请求的面板。';
}

async function handleStaticAction(action, element) {
  switch (action) {
    case 'refresh':
      await refresh(true);
      break;
    case 'check-freshness':
      await checkFreshness();
      break;
    case 'detect-conflicts':
      await detectConflicts();
      break;
    case 'archive-stale':
      await archiveStale();
      break;
    case 'condense-node-knowledge':
      await condenseNodeKnowledge();
      break;
    case 'condense-all-knowledge':
      await condenseAllKnowledge();
      break;
    case 'configure-condense-schedule':
      await configureCondenseSchedule();
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
    case 'load-whitelist':
      await loadWhitelist();
      break;
    case 'new-whitelist-entry':
      newWhitelistEntry();
      break;
    case 'save-whitelist-entry':
      await saveWhitelistEntry();
      break;
    case 'delete-whitelist-entry':
      await deleteWhitelistEntry();
      break;
    case 'show-session-list':
      showSessionList();
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
      openLlmSettings();
      break;
    case 'close-llm-settings':
      closeLlmSettings();
      break;
    default:
      if (action === 'select-whitelist-entry' && element?.dataset.entryId) {
        selectWhitelistEntry(element.dataset.entryId);
        break;
      }
      if (action === 'switch-tab' && element?.dataset.tab) {
        switchTab(element.dataset.tab);
      }
      break;
  }
}

async function handleStaticChangeAction(action) {
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

function bindStaticUiEvents() {
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
      handler: ({ element }) => void handleStaticAction(element.dataset.action, element)
    },
    {
      eventName: 'change',
      selector: '[data-change-action]',
      handler: ({ element }) => void handleStaticChangeAction(element.dataset.changeAction)
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

initDetail(switchTab);

export function enterApp(projectTitle) {
  $('mainApp').classList.add('active');
  $('appWrapper').classList.add('active');
  $('projectPath').textContent = projectTitle;
  $('projectPath').title = `当前入口：${projectTitle}`;

  initUI();
  ui.switchTab('overview');

  refresh(true);
  startAutoRefresh();
}

function switchTab(tab) {
  ui.switchTab(tab);
}

function startAutoRefresh() {
  if (refreshTimer) clearInterval(refreshTimer);
  refreshTimer = setInterval(() => {
    if (document.hidden) return;
    refresh(false);
  }, 8000);

  $('refreshInfo').textContent = '自动刷新已开启';

  if (!visibilityHandlerBound) {
    visibilityHandlerBound = true;
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        clearInterval(refreshTimer);
        refreshTimer = null;
        $('refreshInfo').textContent = '已暂停';
      } else {
        refresh(true);
        refreshTimer = setInterval(() => {
          if (document.hidden) return;
          refresh(false);
        }, 8000);
        $('refreshInfo').textContent = '自动刷新已开启';
      }
    });
  }
}

function shouldSkipAutoRefresh() {
  if (document.querySelector('#llmSettingsOverlay.open')) return true;
  if (document.querySelector('.ui-dialog-overlay')?.style.display !== 'none') return true;

  const active = document.activeElement;
  if (!active) return false;

  const tag = (active.tagName || '').toUpperCase();
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
}

async function refreshTopologyOnly() {
  $('statusText').textContent = '正在加载拓扑...';

  try {
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

    const sidebar = $('archSidebar');
    if (sidebar && sidebar.querySelector('.sidebar-title')?.textContent === '需要登录') {
      sidebar.style.display = 'none';
      sidebar.innerHTML = '';
    }

    $('statusText').textContent = '拓扑已刷新';
    return true;
  } catch (error) {
    resetTopologySummary();
    showTopologyMessage(
      error?.status === 401 || error?.status === 403 ? '需要登录' : '拓扑不可用',
      getProtectedPanelMessage(error)
    );
    $('statusText').textContent = getProtectedPanelMessage(error);
    return false;
  }
}

async function refresh(activeForce = true) {
  if (isRefreshing) return;

  if (!activeForce && shouldSkipAutoRefresh()) {
    $('refreshInfo').textContent = '编辑中，自动刷新已暂停';
    return;
  }

  isRefreshing = true;
  try {
    const activeTab = ui.activeTabId || 'overview';

    if (activeTab === 'overview') {
      await loadServiceOverview();
      $('statusText').textContent = `服务概览已刷新：${new Date().toLocaleTimeString()}`;
      $('refreshInfo').textContent = '自动刷新已开启';
      return;
    }

    if (activeTab === 'memoryMgmt') {
      await Promise.allSettled([loadGovernanceStats(), loadMemories()]);
      $('statusText').textContent = `知识面板已刷新：${new Date().toLocaleTimeString()}`;
      $('refreshInfo').textContent = '自动刷新已开启';
      return;
    }

    if (activeTab === 'users') {
      await loadWhitelist();
      $('statusText').textContent = `连接权限已刷新：${new Date().toLocaleTimeString()}`;
      $('refreshInfo').textContent = '自动刷新已开启';
      return;
    }

    const ok = await refreshTopologyOnly();
    if (ok) {
      $('statusText').textContent = `架构视图已刷新：${new Date().toLocaleTimeString()}`;
    }
    $('refreshInfo').textContent = '自动刷新已开启';
  } finally {
    isRefreshing = false;
  }
}

registerRefreshHandler(refresh);

initSetup();
initChatResize();
bindStaticUiEvents();
