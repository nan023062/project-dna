/**
 * 模块预览 — 基于通用 Tooltip 的悬停预览
 * 只绑定在 .tree-name 元素上，不与突触标签冲突
 */

import { escapeHtml } from '../utils.js';
import { showTooltip, hideTooltip } from '../widgets/tooltip.js';

const OWNER = 'module-preview';
const PRIORITY = 1;

export function clearPreviewCache() {}

export function bindModulePreviewEvents(container, modules, deps, rdeps, stackInfo) {
  container.querySelectorAll('.tree-row[data-module]').forEach(row => {
    const name = row.dataset.module;
    if (!name) return;

    if (stackInfo?.frameMap?.[name]) return;

    const nameEl = row.querySelector('.tree-name');
    if (!nameEl) return;

    const mod = modules.find(m => m.name === name);

    nameEl.addEventListener('mouseenter', (e) => {
      showTooltip(OWNER, PRIORITY, buildPreviewHtml(name, mod), e);
    });

    nameEl.addEventListener('mousemove', (e) => {
      showTooltip(OWNER, PRIORITY, null, e);
    });

    nameEl.addEventListener('mouseleave', () => {
      hideTooltip(OWNER);
    });
  });
}

const BOUNDARY_LABEL = { hard: 'Hard', soft: 'Soft', shared: 'Shared' };

function buildPreviewHtml(moduleName, mod) {
  const bc = '';

  let html = '<div class="preview-tip">';
  html += `<div class="preview-header"><span class="preview-title boundary-${bc}">${escapeHtml(moduleName)}</span>`;
  html += `<span class="preview-boundary boundary-${bc}">${BOUNDARY_LABEL[bc] || bc}</span>`;
  html += '</div>';

  if (mod?.maintainer) {
    html += `<div class="preview-maintainer">@${escapeHtml(mod.maintainer)}</div>`;
  }

  if (mod?.summary) {
    html += `<div class="preview-summary">${escapeHtml(mod.summary)}</div>`;
  } else {
    html += '<div class="preview-summary preview-empty">（未填写概述）</div>';
  }

  if (mod?.keywords?.length) {
    html += '<div class="preview-keywords">';
    for (const kw of mod.keywords) {
      html += `<span class="preview-kw-tag">${escapeHtml(kw)}</span>`;
    }
    html += '</div>';
  }

  html += '</div>';
  return html;
}
