/**
 * 图形拓扑渲染器 — 基于 ELK.js 布局 + SVG 渲染
 *
 * 状态保持策略（符合 ux-usability 规范）：
 * - 数据指纹相同 → 完全跳过重建，零 DOM 操作
 * - 数据变化 → 重建 SVG，但恢复用户的缩放/平移/选中状态
 * - 仅首次渲染执行 fitView 自动居中
 */

const NODE_W = 190;
const NODE_H = 44;
const GROUP_PAD = { top: 42, left: 20, bottom: 20, right: 20 };
const EDGE_COLORS = {
  dependency: '#4ade80',
  containment: '#f59e0b',
  collaboration: '#60a5fa',
  computed: '#9ca3af'
};

let _elk = null;
let _svgRoot = null;
let _transform = { x: 0, y: 0, scale: 1 };
let _userHasInteracted = false;
let _drag = null;
let _selectedNode = null;
let _topoData = null;
let _onModuleClick = null;
let _lastFingerprint = null;
let _boundGlobal = false;
let _relationFilter = { dependency: true, containment: true, collaboration: true };
let _visibleEdges = [];

function getElk() {
  if (_elk) return _elk;
  if (typeof ELK === 'undefined') throw new Error('ELK.js not loaded');
  _elk = new ELK();
  return _elk;
}

function normalizeRelationFilter(raw) {
  const defaults = { dependency: true, containment: true, collaboration: true };
  if (!raw) return defaults;
  return {
    dependency: raw.dependency !== false,
    containment: raw.containment !== false,
    collaboration: raw.collaboration !== false
  };
}

function getAllRelationEdges(topo) {
  if (!topo) return [];
  if (Array.isArray(topo.relationEdges) && topo.relationEdges.length > 0) {
    return topo.relationEdges.map(e => ({
      from: e.from,
      to: e.to,
      relation: e.relation || 'dependency',
      isComputed: !!e.isComputed,
      crossWorkNames: e.crossWorkNames || []
    }));
  }

  const depEdges = (topo.edges || []).map(e => ({
    from: e.from,
    to: e.to,
    relation: 'dependency',
    isComputed: !!e.isComputed
  }));

  const containmentEdges = (topo.containmentEdges || []).map(e => ({
    from: e.from,
    to: e.to,
    relation: 'containment',
    isComputed: !!e.isComputed
  }));

  const collaborationEdges = (topo.collaborationEdges || []).map(e => ({
    from: e.from,
    to: e.to,
    relation: 'collaboration',
    isComputed: !!e.isComputed,
    crossWorkNames: e.crossWorkNames || []
  }));

  return depEdges.concat(containmentEdges, collaborationEdges);
}

function getVisibleEdges(topo, filter) {
  const all = getAllRelationEdges(topo);
  return all.filter(e => {
    if (e.relation === 'dependency') return !!filter.dependency;
    if (e.relation === 'containment') return !!filter.containment;
    if (e.relation === 'collaboration') return !!filter.collaboration;
    return true;
  });
}

function topoFingerprint(topo) {
  if (!topo) return '';
  const mods = (topo.modules || []).map(m => m.name).sort().join(',');
  const rels = (getAllRelationEdges(topo) || [])
    .map(e => `${e.relation || 'dependency'}:${e.from}>${e.to}`)
    .sort()
    .join(',');
  const filter = `${_relationFilter.dependency ? 1 : 0}${_relationFilter.containment ? 1 : 0}${_relationFilter.collaboration ? 1 : 0}`;
  return `${mods}|${rels}|${filter}`;
}

