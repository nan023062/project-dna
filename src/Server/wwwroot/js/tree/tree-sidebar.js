/**
 * 架构侧边栏
 * 职责：单击模块时显示依赖概览面板
 */

import { $, escapeHtml } from '../utils.js';
import { registerDisposable, unregisterDisposable } from '../widgets/lifecycle.js';
import { showModuleDetail } from '../panels/detail.js';

let _sidebarDispose = null;

function dismissSidebar() {
  const sidebar = $('archSidebar');
  if (sidebar) sidebar.style.display = 'none';
  const container = $('topoGrid');
  if (container) container.querySelectorAll('.tree-row.selected').forEach(r => r.classList.remove('selected'));
  if (_sidebarDispose) { unregisterDisposable(_sidebarDispose); _sidebarDispose = null; }
}

export function renderArchSidebar(moduleName, topoData, modules, deps, rdeps) {
  const sidebar = $('archSidebar');
  if (!sidebar) return;

  if (_sidebarDispose) unregisterDisposable(_sidebarDispose);
  _sidebarDispose = () => dismissSidebar();
  registerDisposable(_sidebarDispose);

  const mod = modules.find(m => m.name === moduleName);
  const myDeps = deps[moduleName] || [];
  const myRdeps = rdeps[moduleName] || [];
  const edges = topoData.edges || [];

  const inheritedSet = new Set(
    edges.filter(e => e.from === moduleName && e.inherited).map(e => e.to)
  );

  const directDeps = myDeps.filter(d => !inheritedSet.has(d));
  const inheritedDeps = myDeps.filter(d => inheritedSet.has(d));

  const bc = '';
  let html = '<div class="sidebar-header">';
  html += `<span class="sidebar-title boundary-${bc}">${escapeHtml(moduleName)}</span>`;
  html += '<button class="sidebar-close" id="sidebarClose">×</button>';
  html += '</div>';

  if (mod?.maintainer) {
    html += '<div class="sidebar-meta">';
    html += `<span class="sidebar-maintainer">@${escapeHtml(mod.maintainer)}</span>`;
    html += '</div>';
  }

  html += '<div class="sidebar-section">';
  html += `<div class="sidebar-section-title"><span class="sidebar-arrow">←</span> 直接依赖 <span class="sidebar-count">${directDeps.length}</span></div>`;
  if (directDeps.length === 0) {
    html += '<div class="sidebar-empty">无直接依赖</div>';
  } else {
    for (const d of directDeps) {
      html += `<div class="sidebar-dep-item sidebar-dep-target" data-jump="${escapeHtml(d)}">`;
      html += `<span>⚡</span> <span>${escapeHtml(d)}</span>`;
      html += '</div>';
    }
  }
  html += '</div>';

  if (inheritedDeps.length > 0) {
    html += '<div class="sidebar-section">';
    html += `<div class="sidebar-section-title"><span class="sidebar-arrow">←</span> 间接依赖 <span class="sidebar-count">${inheritedDeps.length}</span></div>`;
    for (const d of inheritedDeps) {
      html += `<div class="sidebar-dep-item sidebar-dep-inherited" data-jump="${escapeHtml(d)}">`;
      html += `<span>📎</span> <span>${escapeHtml(d)}</span>`;
      html += '</div>';
    }
    html += '</div>';
  }

  html += '<div class="sidebar-section">';
  html += `<div class="sidebar-section-title"><span class="sidebar-arrow">→</span> 依赖我的 <span class="sidebar-count">${myRdeps.length}</span></div>`;
  if (myRdeps.length === 0) {
    html += '<div class="sidebar-empty">无被依赖</div>';
  } else {
    for (const d of myRdeps) {
      html += `<div class="sidebar-dep-item sidebar-dep-source" data-jump="${escapeHtml(d)}">`;
      html += `<span>📦</span> <span>${escapeHtml(d)}</span>`;
      html += '</div>';
    }
  }
  html += '</div>';

  html += '<div class="sidebar-footer">';
  html += `<button class="btn btn-primary btn-sm sidebar-detail-btn" data-module="${escapeHtml(moduleName)}">查看详情 →</button>`;
  html += '</div>';

  sidebar.innerHTML = html;
  sidebar.style.display = '';

  sidebar.querySelector('#sidebarClose').addEventListener('click', () => dismissSidebar());

  sidebar.querySelectorAll('[data-jump]').forEach(el => {
    el.addEventListener('click', () => {
      const target = el.dataset.jump;
      const container = $('topoGrid');
      const targetRow = container?.querySelector(`.tree-row[data-module="${target}"]`);
      if (targetRow) {
        let parent = targetRow.closest('.tree-children');
        while (parent) {
          parent.style.display = '';
          const toggle = container.querySelector(`[data-toggle="${parent.dataset.children}"]`);
          if (toggle) toggle.classList.remove('collapsed');
          parent = parent.parentElement?.closest('.tree-children');
        }
        targetRow.scrollIntoView({ behavior: 'smooth', block: 'center' });
        targetRow.classList.add('highlight');
        setTimeout(() => targetRow.classList.remove('highlight'), 2000);
      }
    });
  });

  sidebar.querySelector('.sidebar-detail-btn')?.addEventListener('click', () => {
    showModuleDetail(moduleName, module.type);
  });
}
