/**
 * App entry for the Server admin UI.
 * Keeps fullscreen tabs, auto refresh, and window bridges in one place.
 */

import { $, api, getAuthToken } from './utils.js';
import { ui } from './ui/ui-manager.js';
import { initSetup, showSetup, setProject, openProjectBrowser } from './setup.js';
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
  selectSubmission as selectReviewSubmission,
  reloadSelection as reloadReviewSelection,
  login as loginReviewAdmin,
  logout as logoutReviewAdmin,
  approve as approveReviewSubmission,
  reject as rejectReviewSubmission,
  publish as publishReviewSubmission
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
  closeEditSidebar,
  onEditDisciplineChanged,
  saveFromSidebar,
  deleteFromSidebar,
  onDepSearchInput,
  onDepSearchKeydown
} from './dialogs/module-editor.js';
import { initDetail } from './panels/detail.js';
import { openArchConfigDialog } from './dialogs/arch-config-dialog.js';
import {
  toggleChat,
  newChat,
  sendChatMessage,
  handleChatKeydown,
  autoResizeInput,
  initChatResize,
  switchChatMode,
  loadSession,
  showSessionList,
  stopChat,
  openModelDropdown,
  selectProvider,
  continueChatFromLimit,
  editQueueItem,
  removeQueueItem,
  keepEdit,
  undoEdit,
  beginTaskFromKnowledgeCard,
  askClarifyingFromKnowledgeCard,
  queueDependencyValidationFromKnowledgeCard,
  runGovernanceCheckFromKnowledgeCard,
  runSuggestedActionFromKnowledgeCard
} from './chat/chat-panel.js';
import { openLlmSettings, closeLlmSettings } from './chat/llm-settings.js';

window.Governance = {
  checkFreshness,
  detectConflicts,
  archiveStale,
  condenseNodeKnowledge,
  condenseAllKnowledge,
  configureCondenseSchedule
};
window.MemoryEditor = {
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
};
window.ReviewAdmin = {
  loadReviewQueue,
  selectSubmission: selectReviewSubmission,
  reloadSelection: reloadReviewSelection,
  login: loginReviewAdmin,
  logout: logoutReviewAdmin,
  approve: approveReviewSubmission,
  reject: rejectReviewSubmission,
  publish: publishReviewSubmission
};
window.ModuleAdmin = {
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
};
window.FileTree = { loadFileTree, refreshFileTree };
window.EditSidebar = {
  openEditSidebar,
  closeEditSidebar,
  onEditDisciplineChanged,
  saveFromSidebar,
  deleteFromSidebar,
  onDepSearchInput,
  onDepSearchKeydown
};
window.openEditSidebar = openEditSidebar;
window.openArchConfig = openArchConfigDialog;
window.ui = ui;

let refreshTimer = null;
let topoData = null;
let stackData = null;
let isRefreshing = false;
let visibilityHandlerBound = false;
let authRefreshPending = false;

function formatSummaryMetric(value) {
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

function initUI() {
  const mainApp = $('mainApp');
  if (!mainApp) return;

  ui.init(mainApp);

  const tabDefs = [
    { id: 'topology', onActivate: () => refreshTopologyOnly() },
    { id: 'fileTree', onActivate: () => initEditSidebar().then(() => loadFileTree({ preserveState: true, showLoading: !_rootsHasRendered() })) },
    { id: 'memoryMgmt', onActivate: () => { loadGovernanceStats(); loadMemories(); } },
    { id: 'reviewQueue', onActivate: () => loadReviewQueue() }
  ];

  for (const def of tabDefs) {
    const tabBtn = document.querySelector(`.tab[data-tab="${def.id}"]`);
    const panelEl = $('panel' + def.id.charAt(0).toUpperCase() + def.id.slice(1));
    ui.fullscreen.registerTab(def.id, {
      tabButtonEl: tabBtn,
      panelEl,
      onActivate: def.onActivate,
      onDeactivate: null
    });
  }
}

function resetTopologySummary() {
  const ids = ['statModules', 'statEdges', 'statContainment', 'statCollaboration'];
  for (const id of ids) {
    const el = $(id);
    if (!el) continue;
    el.textContent = '-';
    el.title = '';
  }
}

function showTopologyMessage(title, message) {
  const sidebar = $('archSidebar');
  if (!sidebar) return;

  sidebar.style.display = 'flex';
  sidebar.innerHTML = `
    <div class="sidebar-section">
      <div class="sidebar-title">${title}</div>
      <div class="sidebar-text">${message}</div>
    </div>
  `;
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

initDetail(switchTab);

export function enterApp(projectRoot) {
  $('setupPage').style.display = 'none';
  $('mainApp').classList.add('active');
  $('appWrapper').classList.add('active');
  $('projectPath').textContent = projectRoot;
  $('projectPath').title = `Current project: ${projectRoot}`;

  initUI();
  ui.switchTab('topology');

  refresh();
  startAutoRefresh();
}

export function showSetupFromApp() {
  showSetup();
  $('appWrapper').classList.remove('active');
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

    const statModules = $('statModules');
    const statEdges = $('statEdges');
    const statContainment = $('statContainment');
    const statCollaboration = $('statCollaboration');

    if (statModules) {
      statModules.textContent = formatSummaryMetric(modulesCount);
      statModules.title = String(modulesCount);
    }
    if (statEdges) {
      statEdges.textContent = formatSummaryMetric(edgesCount);
      statEdges.title = String(edgesCount);
    }
    if (statContainment) {
      statContainment.textContent = formatSummaryMetric(containmentCount);
      statContainment.title = String(containmentCount);
    }
    if (statCollaboration) {
      statCollaboration.textContent = formatSummaryMetric(collaborationCount);
      statCollaboration.title = String(collaborationCount);
    }

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

function _rootsHasRendered() {
  const container = $('fileTreeContent');
  return !!container && container.children.length > 0;
}

window.showSetup = showSetupFromApp;
window.setProject = setProject;
window.openProjectBrowser = openProjectBrowser;
window.switchTab = switchTab;
window.refresh = refresh;
window.toggleChat = toggleChat;
window.newChat = newChat;
window.switchChatMode = switchChatMode;
window.loadSession = loadSession;
window.showSessionList = showSessionList;
window.stopChat = stopChat;
window.openModelDropdown = openModelDropdown;
window.selectProvider = selectProvider;
window.closeDd = () => {
  const dd = document.getElementById('chatModelDropdown');
  if (dd) dd.classList.add('hidden');
};
window.continueChatFromLimit = continueChatFromLimit;
window.editQueueItem = editQueueItem;
window.removeQueueItem = removeQueueItem;
window.keepEdit = keepEdit;
window.undoEdit = undoEdit;
window.beginTaskFromKnowledgeCard = beginTaskFromKnowledgeCard;
window.askClarifyingFromKnowledgeCard = askClarifyingFromKnowledgeCard;
window.queueDependencyValidationFromKnowledgeCard = queueDependencyValidationFromKnowledgeCard;
window.runGovernanceCheckFromKnowledgeCard = runGovernanceCheckFromKnowledgeCard;
window.runSuggestedActionFromKnowledgeCard = runSuggestedActionFromKnowledgeCard;
window.sendChatMessage = sendChatMessage;
window.handleChatKeydown = handleChatKeydown;
window.autoResizeInput = autoResizeInput;
window.openLlmSettings = openLlmSettings;
window.closeLlmSettings = closeLlmSettings;

initSetup();
initChatResize();
window.addEventListener('dna-admin-auth-changed', scheduleRefreshAfterAuthChange);