export async function renderGraph(container, topoData, opts = {}) {
  _onModuleClick = opts.onModuleClick || null;
  _relationFilter = normalizeRelationFilter(opts.relationFilter);
  _visibleEdges = getVisibleEdges(topoData, _relationFilter);

  if (!topoData?.modules?.length) {
    _lastFingerprint = null;
    container.innerHTML = '<div class="graph-empty">暂无图节点 — AI Agent 通过 MCP 注册后此处自动显示</div>';
    return;
  }

  const fp = topoFingerprint(topoData);
  if (fp === _lastFingerprint && _svgRoot && container.contains(_svgRoot)) {
    _topoData = topoData;
    return;
  }

  const savedTransform = _userHasInteracted ? { ..._transform } : null;
  const savedSelection = _selectedNode;

  _topoData = topoData;
  _lastFingerprint = fp;

  if (!_svgRoot || !container.contains(_svgRoot)) {
    container.innerHTML = '<div class="graph-loading">布局计算中…</div>';
  }

  try {
    const cw = container.clientWidth || container.getBoundingClientRect().width || 800;
    const ch = container.clientHeight || container.getBoundingClientRect().height || 600;
    const elkGraph = toElkGraph(topoData, cw / ch);
    const elk = getElk();
    const laid = await elk.layout(elkGraph);
    container.innerHTML = '';
    renderSvg(container, laid, topoData, savedTransform, savedSelection);
  } catch (e) {
    console.error('Graph render error', e);
    container.innerHTML = `<div class="graph-empty">图形渲染失败: ${e.message}</div>`;
  }
}

function toElkGraph(topo, containerAspectRatio = 1.6) {
  const { modules } = topo;
  const visibleEdges = getVisibleEdges(topo, _relationFilter);

  const discMap = {};
  for (const m of modules) {
    const d = m.discipline || 'root';
    if (!discMap[d]) discMap[d] = [];
    discMap[d].push(m);
  }

  const children = [];
  const nodeIdSet = new Set();

  for (const [disc, mods] of Object.entries(discMap)) {
    const disciplineDisplayName = mods.find(m => m.disciplineDisplayName)?.disciplineDisplayName || disc;
    const groupChildren = mods.map(m => {
      nodeIdSet.add(m.name);
      return {
        id: m.name,
        width: NODE_W,
        height: NODE_H,
        labels: [{ text: m.displayName || m.name }],
        layoutOptions: {},
        _module: m
      };
    });

    children.push({
      id: `group_${disc}`,
      labels: [{ text: disciplineDisplayName }],
      children: groupChildren,
      layoutOptions: {
        'elk.padding': `[top=${GROUP_PAD.top},left=${GROUP_PAD.left},bottom=${GROUP_PAD.bottom},right=${GROUP_PAD.right}]`
      },
      _discipline: disc,
      _disciplineDisplayName: disciplineDisplayName
    });
  }

  const elkEdges = [];
  for (const e of visibleEdges) {
    if (!nodeIdSet.has(e.from) || !nodeIdSet.has(e.to)) continue;

    const fromDisc = modules.find(m => m.name === e.from)?.discipline || 'root';
    const toDisc = modules.find(m => m.name === e.to)?.discipline || 'root';

    elkEdges.push({
      id: `e_${e.relation || 'dependency'}_${e.from}_${e.to}`,
      sources: [e.from],
      targets: [e.to],
      _relation: e.relation || 'dependency',
      _isComputed: !!e.isComputed,
      _crossWorkNames: e.crossWorkNames || [],
      _isHierarchical: fromDisc === toDisc
    });
  }

  return {
    id: 'root',
    children,
    edges: elkEdges,
    layoutOptions: {
      'elk.algorithm': 'layered',
      'elk.direction': 'DOWN',
      'elk.spacing.nodeNode': '40',
      'elk.spacing.edgeNode': '25',
      'elk.spacing.componentComponent': '50',
      'elk.layered.spacing.nodeNodeBetweenLayers': '70',
      'elk.layered.spacing.edgeNodeBetweenLayers': '30',
      'elk.hierarchyHandling': 'INCLUDE_CHILDREN',
      'elk.layered.crossingMinimization.strategy': 'LAYER_SWEEP',
      'elk.edgeRouting': 'SPLINES',
      'elk.separateConnectedComponents': 'true',
      'elk.aspectRatio': String(containerAspectRatio.toFixed(2))
    }
  };
}

