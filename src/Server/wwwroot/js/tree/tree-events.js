/**
 * 树级事件绑定（只读仪表盘）
 * 职责：折叠/展开、单击/双击行、突触跳转与tooltip、栈模块任务tooltip
 */

import { escapeHtml } from '../utils.js';
import { showModuleDetail } from '../panels/detail.js';
import { bindModulePreviewEvents } from './module-preview.js';
import { showTooltip, hideTooltip } from '../widgets/tooltip.js';
import { renderArchSidebar } from './tree-sidebar.js';

export function bindTreeEvents(container, topoData, stackInfo) {
  const modules = topoData.modules || [];
  const deps = topoData.depMap || {};
  const rdeps = topoData.rdepMap || {};

  bindToggle(container);
  bindRowClick(container, topoData, modules, deps, rdeps);
  bindStackPreview(container, stackInfo);
  bindModulePreviewEvents(container, modules, deps, rdeps, stackInfo);
  bindSynapseTooltip(container);
  bindSynapseJump(container, topoData);
}

function bindToggle(container) {
  container.querySelectorAll('.tree-toggle:not(.leaf)').forEach(btn => {
    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      const id = btn.dataset.toggle;
      const children = container.querySelector(`[data-children="${id}"]`);
      if (!children) return;
      const collapsed = children.style.display === 'none';
      children.style.display = collapsed ? '' : 'none';
      btn.classList.toggle('collapsed', !collapsed);
    });
  });
}

function bindRowClick(container, topoData, modules, deps, rdeps) {
  container.querySelectorAll('.tree-row[data-module]').forEach(row => {
    if (!row.dataset.module) return;

    row.addEventListener('click', (e) => {
      if (e.target.closest('.synapse') || e.target.closest('.tree-toggle')) return;
      renderArchSidebar(row.dataset.module, topoData, modules, deps, rdeps);
      container.querySelectorAll('.tree-row.selected').forEach(r => r.classList.remove('selected'));
      row.classList.add('selected');
    });

    row.addEventListener('dblclick', (e) => {
      if (e.target.closest('.synapse') || e.target.closest('.tree-toggle')) return;
      const m = topoData.modules.find(x => x.name === row.dataset.module);
      showModuleDetail(row.dataset.module, m?.type);
    });
  });
}

const STACK_TIP_OWNER = 'stack-task';
const STACK_TIP_PRIORITY = 3;

function bindStackPreview(container, stackInfo) {
  if (!stackInfo?.frameMap) return;

  container.querySelectorAll('.task-badge').forEach(badge => {
    const row = badge.closest('.tree-row');
    if (!row) return;
    const moduleName = row.dataset.module;
    const frame = stackInfo.frameMap[moduleName];
    if (!frame) return;

    badge.addEventListener('mouseenter', (e) => {
      showTooltip(STACK_TIP_OWNER, STACK_TIP_PRIORITY, buildTaskTooltip(frame), e);
    });
    badge.addEventListener('mousemove', (e) => {
      showTooltip(STACK_TIP_OWNER, STACK_TIP_PRIORITY, null, e);
    });
    badge.addEventListener('mouseleave', () => {
      hideTooltip(STACK_TIP_OWNER);
    });
  });
}

function buildTaskTooltip(frame) {
  const isActive = frame.isTop;
  const statusIcon = isActive ? '▶' : '⏸';
  const statusText = isActive ? '正在执行' : '已挂起';
  const statusCls = isActive ? 'task-tip-active' : 'task-tip-suspended';

  let html = '<div class="task-tip">';
  html += `<div class="task-tip-header"><span class="${statusCls}">${statusIcon} ${statusText}</span><span class="task-tip-module">${escapeHtml(frame.moduleName)}</span></div>`;
  html += `<div class="task-tip-desc">${escapeHtml(frame.taskDescription)}</div>`;

  if (frame.suspendReason) {
    html += `<div class="task-tip-field"><span class="task-tip-label">⏳ 挂起原因</span>${escapeHtml(frame.suspendReason)}</div>`;
  }
  if (frame.resumeCondition) {
    html += `<div class="task-tip-field"><span class="task-tip-label">🎯 恢复条件</span>${escapeHtml(frame.resumeCondition)}</div>`;
  }
  if (frame.contextSummary) {
    html += `<div class="task-tip-field"><span class="task-tip-label">📋 上下文</span>${escapeHtml(frame.contextSummary)}</div>`;
  }

  if (frame.subTasks?.length) {
    const done = frame.subTasks.filter(s => s.completed).length;
    html += `<div class="task-tip-subtasks"><span class="task-tip-label">📌 子任务 ${done}/${frame.subTasks.length}</span>`;
    for (const st of frame.subTasks) {
      html += `<div class="task-tip-subtask ${st.completed ? 'done' : ''}">${st.completed ? '✅' : '⬜'} ${escapeHtml(st.description)}</div>`;
    }
    html += '</div>';
  }

  if (frame.createdAt || frame.updatedAt) {
    html += '<div class="task-tip-time">';
    if (frame.createdAt) html += `创建: ${formatTime(frame.createdAt)}`;
    if (frame.updatedAt) html += ` · 更新: ${formatTime(frame.updatedAt)}`;
    html += '</div>';
  }

  html += '</div>';
  return html;
}

function formatTime(iso) {
  if (!iso) return '';
  try {
    const d = new Date(iso);
    return d.toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' });
  } catch { return iso; }
}

function bindSynapseTooltip(container) {
  container.querySelectorAll('.synapse[data-tip]').forEach(tag => {
    tag.addEventListener('mouseenter', (e) => {
      const tip = tag.dataset.tip || '';
      const html = `<div class="tip-synapse">${escapeHtml(tip).replace(/\n/g, '<br>')}</div>`;
      showTooltip('synapse', 2, html, e);
    });
    tag.addEventListener('mousemove', (e) => showTooltip('synapse', 2, null, e));
    tag.addEventListener('mouseleave', () => hideTooltip('synapse'));
  });
}

function bindSynapseJump(container, topoData) {
  container.querySelectorAll('.synapse[data-jump]').forEach(tag => {
    tag.addEventListener('click', (e) => {
      e.stopPropagation();
      const targetName = tag.dataset.jump;
      const targetModule = topoData.modules.find(m => m.name === targetName);
      if (!targetModule) return;

      const targetNodeId = 'tnode_' + targetModule.relativePath.replace(/[^a-zA-Z0-9]/g, '_');
      const targetEl = document.getElementById(targetNodeId);
      if (!targetEl) return;

      let parent = targetEl.parentElement;
      while (parent) {
        if (parent.dataset.children) {
          parent.style.display = '';
          const toggle = container.querySelector(`[data-toggle="${parent.dataset.children}"]`);
          if (toggle) toggle.classList.remove('collapsed');
        }
        parent = parent.parentElement;
      }

      targetEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
      const row = targetEl.querySelector('.tree-row');
      if (row) {
        row.classList.add('highlight');
        setTimeout(() => row.classList.remove('highlight'), 2000);
      }
    });
  });
}
