/**
 * 拓扑面板 — 纯图形视图
 */

import { $ } from '../utils.js';
import { renderGraph } from '../graph/graph-renderer.js';

export function renderTopology(topoData) {
  const grid = $('topoGrid');
  if (grid) grid.style.display = 'none';

  const sidebar = $('archSidebar');
  if (sidebar) sidebar.style.display = 'none';

  let gc = $('graphContainer');
  if (!gc) {
    gc = document.createElement('div');
    gc.id = 'graphContainer';
    gc.classList.add('graph-container');
    const layout = document.querySelector('#panelTopology .arch-layout');
    if (layout) layout.insertBefore(gc, layout.firstChild);
  }
  gc.style.display = 'flex';

  renderGraph(gc, topoData);
}