function renderSvg(container, laid, topo, savedTransform, savedSelection) {
  const svgNs = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(svgNs, 'svg');
  svg.classList.add('graph-svg');
  svg.setAttribute('width', '100%');
  svg.setAttribute('height', '100%');

  const defs = document.createElementNS(svgNs, 'defs');
  defs.innerHTML = `
    <marker id="arrow-dependency" viewBox="0 0 10 6" refX="10" refY="3" markerWidth="8" markerHeight="6" orient="auto-start-reverse">
      <path d="M0,0 L10,3 L0,6" fill="${EDGE_COLORS.dependency}" />
    </marker>
    <marker id="arrow-collaboration" viewBox="0 0 10 6" refX="10" refY="3" markerWidth="8" markerHeight="6" orient="auto-start-reverse">
      <path d="M0,0 L10,3 L0,6" fill="${EDGE_COLORS.collaboration}" />
    </marker>
    <marker id="arrow-computed" viewBox="0 0 10 6" refX="10" refY="3" markerWidth="8" markerHeight="6" orient="auto-start-reverse">
      <path d="M0,0 L10,3 L0,6" fill="${EDGE_COLORS.computed}" />
    </marker>
  `;
  svg.appendChild(defs);

  const viewport = document.createElementNS(svgNs, 'g');
  viewport.classList.add('graph-viewport');
  svg.appendChild(viewport);

  drawGroups(viewport, laid.children || [], svgNs);
  drawEdges(viewport, laid, svgNs);
  drawNodes(viewport, laid.children || [], svgNs, topo);

  container.appendChild(svg);
  _svgRoot = svg;
  svg._laid = laid;

  bindInteractions(svg, viewport);

  if (savedTransform) {
    _transform = savedTransform;
    applyTransform(svg);
  } else {
    requestAnimationFrame(() => {
      requestAnimationFrame(() => fitView(svg, laid));
    });
  }

  if (savedSelection) {
    const nodeG = svg.querySelector(`.graph-node[data-module="${savedSelection}"]`);
    if (nodeG) {
      nodeG.classList.add('selected');
      _selectedNode = savedSelection;
    }
  }
}

// ═══════════════════════════════════════
//  SVG 绘制
// ═══════════════════════════════════════

function drawGroups(viewport, groups, ns) {
  for (const g of groups) {
    if (!g._discipline) continue;
    const x = g.x || 0, y = g.y || 0;
    const w = g.width || 200, h = g.height || 100;

    const rect = document.createElementNS(ns, 'rect');
    rect.setAttribute('x', x);
    rect.setAttribute('y', y);
    rect.setAttribute('width', w);
    rect.setAttribute('height', h);
    rect.setAttribute('rx', '8');
    rect.classList.add('graph-group-bg');
    viewport.appendChild(rect);

    const label = document.createElementNS(ns, 'text');
    label.setAttribute('x', x + 12);
    label.setAttribute('y', y + 22);
    label.classList.add('graph-group-label');
    label.textContent = g._disciplineDisplayName || g._discipline;
    viewport.appendChild(label);
  }
}

function drawNodes(viewport, groups, ns, topo) {
  for (const g of groups) {
    const gx = g.x || 0, gy = g.y || 0;

    for (const node of (g.children || [])) {
      const nx = gx + (node.x || 0);
      const ny = gy + (node.y || 0);
      const w = node.width || NODE_W;
      const h = node.height || NODE_H;
      const m = node._module;

      const group = document.createElementNS(ns, 'g');
      group.classList.add('graph-node');
      group.dataset.module = node.id;

      const rect = document.createElementNS(ns, 'rect');
      rect.setAttribute('x', nx);
      rect.setAttribute('y', ny);
      rect.setAttribute('width', w);
      rect.setAttribute('height', h);
      rect.setAttribute('rx', '6');
      rect.classList.add('graph-node-rect', `layer-${m?.computedLayer ?? m?.layer ?? 0}`);
      group.appendChild(rect);

      const text = document.createElementNS(ns, 'text');
      text.setAttribute('x', nx + w / 2);
      text.setAttribute('y', ny + h / 2 + 1);
      text.classList.add('graph-node-text');
      const displayName = m?.displayName || node.id;
      text.textContent = displayName.length > 18 ? displayName.slice(0, 17) + '…' : displayName;
      group.appendChild(text);

      if (m?.computedLayer != null) {
        const badge = document.createElementNS(ns, 'text');
        badge.setAttribute('x', nx + w - 8);
        badge.setAttribute('y', ny + 12);
        badge.classList.add('graph-layer-badge');
        badge.textContent = `L${m.computedLayer}`;
        group.appendChild(badge);
      }

      viewport.appendChild(group);
    }
  }
}

