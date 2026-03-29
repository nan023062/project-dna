/**
 * 应用入口
 * - UIManager 驱动的界面管理
 * - Tab 注册与切换
 * - 自动刷新
 * - Chat 面板与 LLM 配置
 */

import { $ } from './utils.js';
import { ui } from './ui/ui-manager.js';
import { initSetup, showSetup, setProject, openProjectBrowser } from './setup.js';
import { renderTopology } from './panels/topology.js';
import { loadGovernanceStats, checkFreshness, detectConflicts, archiveStale, condenseNodeKnowledge, condenseAllKnowledge, configureCondenseSchedule } from './panels/governance.js';
import { loadMemories, selectMemory, createNew, saveMemory, deleteMemory, applyTemplate, onLayerTypeChanged, addFeature, addNodeId, addTag } from './panels/memory-editor.js';
import { loadModuleManagement, saveModule, deleteModule, saveCrosswork, deleteCrosswork, newModule, newCrosswork, onDisciplineChanged, addCrossworkParticipant, newDiscipline, saveDiscipline, deleteDiscipline, addLayerRow } from './panels/arch-config.js';
import { loadFileTree, refreshFileTree } from './panels/file-tree.js';
import { initEditSidebar, openEditSidebar, closeEditSidebar, onEditDisciplineChanged, saveFromSidebar, deleteFromSidebar, onDepSearchInput, onDepSearchKeydown } from './dialogs/module-editor.js';
import { initDetail } from './panels/detail.js';
import { openArchConfigDialog } from './dialogs/arch-config-dialog.js';
import { toggleChat, newChat, sendChatMessage, handleChatKeydown, autoResizeInput, initChatResize, switchChatMode, loadSession, showSessionList, stopChat, openModelDropdown, selectProvider, continueChatFromLimit, editQueueItem, removeQueueItem, keepEdit, undoEdit, beginTaskFromKnowledgeCard, askClarifyingFromKnowledgeCard, queueDependencyValidationFromKnowledgeCard, runGovernanceCheckFromKnowledgeCard, runSuggestedActionFromKnowledgeCard } from './chat/chat-panel.js';
import { openLlmSettings, closeLlmSettings, loadProviders } from './chat/llm-settings.js';

// ── Window bridges ──
window.Governance = { checkFreshness, detectConflicts, archiveStale, condenseNodeKnowledge, condenseAllKnowledge, configureCondenseSchedule };
window.MemoryEditor = { loadMemories, selectMemory, createNew, saveMemory, deleteMemory, applyTemplate, onLayerTypeChanged, addFeature, addNodeId, addTag };
window.ModuleAdmin = { loadModuleManagement, saveModule, deleteModule, saveCrosswork, deleteCrosswork, newModule, newCrosswork, onDisciplineChanged, addCrossworkParticipant, newDiscipline, saveDiscipline, deleteDiscipline, addLayerRow };
window.FileTree = { loadFileTree, refreshFileTree };
window.EditSidebar = { openEditSidebar, closeEditSidebar, onEditDisciplineChanged, saveFromSidebar, deleteFromSidebar, onDepSearchInput, onDepSearchKeydown };
window.openEditSidebar = openEditSidebar;
window.openArchConfig = openArchConfigDialog;
window.ui = ui;

let refreshTimer = null;
let topoData = null;
let stackData = null;
let isRefreshing = false;
let visibilityHandlerBound = false;

function formatSummaryMetric(value) {
  if (!Number.isFinite(value)) return '-';
  try {
    return new Intl.NumberFormat('zh-CN', {
      notation: 'compact',
      maximumFractionDigits: 1
    }).format(value);
  } catch {
    if (value >= 100000000) return `${(value / 100000000).toFixed(1)}亿`;
    if (value >= 10000) return `${(value / 10000).toFixed(1)}万`;
    return String(value);
  }
}

// ── 初始化 UI 框架 ──
function initUI() {
  const mainApp = $('mainApp');
  if (!mainApp) return;

  ui.init(mainApp);

  const tabDefs = [
    { id: 'topology', onActivate: null },
    { id: 'fileTree', onActivate: () => initEditSidebar().then(() => loadFileTree({ preserveState: true, showLoading: !_rootsHasRendered() })) },
    { id: 'memoryMgmt', onActivate: () => { loadGovernanceStats(); loadMemories(); } },
  ];

  for (const def of tabDefs) {
    const tabBtn = document.querySelector(`.tab[data-tab="${def.id}"]`);
    const panelEl = $('panel' + def.id.charAt(0).toUpperCase() + def.id.slice(1));
    ui.fullscreen.registerTab(def.id, {
      tabButtonEl: tabBtn,
      panelEl: panelEl,
      onActivate: def.onActivate,
      onDeactivate: null
    });
  }
}

initDetail(switchTab);

export function enterApp(projectRoot) {
  $('setupPage').style.display = 'none';
  $('mainApp').classList.add('active');
  $('appWrapper').classList.add('active');
  $('projectPath').textContent = projectRoot;
  $('projectPath').title = '当前项目: ' + projectRoot + ' — 点击切换';

  initUI();
  ui.switchTab('topology');

  refresh();
  startAutoRefresh();
  loadProviders();
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
  $('refreshInfo').textContent = '自动刷新中';

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
        $('refreshInfo').textContent = '自动刷新中';
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
  $('statusText').textContent = '加载中…';
  try {
    const topoRes = await fetch('/api/topology');

    if (!topoRes.ok) {
      const d = await topoRes.json().catch(() => ({}));
      if (d.error?.includes('未配置')) { showSetupFromApp(); return; }
      $('statusText').textContent = '错误: ' + (d.error || topoRes.status);
      return;
    }

    topoData = await topoRes.json();
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
    return true;
  } catch (e) {
    $('statusText').textContent = '连接失败: ' + e.message;
    return false;
  }
}

async function refresh(activeForce = true) {
  if (isRefreshing) return;
  if (!activeForce && shouldSkipAutoRefresh()) {
    $('refreshInfo').textContent = '编辑中，自动刷新已暂缓';
    return;
  }

  isRefreshing = true;
  try {
    const activeTab = ui.activeTabId || 'topology';
    if (activeTab === 'fileTree') {
      await refreshFileTree();
      $('statusText').textContent = '文件树已刷新 · ' + new Date().toLocaleTimeString();
      $('refreshInfo').textContent = '自动刷新中';
      return;
    }

    if (activeTab === 'memoryMgmt') {
      await Promise.allSettled([loadGovernanceStats(), loadMemories()]);
      $('statusText').textContent = '记忆面板已刷新 · ' + new Date().toLocaleTimeString();
      $('refreshInfo').textContent = '自动刷新中';
      return;
    }

    const ok = await refreshTopologyOnly();
    if (ok) $('statusText').textContent = '已刷新 · ' + new Date().toLocaleTimeString();
    $('refreshInfo').textContent = '自动刷新中';
  } finally {
    isRefreshing = false;
  }
}

function _rootsHasRendered() {
  const container = $('fileTreeContent');
  return !!container && container.children.length > 0;
}

// ── Window bridges for HTML onclick ──
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
window.closeDd = () => { const dd = document.getElementById('chatModelDropdown'); if (dd) dd.classList.add('hidden'); };
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
