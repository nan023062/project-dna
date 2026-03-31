/**
 * App entry for the Server admin UI.
 * Keeps the shell lifecycle, fullscreen tabs, and refresh flow in one place.
 */

import { $, api, getAuthToken } from './utils.js';
import {
  registerEditSidebarOpener,
  registerFileTreeActions,
  registerModuleAdminActions,
  registerRefreshHandler
} from './app-runtime.js';
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
import {
  loadReviewQueue,
} from './panels/review-admin.js';
import {
  loadModuleManagement,
  saveModule,
  deleteModule,
  saveCrosswork,
  deleteCrosswork,
  newModule,
  newCrosswork,
  onDisciplineChanged,
  addCrossworkParticipant,
  newDiscipline,
  saveDiscipline,
  deleteDiscipline,
  addLayerRow
} from './panels/arch-config.js';
import { loadFileTree, refreshFileTree } from './panels/file-tree.js';
import {
  initEditSidebar,
  openEditSidebar,
} from './dialogs/module-editor.js';
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
  openModelDropdown,
} from './chat/chat-panel.js';
import { openLlmSettings, closeLlmSettings } from './chat/llm-settings.js';

registerModuleAdminActions({
  loadModuleManagement,
  saveModule,
  deleteModule,
  saveCrosswork,
  deleteCrosswork,
  newModule,
  newCrosswork,
  onDisciplineChanged,
  addCrossworkParticipant,
  newDiscipline,
  saveDiscipline,
  deleteDiscipline,
  addLayerRow
});
registerFileTreeActions({ loadFileTree, refreshFileTree });
registerEditSidebarOpener(openEditSidebar);

let refreshTimer = null;
let topoData = null;
let stackData = null;
let isRefreshing = false;
let visibilityHandlerBound = false;
let authRefreshPending = false;

function initUI() {
  const mainApp = $('mainApp');
  if (!mainApp) return;

  ui.init(mainApp);

  registerFullscreenTabs(ui, [
    { id: 'topology', tabButtonSelector: '.tab[data-tab="topology"]', onActivate: () => refreshTopologyOnly() },
    {
      id: 'fileTree',
      tabButtonSelector: '.tab[data-tab="fileTree"]',
      onActivate: () => initEditSidebar().then(() => loadFileTree({ preserveState: true, showLoading: !_rootsHasRendered() }))
    },
    { id: 'memoryMgmt', tabButtonSelector: '.tab[data-tab="memoryMgmt"]', onActivate: () => { loadGovernanceStats(); loadMemories(); } },
    { id: 'reviewQueue', tabButtonSelector: '.tab[data-tab="reviewQueue"]', onActivate: () => loadReviewQueue() }
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
    return 'Sign in as admin in Review Queue to unlock this panel.';
  }

  if (error?.status === 403) {
    return 'The current account does not have permission to load this panel.';
  }

  return error?.message || 'Unable to load the requested panel.';
}

function scheduleRefreshAfterAuthChange() {
  if (authRefreshPending) return;
  authRefreshPending = true;

  setTimeout(() => {
    authRefreshPending = false;
    refresh(true).catch(err => {
      $('statusText').textContent = `Refresh failed: ${err?.message || String(err)}`;
    });
  }, 0);
}

function clearReviewFilters() {
  const status = $('reviewFilterStatus');
  const submitter = $('reviewFilterSubmitter');
  if (status) status.value = '';
  if (submitter) submitter.value = '';
  return loadReviewQueue();
}

async function handleStaticAction(action, element, event) {
  switch (action) {
    case 'refresh':
      await refresh(true);
      break;
    case 'refresh-file-tree':
      await refreshFileTree();
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
    case 'load-review-queue':
      await loadReviewQueue();
      break;
    case 'clear-review-filters':
      await clearReviewFilters();
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
    case 'load-review-queue':
      await loadReviewQueue();
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
      handler: ({ event, element }) => void handleStaticAction(element.dataset.action, element, event)
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
        if (element.dataset.keydownAction === 'load-review-queue-on-enter' && event.key === 'Enter') {
          event.preventDefault();
          void loadReviewQueue();
          return;
        }

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

export function enterApp(projectRoot) {
  $('mainApp').classList.add('active');
  $('appWrapper').classList.add('active');
  $('projectPath').textContent = projectRoot;
  $('projectPath').title = `Current project: ${projectRoot}`;

  initUI();
  ui.switchTab('topology');

  refresh();
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

  $('refreshInfo').textContent = 'Auto refresh on';

  if (!visibilityHandlerBound) {
    visibilityHandlerBound = true;
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        clearInterval(refreshTimer);
        refreshTimer = null;
        $('refreshInfo').textContent = 'Paused';
      } else {
        refresh(true);
        refreshTimer = setInterval(() => {
          if (document.hidden) return;
          refresh(false);
        }, 8000);
        $('refreshInfo').textContent = 'Auto refresh on';
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
  if (!getAuthToken()) {
    resetTopologySummary();
    showTopologyMessage('Access required', 'Sign in as admin in Review Queue to unlock this panel.');
    $('statusText').textContent = 'Sign in as admin in Review Queue to unlock this panel.';
    return false;
  }

  $('statusText').textContent = 'Loading topology...';

  try {
    topoData = await api('/topology');
    stackData = {};

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
    if (sidebar && sidebar.querySelector('.sidebar-title')?.textContent === 'Access required') {
      sidebar.style.display = 'none';
      sidebar.innerHTML = '';
    }

    $('statusText').textContent = 'Topology refreshed';
    return true;
  } catch (error) {
    resetTopologySummary();
    showTopologyMessage(
      error?.status === 401 || error?.status === 403 ? 'Access required' : 'Topology unavailable',
      getProtectedPanelMessage(error)
    );
    $('statusText').textContent = getProtectedPanelMessage(error);
    return false;
  }
}

async function refresh(activeForce = true) {
  if (isRefreshing) return;

  if (!activeForce && shouldSkipAutoRefresh()) {
    $('refreshInfo').textContent = 'Auto refresh paused while editing';
    return;
  }

  isRefreshing = true;
  try {
    const activeTab = ui.activeTabId || 'topology';

    if (activeTab === 'fileTree') {
      await refreshFileTree();
      $('statusText').textContent = `File tree refreshed at ${new Date().toLocaleTimeString()}`;
      $('refreshInfo').textContent = 'Auto refresh on';
      return;
    }

    if (activeTab === 'memoryMgmt') {
      await Promise.allSettled([loadGovernanceStats(), loadMemories()]);
      $('statusText').textContent = `Knowledge panel refreshed at ${new Date().toLocaleTimeString()}`;
      $('refreshInfo').textContent = 'Auto refresh on';
      return;
    }

    if (activeTab === 'reviewQueue') {
      await loadReviewQueue();
      $('statusText').textContent = `Review queue refreshed at ${new Date().toLocaleTimeString()}`;
      $('refreshInfo').textContent = 'Auto refresh on';
      return;
    }

    const ok = await refreshTopologyOnly();
    if (ok) {
      $('statusText').textContent = `Refreshed at ${new Date().toLocaleTimeString()}`;
    }
    $('refreshInfo').textContent = 'Auto refresh on';
  } finally {
    isRefreshing = false;
  }
}

registerRefreshHandler(refresh);

function _rootsHasRendered() {
  const container = $('fileTreeContent');
  return !!container && container.children.length > 0;
}

initSetup();
initChatResize();
bindStaticUiEvents();
window.addEventListener('dna-admin-auth-changed', scheduleRefreshAfterAuthChange);