function drawEdges(viewport, laid, ns) {
  const nodePositions = {};
  for (const g of (laid.children || [])) {
    const gx = g.x || 0, gy = g.y || 0;
    for (const n of (g.children || [])) {
      nodePositions[n.id] = {
        x: gx + (n.x || 0), y: gy + (n.y || 0),
        w: n.width || NODE_W, h: n.height || NODE_H
      };
    }
  }

  for (const edge of (laid.edges || [])) {
    const relation = edge._relation || 'dependency';
    const isComputed = !!edge._isComputed;
    const color = isComputed ? EDGE_COLORS.computed : (EDGE_COLORS[relation] || EDGE_COLORS.dependency);
    const markerId = relation === 'containment'
      ? null
      : (isComputed ? 'arrow-computed' : `arrow-${relation}`);

    const sections = edge.sections || [];
    if (sections.length === 0) {
      const fromPos = nodePositions[edge.sources?.[0]];
      const toPos = nodePositions[edge.targets?.[0]];
      if (!fromPos || !toPos) continue;

      const path = document.createElementNS(ns, 'path');
      path.dataset.from = edge.sources?.[0] || '';
      path.dataset.to = edge.targets?.[0] || '';
      path.setAttribute('d', bezierPath(
        fromPos.x + fromPos.w / 2, fromPos.y + fromPos.h,
        toPos.x + toPos.w / 2, toPos.y
      ));
      styleEdge(path, relation, color, markerId, isComputed);
      viewport.appendChild(path);
      continue;
    }

    for (const section of sections) {
      const sp = section.startPoint, ep = section.endPoint;
      const bends = section.bendPoints || [];
      const path = document.createElementNS(ns, 'path');
      path.dataset.from = edge.sources?.[0] || '';
      path.dataset.to = edge.targets?.[0] || '';

      if (bends.length === 0) {
        path.setAttribute('d', bezierPath(sp.x, sp.y, ep.x, ep.y));
      } else {
        let d = `M${sp.x},${sp.y}`;
        let prev = sp;
        for (const bp of bends) {
          d += ` Q${prev.x},${bp.y} ${bp.x},${bp.y}`;
          prev = bp;
        }
        d += ` Q${prev.x},${ep.y} ${ep.x},${ep.y}`;
        path.setAttribute('d', d);
      }

      styleEdge(path, relation, color, markerId, isComputed);
      viewport.appendChild(path);
    }
  }
}

function styleEdge(path, relation, color, markerId, isComputed) {
  path.classList.add('graph-edge');
  path.classList.add(`graph-edge-${relation}`);
  path.setAttribute('stroke', color);
  if (markerId) path.setAttribute('marker-end', `url(#${markerId})`);
  if (relation === 'containment') path.setAttribute('stroke-dasharray', '4,5');
  if (relation === 'collaboration') path.setAttribute('stroke-dasharray', '8,4');
  if (isComputed) path.setAttribute('stroke-dasharray', '5,4');
}

function bezierPath(x1, y1, x2, y2) {
  const cpOffset = Math.max(Math.abs(y2 - y1) * 0.4, 30);
  return `M${x1},${y1} C${x1},${y1 + cpOffset} ${x2},${y2 - cpOffset} ${x2},${y2}`;
}

// ═══════════════════════════════════════
//  视口控制
// ═══════════════════════════════════════

function fitView(svg, laid) {
  const totalW = laid.width || 800;
  const totalH = laid.height || 600;

  let containerW = svg.clientWidth;
  let containerH = svg.clientHeight;
  if (!containerW || !containerH) {
    const rect = svg.getBoundingClientRect();
    containerW = rect.width || 800;
    containerH = rect.height || 600;
  }
  if (containerW < 100) containerW = svg.parentElement?.clientWidth || 800;
  if (containerH < 100) containerH = svg.parentElement?.clientHeight || 600;

  const scale = Math.min(
    (containerW * 0.94) / totalW,
    (containerH * 0.94) / totalH,
    3.0
  );

  _transform = {
    x: (containerW - totalW * scale) / 2,
    y: (containerH - totalH * scale) / 2,
    scale
  };
  _userHasInteracted = false;
  applyTransform(svg);
}

