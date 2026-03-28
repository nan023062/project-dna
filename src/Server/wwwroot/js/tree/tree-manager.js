/**
 * 树管理器（只读仪表盘）
 */

import { $ } from '../utils.js';
import { disposeActiveWidgets } from '../widgets/lifecycle.js';
import { buildTree } from './tree-builder.js';
import { renderModuleNode } from './module-node.js';
import { bindTreeEvents } from './tree-events.js';
import { clearPreviewCache } from './module-preview.js';

let _prevStackModules = new Set();

export function renderTopology(topoData, stackData) {
  const container = $('topoGrid');
  disposeActiveWidgets();
  clearPreviewCache();

  if (!topoData?.modules?.length) {
    container.style.display = 'block';
    container.innerHTML = '<div class="empty">暂无模块 — AI Agent 通过 MCP 注册模块后此处自动显示</div>';
    return;
  }

  const collapsedSet = new Set();
  container.querySelectorAll('.tree-toggle.collapsed').forEach(btn => {
    collapsedSet.add(btn.dataset.toggle);
  });

  const edges = topoData.edges || [];
  const tree = buildTree(topoData.modules, edges);

  const stackInfo = buildStackInfo(stackData, topoData);

  let html = '<div class="module-tree">';
  html += renderModuleNode(tree, edges, stackInfo);
  html += '</div>';

  container.style.display = 'block';
  container.innerHTML = html;

  collapsedSet.forEach(id => {
    const toggle = container.querySelector(`[data-toggle="${id}"]`);
    const children = container.querySelector(`[data-children="${id}"]`);
    if (toggle && children) {
      toggle.classList.add('collapsed');
      children.style.display = 'none';
    }
  });

  applyStackAnimations(container, stackInfo);

  bindTreeEvents(container, topoData, stackInfo);
}

function buildStackInfo(stackData, topoData) {
  const info = {
    currentModule: stackData?.currentModule || null,
    frameMap: {},
    allStackModules: new Set(),
    dependencies: new Set(),
    entered: new Set(),
    exited: new Set(),
  };

  if (!stackData?.frames) return info;

  for (const f of stackData.frames) {
    info.frameMap[f.moduleName] = f;
    info.allStackModules.add(f.moduleName);
  }

  if (info.currentModule) {
    const depMap = topoData?.depMap || {};
    const deps = depMap[info.currentModule] || [];
    for (const d of deps) info.dependencies.add(d);
  }

  const currentSet = info.allStackModules;
  for (const m of currentSet) {
    if (!_prevStackModules.has(m)) info.entered.add(m);
  }
  for (const m of _prevStackModules) {
    if (!currentSet.has(m)) info.exited.add(m);
  }
  _prevStackModules = new Set(currentSet);

  return info;
}

function applyStackAnimations(container, stackInfo) {
  for (const name of stackInfo.entered) {
    const row = container.querySelector(`.tree-row[data-module="${name}"]`);
    if (row) {
      row.classList.add('stack-enter');
      setTimeout(() => row.classList.remove('stack-enter'), 600);
    }
  }
  for (const name of stackInfo.exited) {
    const row = container.querySelector(`.tree-row[data-module="${name}"]`);
    if (row) {
      row.classList.add('stack-exit');
      setTimeout(() => row.classList.remove('stack-exit'), 600);
    }
  }
}
