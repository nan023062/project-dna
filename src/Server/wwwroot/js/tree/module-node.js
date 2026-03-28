/**
 * 模块节点渲染（只读仪表盘）
 * 职责：单个树节点的 HTML 生成（模块名、边界颜色、突触标签、调用栈状态）
 */

import { escapeHtml } from '../utils.js';
import { getCrossDeps, getCycleDeps } from './tree-builder.js';

export function renderModuleNode(node, edges, stackInfo) {
  const isLeaf = node.children.length === 0;
  const m = node.module;
  const nodeId = 'tnode_' + node.path.replace(/[^a-zA-Z0-9]/g, '_');
  const bc = '';
  const isVirtual = !m;

  const stackCls = m ? getStackClass(m.name, stackInfo) : '';

  let html = `<div class="tree-node" id="${nodeId}">`;
  html += `<div class="tree-row${isVirtual ? ' virtual' : ''}${stackCls}" data-module="${m ? m.name : ''}">`;
  html += `<span class="tree-toggle${isLeaf ? ' leaf' : ''}" data-toggle="${nodeId}">▼</span>`;
  html += `<span class="tree-icon">${isLeaf ? '📄' : '📂'}</span>`;
  html += `<span class="tree-name${bc ? ' boundary-' + bc : ''}">${node.name}</span>`;

  if (m) {
    if (m.maintainer) html += `<span class="tree-maintainer">@${m.maintainer}</span>`;

    const frame = stackInfo?.frameMap?.[m.name];
    if (frame) html += renderTaskBadge(frame);

    html += renderSynapses(m, edges);
  }

  html += '</div>';

  if (!isLeaf) {
    html += `<div class="tree-children" data-children="${nodeId}">`;
    for (const child of node.children) {
      html += renderModuleNode(child, edges, stackInfo);
    }
    html += '</div>';
  }

  html += '</div>';
  return html;
}

function getStackClass(moduleName, stackInfo) {
  if (!stackInfo) return '';
  if (moduleName === stackInfo.currentModule) return ' stack-active';
  if (stackInfo.frameMap[moduleName]) return ' stack-suspended';
  if (stackInfo.dependencies.has(moduleName)) return ' stack-dependency';
  return '';
}

function renderTaskBadge(frame) {
  const isActive = frame.isTop;
  const cls = isActive ? 'task-badge active' : 'task-badge suspended';
  const icon = isActive ? '▶' : '⏸';

  let progress = '';
  if (frame.subTasks?.length) {
    const done = frame.subTasks.filter(s => s.completed).length;
    const total = frame.subTasks.length;
    const pct = Math.round((done / total) * 100);
    progress = `<span class="task-progress"><span class="task-progress-bar" style="width:${pct}%"></span></span><span class="task-progress-text">${done}/${total}</span>`;
  }

  return `<span class="${cls}">${icon} ${escapeHtml(truncate(frame.taskDescription, 30))}${progress}</span>`;
}

function truncate(s, max) {
  if (!s) return '';
  return s.length <= max ? s : s.slice(0, max) + '…';
}

function renderSynapses(m, edges) {
  const crossDeps = getCrossDeps(m.name, edges);
  const cycleDeps = getCycleDeps(m.name, edges);

  if (!crossDeps.length && !cycleDeps.length) return '';

  let html = '<span class="synapse-list">';
  for (const e of crossDeps) {
    const cls = e.inherited ? 'synapse inherited' : 'synapse';
    const label = e.inherited ? '间接依赖（继承自子模块）' : '依赖';
    html += `<span class="${cls}" data-jump="${e.to}" data-tip="⚡ ${label}\n${m.name} → ${e.to}\n点击跳转到目标模块">${e.to}</span>`;
  }
  for (const e of cycleDeps) {
    html += `<span class="synapse cycle" data-jump="${e.to}" data-tip="🔄 循环依赖\n${m.name} ↔ ${e.to}\n形成环路，需消除其中一个方向的依赖">🔄 ${e.to}</span>`;
  }
  html += '</span>';
  return html;
}