function applyTransform(svg) {
  const vp = svg.querySelector('.graph-viewport');
  if (vp) {
    vp.setAttribute('transform',
      `translate(${_transform.x},${_transform.y}) scale(${_transform.scale})`);
  }
}

// ═══════════════════════════════════════
//  交互
// ═══════════════════════════════════════

function bindInteractions(svg, viewport) {
  svg.addEventListener('wheel', e => {
    e.preventDefault();
    _userHasInteracted = true;
    const rect = svg.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;

    const zoomFactor = e.deltaY < 0 ? 1.12 : 1 / 1.12;
    const newScale = Math.max(0.1, Math.min(5, _transform.scale * zoomFactor));

    _transform.x = mx - (mx - _transform.x) * (newScale / _transform.scale);
    _transform.y = my - (my - _transform.y) * (newScale / _transform.scale);
    _transform.scale = newScale;
    applyTransform(svg);
  }, { passive: false });

  svg.addEventListener('mousedown', e => {
    if (e.button !== 0) return;
    const nodeG = e.target.closest('.graph-node');
    if (nodeG) {
      handleNodeClick(nodeG, svg);
      return;
    }
    _drag = { startX: e.clientX, startY: e.clientY, tx: _transform.x, ty: _transform.y };
    svg.style.cursor = 'grabbing';
  });

  if (!_boundGlobal) {
    _boundGlobal = true;
    window.addEventListener('mousemove', e => {
      if (!_drag) return;
      _userHasInteracted = true;
      _transform.x = _drag.tx + (e.clientX - _drag.startX);
      _transform.y = _drag.ty + (e.clientY - _drag.startY);
      if (_svgRoot) applyTransform(_svgRoot);
    });
    window.addEventListener('mouseup', () => {
      if (_drag) {
        _drag = null;
        if (_svgRoot) _svgRoot.style.cursor = '';
      }
    });
  }

  viewport.addEventListener('mouseover', e => {
    const nodeG = e.target.closest('.graph-node');
    if (nodeG) highlightConnected(nodeG.dataset.module, svg, true);
  });
  viewport.addEventListener('mouseout', e => {
    const nodeG = e.target.closest('.graph-node');
    if (nodeG) highlightConnected(nodeG.dataset.module, svg, false);
  });
}

function handleNodeClick(nodeG, svg) {
  const name = nodeG.dataset.module;
  svg.querySelectorAll('.graph-node.selected').forEach(n => n.classList.remove('selected'));
  nodeG.classList.add('selected');
  _selectedNode = name;
  if (_onModuleClick) _onModuleClick(name, _topoData);
}

function highlightConnected(moduleName, svg, on) {
  if (!_topoData) return;
  const neighbors = new Set();
  for (const e of _visibleEdges) {
    if (e.from === moduleName) neighbors.add(e.to);
    if (e.to === moduleName) neighbors.add(e.from);
  }

  svg.querySelectorAll('.graph-node').forEach(n => {
    const id = n.dataset.module;
    if (id === moduleName) return;
    if (on) {
      n.classList.toggle('connected', neighbors.has(id));
      n.classList.toggle('dimmed', !neighbors.has(id));
    } else {
      n.classList.remove('connected', 'dimmed');
    }
  });

  svg.querySelectorAll('.graph-edge').forEach(e => {
    if (!on) {
      e.classList.remove('dimmed-edge', 'highlight-edge');
      return;
    }
    const from = e.dataset.from;
    const to = e.dataset.to;
    const isIncident = from === moduleName || to === moduleName;
    e.classList.toggle('dimmed-edge', !isIncident);
    e.classList.toggle('highlight-edge', isIncident);
  });
}

export function resetGraphView() {
  _userHasInteracted = false;
  if (_svgRoot?._laid) fitView(_svgRoot, _svgRoot._laid);
}
