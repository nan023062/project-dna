/**
 * 图形拓扑渲染器 — 基于 ELK.js 布局 + SVG 渲染
 *
 * 目标：
 * - 平滑缩放 / 适中速度
 * - 无断头连线（保证起点终点）
 * - 支持依赖/组合/聚合/父子/协作关系可视化
 * - 节点手动拖拽
 * - 组合节点双击钻取当前层级，可返回上一级/全貌
 * - 折叠视图下，跨层连线自动锚定到可见父节点
 */

const NODE_W = 200;
const NODE_H = 48;
const GROUP_PAD = { top: 42, left: 20, bottom: 20, right: 20 };
const DEPT_NODE_PREFIX = '__dept__:';
const ENTRY_NODE_PREFIX = '__entry__:';
const EXIT_NODE_PREFIX = '__exit__:';

const EDGE_COLORS = {
  dependency: '#4ade80',
  collaboration: '#60a5fa',
  composition: '#f59e0b',
  aggregation: '#fbbf24',
  parentChild: '#a78bfa'
};

let _elk = null;
let _svgRoot = null;
let _containerEl = null;
let _topoData = null;
let _onModuleClick = null;
let _onEdgeClick = null;

let _transform = { x: 0, y: 0, scale: 1 };
let _targetTransform = null;
let _transformRaf = 0;
let _userHasInteracted = false;

let _panDrag = null;
let _nodeDrag = null;
let _boundGlobal = false;

let _selectedNode = null;
let _selectedEdgeKey = null;
let _viewRoot = null;

let _lastFingerprint = null;
let _renderToken = 0;

let _relationFilter = {
  dependency: true,
  composition: true,
  aggregation: true,
  parentChild: true,
  collaboration: true
};

let _drawState = {
  nodeBaseRects: new Map(),
  nodeEls: new Map(),
  edgeEls: []
};

let _nodeOffsets = new Map();
let _renderEdges = [];
let _hierarchy = {
  parentByChild: new Map(),
  childrenByParent: new Map(),
  roots: []
};
let _allNodeMap = new Map();

function getElk() {
  if (_elk) return _elk;
  if (typeof ELK === 'undefined') throw new Error('ELK.js not loaded');
  _elk = new ELK();
  return _elk;
}

function clamp(v, min, max) {
  return Math.max(min, Math.min(max, v));
}

function normalizeRelationFilter(raw) {
  const defaults = {
    dependency: true,
    composition: true,
    aggregation: true,
    parentChild: true,
    collaboration: true
  };
  if (!raw) return defaults;
  return {
    dependency: raw.dependency !== false,
    composition: raw.composition !== false,
    aggregation: raw.aggregation !== false,
    parentChild: raw.parentChild !== false,
    collaboration: raw.collaboration !== false
  };
}

function normalizeContainmentKind(edge) {
  const raw = String(edge?.kind || '').trim().toLowerCase();
  if (raw === 'composition' || raw === 'aggregation') return raw;
  if (edge?.isComputed) return 'aggregation';
  return 'composition';
}

function toRelationKey(edge) {
  const relation = edge?.relation || 'dependency';
  if (relation === 'dependency') return 'dependency';
  if (relation === 'collaboration') return 'collaboration';
  if (relation === 'containment') {
    const kind = normalizeContainmentKind(edge);
    return kind === 'aggregation' ? 'aggregation' : 'composition';
  }
  return 'dependency';
}

function shouldKeepEdge(edge, filter) {
  const relation = edge?.relation || 'dependency';
  if (relation === 'dependency') return !!filter.dependency;
  if (relation === 'collaboration') return !!filter.collaboration;
  if (relation === 'containment') {
    const kind = normalizeContainmentKind(edge);
    if (kind === 'aggregation') return !!filter.aggregation || !!filter.parentChild;
    return !!filter.composition || !!filter.parentChild;
  }
  return true;
}

function getAllRelationEdges(topo) {
  if (!topo) return [];

  if (Array.isArray(topo.relationEdges) && topo.relationEdges.length > 0) {
    return topo.relationEdges.map(e => ({
      from: e.from,
      to: e.to,
      relation: e.relation || 'dependency',
      kind: e.kind,
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
    kind: e.kind,
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

function toDepartmentNodeId(disciplineId) {
  return `${DEPT_NODE_PREFIX}${disciplineId}`;
}

function isDepartmentNodeId(nodeId) {
  return typeof nodeId === 'string' && nodeId.startsWith(DEPT_NODE_PREFIX);
}

function isScopeGatewayNodeId(nodeId) {
  return typeof nodeId === 'string' &&
    (nodeId.startsWith(ENTRY_NODE_PREFIX) || nodeId.startsWith(EXIT_NODE_PREFIX));
}

function buildSyntheticDepartmentNodes(topo, modules) {
  const byDiscipline = new Map();
  for (const m of modules) {
    const disciplineId = String(m.discipline || '').trim();
    if (!disciplineId || disciplineId.toLowerCase() === 'root') continue;
    if (!byDiscipline.has(disciplineId)) byDiscipline.set(disciplineId, []);
    byDiscipline.get(disciplineId).push(m);
  }

  const displayNameMap = new Map();
  for (const d of topo.disciplines || []) {
    const disciplineId = String(d.id || '').trim();
    if (!disciplineId || disciplineId.toLowerCase() === 'root') continue;
    const displayName = String(d.displayName || disciplineId).trim() || disciplineId;
    displayNameMap.set(disciplineId, displayName);
  }

  const deptNodes = [];
  for (const [disciplineId, members] of byDiscipline) {
    deptNodes.push({
      name: toDepartmentNodeId(disciplineId),
      displayName: displayNameMap.get(disciplineId) || members[0]?.disciplineDisplayName || disciplineId,
      nodeId: toDepartmentNodeId(disciplineId),
      discipline: '__department__',
      disciplineDisplayName: '部门',
      type: 'Department',
      typeName: 'Department',
      typeLabel: '部门',
      summary: `${displayNameMap.get(disciplineId) || disciplineId}：父级治理节点`,
      isSyntheticDepartment: true
    });
  }

  deptNodes.sort((a, b) => String(a.displayName || a.name).localeCompare(String(b.displayName || b.name), 'zh-CN'));
  return deptNodes;
}

function buildDepartmentNodes(topo, modules) {
  const byDiscipline = new Map();
  for (const m of modules) {
    const disciplineId = String(m.discipline || '').trim();
    if (!disciplineId || disciplineId.toLowerCase() === 'root') continue;
    if (!byDiscipline.has(disciplineId)) byDiscipline.set(disciplineId, []);
    byDiscipline.get(disciplineId).push(m);
  }

  const nodes = new Map();
  for (const d of topo.disciplines || []) {
    const disciplineId = String(d.id || '').trim();
    if (!disciplineId || disciplineId.toLowerCase() === 'root') continue;
    const displayName = String(d.displayName || disciplineId).trim() || disciplineId;
    nodes.set(disciplineId, {
      name: toDepartmentNodeId(disciplineId),
      displayName,
      nodeId: toDepartmentNodeId(disciplineId),
      discipline: disciplineId,
      disciplineDisplayName: 'Department',
      type: 'Department',
      typeName: 'Department',
      typeLabel: 'Department',
      summary: `${displayName}: project department node`,
      isDepartmentNode: true,
      isSyntheticDepartment: true
    });
  }

  for (const [disciplineId, members] of byDiscipline) {
    if (nodes.has(disciplineId)) continue;
    const displayName = members[0]?.disciplineDisplayName || disciplineId;
    nodes.set(disciplineId, {
      name: toDepartmentNodeId(disciplineId),
      displayName,
      nodeId: toDepartmentNodeId(disciplineId),
      discipline: disciplineId,
      disciplineDisplayName: 'Department',
      type: 'Department',
      typeName: 'Department',
      typeLabel: 'Department',
      summary: `${displayName}: project department node`,
      isDepartmentNode: true,
      isSyntheticDepartment: true
    });
  }

  const departmentNodes = [...nodes.values()];
  departmentNodes.sort((a, b) => String(a.displayName || a.name).localeCompare(String(b.displayName || b.name), 'zh-CN'));
  return departmentNodes;
}

function buildProjectNode(topo) {
  const project = topo?.project;
  if (!project || typeof project !== 'object') return null;

  const nodeId = String(project.id || 'project').trim() || 'project';
  return {
    name: nodeId,
    displayName: String(project.name || 'Project').trim() || 'Project',
    nodeId,
    discipline: 'root',
    disciplineDisplayName: '项目',
    type: 'Project',
    typeName: 'Project',
    typeLabel: '项目',
    summary: String(project.summary || '项目根节点：进入后查看部门层级。').trim() || '项目根节点：进入后查看部门层级。',
    isProjectNode: true,
    isSyntheticProject: true
  };
}

function isProjectNodeId(nodeId) {
  const node = _allNodeMap.get(nodeId);
  return String(node?.type || '').trim().toLowerCase() === 'project';
}

function getScopedChildIds(parentId, nodeIdSet) {
  const children = [...(_hierarchy.childrenByParent.get(parentId) || [])]
    .filter(id => nodeIdSet.has(id));

  if (!isProjectNodeId(parentId)) return children;

  const departmentChildren = children.filter(id => {
    if (isDepartmentNodeId(id)) return true;
    const child = _allNodeMap.get(id);
    return String(child?.type || '').trim().toLowerCase() === 'department';
  });

  return departmentChildren.length > 0 ? departmentChildren : children;
}

function buildHierarchy(nodeIds, containmentEdges) {
  const parentByChild = new Map();
  const childrenByParent = new Map();
  const nodeIdSet = new Set(nodeIds);

  const containment = containmentEdges
    .filter(e => e.relation === 'containment' && nodeIdSet.has(e.from) && nodeIdSet.has(e.to) && e.from !== e.to)
    .sort((a, b) => {
      const aPriority = ePriority(a);
      const bPriority = ePriority(b);
      if (aPriority !== bPriority) return aPriority - bPriority;
      return 0;
    });

  for (const edge of containment) {
    if (!parentByChild.has(edge.to)) {
      parentByChild.set(edge.to, edge.from);
    }

    if (!childrenByParent.has(edge.from)) childrenByParent.set(edge.from, new Set());
    childrenByParent.get(edge.from).add(edge.to);
  }

  const roots = [];
  for (const id of nodeIdSet) {
    if (!parentByChild.has(id)) roots.push(id);
  }

  if (roots.length === 0) roots.push(...nodeIdSet);

  return {
    parentByChild,
    childrenByParent,
    roots
  };
}

function ePriority(edge) {
  if (edge?.isDepartmentEdge) return 30;
  if (edge?.relation !== 'containment') return 50;
  const kind = normalizeContainmentKind(edge);
  if (kind === 'composition') return 10;
  if (kind === 'aggregation') return 20;
  return 25;
}

function getVisibleNodeIds(allIds) {
  const idSet = new Set(allIds);

  if (_viewRoot && !idSet.has(_viewRoot)) {
    _viewRoot = null;
  }

  if (!_viewRoot) {
    const projectRoots = _hierarchy.roots.filter(id => isProjectNodeId(id));
    if (projectRoots.length > 0) return projectRoots;
    return _hierarchy.roots.filter(id => idSet.has(id));
  }

  const children = getScopedChildIds(_viewRoot, idSet);

  // Unity 子状态机视角：进入后仅显示内部层级，不再显示父节点本体。
  if (children.length === 0) return [_viewRoot];
  return children;
}

function collapseToVisible(nodeId, visibleSet) {
  let cur = nodeId;
  let guard = 0;

  while (cur && !visibleSet.has(cur) && guard < 64) {
    cur = _hierarchy.parentByChild.get(cur) || null;
    guard++;
  }

  if (cur && visibleSet.has(cur)) return cur;
  if (_viewRoot && visibleSet.has(_viewRoot)) return _viewRoot;
  return null;
}

function buildRenderModel(topo) {
  const modules = topo.modules || [];
  const projectNode = buildProjectNode(topo);
  const departmentNodes = buildDepartmentNodes(topo, modules);
  const allNodes = [...(projectNode ? [projectNode] : []), ...departmentNodes, ...modules];
  _allNodeMap = new Map(allNodes.map(m => [m.name, m]));

  const allEdges = getAllRelationEdges(topo);
  const explicitContainmentKeys = new Set(
    allEdges
      .filter(e => e.relation === 'containment')
      .map(e => `${e.from}|${e.to}`)
  );
  const departmentContainmentEdges = [];
  for (const module of modules) {
    const disciplineId = String(module.discipline || '').trim();
    if (!disciplineId || disciplineId.toLowerCase() === 'root') continue;
    if (module.parentModuleId || module.parentId) continue;
    const deptId = toDepartmentNodeId(disciplineId);
    if (!_allNodeMap.has(deptId)) continue;
    const edgeKey = `${deptId}|${module.name}`;
    if (explicitContainmentKeys.has(edgeKey)) continue;
    departmentContainmentEdges.push({
      from: deptId,
      to: module.name,
      relation: 'containment',
      kind: 'composition',
      isComputed: false,
      isDepartmentEdge: true
    });
  }

  const allContainmentEdges = allEdges
    .concat(departmentContainmentEdges)
    .filter(e => e.relation === 'containment');

  _hierarchy = buildHierarchy(allNodes.map(m => m.name), allContainmentEdges);

  const visibleNodeIds = getVisibleNodeIds(allNodes.map(m => m.name));
  const visibleSet = new Set(visibleNodeIds);
  const inDetailScope = !!_viewRoot && visibleNodeIds.length > 0;

  const scopeChildren = inDetailScope ? [...visibleNodeIds] : [];
  let gatewayNodes = [];
  let entryId = null;
  let exitId = null;
  if (inDetailScope) {
    const scopeNode = _allNodeMap.get(_viewRoot);
    const discipline = scopeChildren
      .map(id => _allNodeMap.get(id)?.discipline)
      .find(Boolean) || scopeNode?.discipline || '__gateway__';
    const disciplineDisplayName = scopeNode?.displayName || '子状态机';
    const scopeKey = String(_viewRoot);
    entryId = `${ENTRY_NODE_PREFIX}${scopeKey}`;
    exitId = `${EXIT_NODE_PREFIX}${scopeKey}`;

    gatewayNodes.push({
      name: entryId,
      displayName: 'ENTRY',
      nodeId: entryId,
      discipline,
      disciplineDisplayName,
      type: 'Gateway',
      typeName: 'Gateway',
      typeLabel: '入口',
      isScopeGateway: true,
      gatewayKind: 'entry'
    });
    gatewayNodes.push({
      name: exitId,
      displayName: 'EXIT',
      nodeId: exitId,
      discipline,
      disciplineDisplayName,
      type: 'Gateway',
      typeName: 'Gateway',
      typeLabel: '出口',
      isScopeGateway: true,
      gatewayKind: 'exit'
    });
  }

  const visibleModules = visibleNodeIds
    .map(id => _allNodeMap.get(id))
    .filter(Boolean)
    .concat(gatewayNodes);

  const mergedEdges = new Map();
  const mergedSourceEdges = allEdges.concat(departmentContainmentEdges);
  const visibleWithGateway = new Set(visibleModules.map(m => m.name));
  let entryUsed = false;
  let exitUsed = false;

  for (const edge of mergedSourceEdges) {
    if (!shouldKeepEdge(edge, _relationFilter)) continue;
    let from = collapseToVisible(edge.from, visibleSet);
    let to = collapseToVisible(edge.to, visibleSet);

    if (inDetailScope) {
      const fromInScope = visibleSet.has(edge.from);
      const toInScope = visibleSet.has(edge.to);

      if (!fromInScope && toInScope) {
        if (!entryId || edge.relation === 'containment') continue;
        from = entryId;
        entryUsed = true;
      } else if (fromInScope && !toInScope) {
        if (!exitId || edge.relation === 'containment') continue;
        to = exitId;
        exitUsed = true;
      } else if (!fromInScope && !toInScope) {
        continue;
      }
    }

    if (!from || !to || from === to) continue;
    if (!visibleWithGateway.has(from) || !visibleWithGateway.has(to)) continue;

    const relationKey = toRelationKey(edge);
    const key = `${relationKey}|${from}|${to}`;

    if (!mergedEdges.has(key)) {
      mergedEdges.set(key, {
        id: key,
        from,
        to,
        relation: edge.relation || 'dependency',
        relationKey,
        kind: edge.kind,
        isComputed: !!edge.isComputed,
        sources: new Set([edge.from]),
        targets: new Set([edge.to]),
        crossWorkNames: new Set(edge.crossWorkNames || [])
      });
    } else {
      const item = mergedEdges.get(key);
      item.isComputed = item.isComputed && !!edge.isComputed;
      item.sources.add(edge.from);
      item.targets.add(edge.to);
      for (const n of edge.crossWorkNames || []) item.crossWorkNames.add(n);
    }
  }

  const visibleEdges = [...mergedEdges.values()].map(e => ({
    ...e,
    sources: [...e.sources],
    targets: [...e.targets],
    crossWorkNames: [...e.crossWorkNames]
  }));

  if (inDetailScope) {
    gatewayNodes = gatewayNodes.filter(n => {
      if (n.name === entryId) return entryUsed;
      if (n.name === exitId) return exitUsed;
      return true;
    });
  }

  const finalModules = visibleNodeIds
    .map(id => _allNodeMap.get(id))
    .filter(Boolean)
    .concat(gatewayNodes);
  const finalNodeIdSet = new Set(finalModules.map(m => m.name));
  const finalEdges = visibleEdges.filter(e => finalNodeIdSet.has(e.from) && finalNodeIdSet.has(e.to));

  return {
    modules: finalModules,
    edges: finalEdges,
    visibleSet,
    allNodes
  };
}

function topoFingerprint(topo, renderModel) {
  const mods = (renderModel.modules || []).map(m => m.name).sort().join(',');
  const rels = (renderModel.edges || [])
    .map(e => `${e.relationKey}:${e.from}>${e.to}`)
    .sort()
    .join(',');
  const filter = `${_relationFilter.dependency ? 1 : 0}${_relationFilter.composition ? 1 : 0}${_relationFilter.aggregation ? 1 : 0}${_relationFilter.parentChild ? 1 : 0}${_relationFilter.collaboration ? 1 : 0}`;
  const root = _viewRoot || '::root::';
  const allMods = (renderModel.allNodes || topo.modules || []).map(m => m.name).sort().join(',');
  return `${allMods}|${mods}|${rels}|${filter}|${root}`;
}

function pruneNodeOffsets(allModuleIds) {
  const idSet = new Set(allModuleIds);
  for (const id of [..._nodeOffsets.keys()]) {
    if (!idSet.has(id)) _nodeOffsets.delete(id);
  }
}

export async function renderGraph(container, topoData, opts = {}) {
  _containerEl = container;
  _topoData = topoData;
  _onModuleClick = opts.onModuleClick || null;
  _onEdgeClick = opts.onEdgeClick || null;
  _relationFilter = normalizeRelationFilter(opts.relationFilter);

  if (!topoData?.modules?.length) {
    _lastFingerprint = null;
    _viewRoot = null;
    container.innerHTML = '<div class="graph-empty">暂无图节点 — AI Agent 通过 MCP 注册后此处自动显示</div>';
    return;
  }

  const renderModel = buildRenderModel(topoData);
  pruneNodeOffsets((renderModel.allNodes || []).map(m => m.name));
  _renderEdges = renderModel.edges;

  if (!renderModel.modules.length) {
    _lastFingerprint = null;
    container.innerHTML = '<div class="graph-empty">当前层级暂无可显示节点</div>';
    renderNav(container);
    return;
  }

  const fp = topoFingerprint(topoData, renderModel);
  if (fp === _lastFingerprint && _svgRoot && container.contains(_svgRoot)) {
    renderNav(container);
    return;
  }

  const token = ++_renderToken;
  _lastFingerprint = fp;

  const savedTransform = _userHasInteracted ? { ..._transform } : null;
  const savedSelection = _selectedNode;
  const savedEdgeSelection = _selectedEdgeKey;

  if (!_svgRoot || !container.contains(_svgRoot)) {
    container.innerHTML = '<div class="graph-loading">布局计算中…</div>';
  }

  try {
    const cw = container.clientWidth || container.getBoundingClientRect().width || 800;
    const ch = container.clientHeight || container.getBoundingClientRect().height || 600;

    const elkGraph = toElkGraph(renderModel.modules, renderModel.edges, cw / Math.max(ch, 1));
    const laid = await getElk().layout(elkGraph);

    if (token !== _renderToken) return;

    container.innerHTML = '';
    renderSvg(container, laid, renderModel, savedTransform, savedSelection, savedEdgeSelection);
    renderNav(container);
  } catch (e) {
    console.error('Graph render error', e);
    container.innerHTML = `<div class="graph-empty">图形渲染失败: ${e.message}</div>`;
    renderNav(container);
  }
}

function toElkGraph(modules, edges, containerAspectRatio = 1.6) {
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
      const isDepartment = !!m.isDepartmentNode || !!m.isSyntheticDepartment;
      const isGateway = !!m.isScopeGateway;
      return {
        id: m.name,
        width: isGateway ? 96 : (isDepartment ? 230 : NODE_W),
        height: isGateway ? 36 : (isDepartment ? 52 : NODE_H),
        labels: [{ text: m.displayName || m.name }],
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
  let idx = 0;
  for (const edge of edges) {
    if (!nodeIdSet.has(edge.from) || !nodeIdSet.has(edge.to)) continue;

    elkEdges.push({
      id: `e_${idx++}_${edge.relationKey}_${edge.from}_${edge.to}`,
      sources: [edge.from],
      targets: [edge.to],
      _edge: edge
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
      'elk.spacing.edgeNode': '28',
      'elk.spacing.componentComponent': '60',
      'elk.layered.spacing.nodeNodeBetweenLayers': '78',
      'elk.layered.spacing.edgeNodeBetweenLayers': '32',
      'elk.layered.crossingMinimization.strategy': 'LAYER_SWEEP',
      'elk.edgeRouting': 'POLYLINE',
      'elk.hierarchyHandling': 'INCLUDE_CHILDREN',
      'elk.separateConnectedComponents': 'true',
      'elk.aspectRatio': String(containerAspectRatio.toFixed(2))
    }
  };
}

function renderSvg(container, laid, renderModel, savedTransform, savedSelection, savedEdgeSelection) {
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
    <marker id="arrow-containment" viewBox="0 0 10 6" refX="10" refY="3" markerWidth="8" markerHeight="6" orient="auto-start-reverse">
      <path d="M0,0 L10,3 L0,6" fill="${EDGE_COLORS.composition}" />
    </marker>
    <marker id="diamond-filled" viewBox="0 0 12 8" refX="1" refY="4" markerWidth="10" markerHeight="8" orient="auto">
      <path d="M1,4 L5,1 L9,4 L5,7 Z" fill="${EDGE_COLORS.composition}" />
    </marker>
    <marker id="diamond-hollow" viewBox="0 0 12 8" refX="1" refY="4" markerWidth="10" markerHeight="8" orient="auto">
      <path d="M1,4 L5,1 L9,4 L5,7 Z" fill="#08081a" stroke="${EDGE_COLORS.aggregation}" stroke-width="1" />
    </marker>
  `;
  svg.appendChild(defs);

  const viewport = document.createElementNS(svgNs, 'g');
  viewport.classList.add('graph-viewport');
  svg.appendChild(viewport);

  drawGroups(viewport, laid.children || [], svgNs);

  _drawState = {
    nodeBaseRects: new Map(),
    nodeEls: new Map(),
    edgeEls: []
  };

  drawEdges(viewport, laid, svgNs);
  drawNodes(viewport, laid.children || [], svgNs, renderModel);

  container.appendChild(svg);
  _svgRoot = svg;
  svg._laid = laid;

  applyNodeOffsets(svg);
  updateEdgePaths(svg);
  bindInteractions(svg, viewport);

  if (savedTransform) {
    _transform = savedTransform;
    _targetTransform = null;
    applyTransform(svg);
  } else {
    requestAnimationFrame(() => {
      requestAnimationFrame(() => fitView(svg, laid));
    });
  }

  if (savedSelection && _drawState.nodeEls.has(savedSelection)) {
    const nodeG = _drawState.nodeEls.get(savedSelection);
    nodeG.classList.add('selected');
    _selectedNode = savedSelection;
  } else if (_selectedNode && !_drawState.nodeEls.has(_selectedNode)) {
    _selectedNode = null;
  }

  if (savedEdgeSelection) {
    const edgeItem = _drawState.edgeEls.find(x => x.edge?.id === savedEdgeSelection);
    if (edgeItem) {
      setEdgeSelected(edgeItem.edge.id, false);
    } else {
      _selectedEdgeKey = null;
    }
  }
}

function drawGroups(viewport, groups, ns) {
  // 进入子层（部门/子状态机）后，隐藏外围分组框，避免与导航层级标题重复。
  if (_viewRoot) return;

  for (const g of groups) {
    if (!g._discipline) continue;

    const x = g.x || 0;
    const y = g.y || 0;
    const w = g.width || 200;
    const h = g.height || 100;

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

function drawNodes(viewport, groups, ns, renderModel) {
  const moduleMap = new Map((renderModel.modules || []).map(m => [m.name, m]));

  for (const g of groups) {
    const gx = g.x || 0;
    const gy = g.y || 0;

    for (const node of g.children || []) {
      const nx = gx + (node.x || 0);
      const ny = gy + (node.y || 0);
      const w = node.width || NODE_W;
      const h = node.height || NODE_H;
      const m = moduleMap.get(node.id) || node._module;

      const group = document.createElementNS(ns, 'g');
      group.classList.add('graph-node');
      group.dataset.module = node.id;
      if (m?.isDepartmentNode || m?.isSyntheticDepartment) group.classList.add('graph-node-department');
      if (m?.isScopeGateway) group.classList.add('graph-node-gateway');

      const hasChildren = (_hierarchy.childrenByParent.get(node.id)?.size || 0) > 0;
      const isContext = !!_viewRoot && node.id === _viewRoot;

      if (hasChildren && !m?.isScopeGateway) group.dataset.drillable = '1';
      if (isContext) group.classList.add('graph-node-context');

      const rect = document.createElementNS(ns, 'rect');
      rect.setAttribute('x', nx);
      rect.setAttribute('y', ny);
      rect.setAttribute('width', w);
      rect.setAttribute('height', h);
      rect.setAttribute('rx', '8');
      rect.classList.add('graph-node-rect', `layer-${m?.computedLayer ?? m?.layer ?? 0}`);
      group.appendChild(rect);

      const text = document.createElementNS(ns, 'text');
      text.setAttribute('x', nx + w / 2);
      text.setAttribute('y', ny + h / 2 + 1);
      text.classList.add('graph-node-text');
      const displayName = m?.displayName || node.id;
      const limit = m?.isScopeGateway ? 12 : 20;
      text.textContent = displayName.length > limit ? `${displayName.slice(0, limit - 1)}…` : displayName;
      group.appendChild(text);

      if (hasChildren && !m?.isScopeGateway) {
        const badge = document.createElementNS(ns, 'text');
        badge.setAttribute('x', nx + w - 10);
        badge.setAttribute('y', ny + 14);
        badge.classList.add('graph-node-composite');
        badge.textContent = '◎';
        group.appendChild(badge);
      }

      if (m?.computedLayer != null) {
        const layerBadge = document.createElementNS(ns, 'text');
        layerBadge.setAttribute('x', nx + 8);
        layerBadge.setAttribute('y', ny + 14);
        layerBadge.classList.add('graph-layer-badge');
        layerBadge.textContent = `L${m.computedLayer}`;
        group.appendChild(layerBadge);
      }

      viewport.appendChild(group);
      _drawState.nodeEls.set(node.id, group);
      _drawState.nodeBaseRects.set(node.id, { x: nx, y: ny, w, h });
    }
  }
}

function drawEdges(viewport, laid, ns) {
  const elkEdges = laid.edges || [];

  for (const e of elkEdges) {
    const edge = e._edge;
    if (!edge) continue;

    const hitPath = document.createElementNS(ns, 'path');
    hitPath.classList.add('graph-edge-hit');
    hitPath.dataset.from = edge.from;
    hitPath.dataset.to = edge.to;
    hitPath.dataset.relation = edge.relationKey;
    hitPath.dataset.edgeId = edge.id || '';
    viewport.appendChild(hitPath);

    const path = document.createElementNS(ns, 'path');
    path.classList.add('graph-edge', `graph-edge-${edge.relationKey}`);
    path.dataset.from = edge.from;
    path.dataset.to = edge.to;
    path.dataset.relation = edge.relationKey;
    path.dataset.edgeId = edge.id || '';

    applyEdgeStyle(path, edge);
    viewport.appendChild(path);

    const item = { el: path, hitEl: hitPath, edge };
    bindEdgeInteractions(item);
    _drawState.edgeEls.push(item);
  }
}

function applyEdgeStyle(path, edge) {
  const key = edge.relationKey;
  const color = EDGE_COLORS[key] || EDGE_COLORS.dependency;

  path.setAttribute('stroke', color);

  if (key === 'dependency') {
    path.setAttribute('marker-end', 'url(#arrow-dependency)');
    return;
  }

  if (key === 'collaboration') {
    path.setAttribute('marker-end', 'url(#arrow-collaboration)');
    path.setAttribute('stroke-dasharray', '8,4');
    return;
  }

  if (key === 'composition') {
    path.setAttribute('marker-start', 'url(#diamond-filled)');
    path.setAttribute('marker-end', 'url(#arrow-containment)');
    return;
  }

  if (key === 'aggregation') {
    path.setAttribute('marker-start', 'url(#diamond-hollow)');
    path.setAttribute('marker-end', 'url(#arrow-containment)');
    path.setAttribute('stroke-dasharray', '6,4');
  }
}

function bindEdgeInteractions(item) {
  const clickHandler = e => {
    e.stopPropagation();
    const edgeId = item.edge?.id;
    if (!edgeId) return;
    setEdgeSelected(edgeId, true);
  };

  item.el.addEventListener('click', clickHandler);
  if (item.hitEl) item.hitEl.addEventListener('click', clickHandler);
}

function setEdgeSelected(edgeId, notify = true) {
  _selectedEdgeKey = edgeId;
  _selectedNode = null;
  _svgRoot?.querySelectorAll('.graph-node.selected').forEach(n => n.classList.remove('selected'));

  for (const item of _drawState.edgeEls) {
    const isSelected = item.edge?.id === edgeId;
    item.el.classList.toggle('selected-edge', isSelected);
    if (item.hitEl) item.hitEl.classList.toggle('selected-edge-hit', isSelected);
  }

  if (!notify || !_onEdgeClick) return;
  const edgeItem = _drawState.edgeEls.find(x => x.edge?.id === edgeId);
  if (!edgeItem) return;

  _onEdgeClick({
    ...edgeItem.edge,
    viewRoot: _viewRoot
  });
}

function getNodeRect(id) {
  const base = _drawState.nodeBaseRects.get(id);
  if (!base) return null;
  const offset = _nodeOffsets.get(id) || { x: 0, y: 0 };
  return {
    x: base.x + offset.x,
    y: base.y + offset.y,
    w: base.w,
    h: base.h
  };
}

function getAnchors(fromRect, toRect) {
  const fromCx = fromRect.x + fromRect.w / 2;
  const fromCy = fromRect.y + fromRect.h / 2;
  const toCx = toRect.x + toRect.w / 2;
  const toCy = toRect.y + toRect.h / 2;

  const dx = toCx - fromCx;
  const dy = toCy - fromCy;

  if (Math.abs(dx) > Math.abs(dy)) {
    return {
      start: {
        x: dx >= 0 ? fromRect.x + fromRect.w : fromRect.x,
        y: fromCy
      },
      end: {
        x: dx >= 0 ? toRect.x : toRect.x + toRect.w,
        y: toCy
      },
      horizontal: true
    };
  }

  return {
    start: {
      x: fromCx,
      y: dy >= 0 ? fromRect.y + fromRect.h : fromRect.y
    },
    end: {
      x: toCx,
      y: dy >= 0 ? toRect.y : toRect.y + toRect.h
    },
    horizontal: false
  };
}

function smoothPath(start, end, horizontal) {
  if (horizontal) {
    const dx = end.x - start.x;
    const c = clamp(Math.abs(dx) * 0.45, 36, 160);
    const c1x = start.x + Math.sign(dx || 1) * c;
    const c2x = end.x - Math.sign(dx || 1) * c;
    return `M${start.x},${start.y} C${c1x},${start.y} ${c2x},${end.y} ${end.x},${end.y}`;
  }

  const dy = end.y - start.y;
  const c = clamp(Math.abs(dy) * 0.45, 36, 160);
  const c1y = start.y + Math.sign(dy || 1) * c;
  const c2y = end.y - Math.sign(dy || 1) * c;
  return `M${start.x},${start.y} C${start.x},${c1y} ${end.x},${c2y} ${end.x},${end.y}`;
}

function updateEdgePaths() {
  for (const item of _drawState.edgeEls) {
    const fromRect = getNodeRect(item.edge.from);
    const toRect = getNodeRect(item.edge.to);

    if (!fromRect || !toRect) {
      item.el.setAttribute('d', '');
      if (item.hitEl) item.hitEl.setAttribute('d', '');
      continue;
    }

    const { start, end, horizontal } = getAnchors(fromRect, toRect);
    const d = smoothPath(start, end, horizontal);
    item.el.setAttribute('d', d);
    if (item.hitEl) item.hitEl.setAttribute('d', d);
  }
}

function applyNodeOffsets() {
  for (const [id, el] of _drawState.nodeEls) {
    const offset = _nodeOffsets.get(id) || { x: 0, y: 0 };
    el.setAttribute('transform', `translate(${offset.x},${offset.y})`);
  }
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

  const scale = Math.min((containerW * 0.92) / totalW, (containerH * 0.92) / totalH, 3.0);

  _transform = {
    x: (containerW - totalW * scale) / 2,
    y: (containerH - totalH * scale) / 2,
    scale
  };

  _targetTransform = null;
  _userHasInteracted = false;
  applyTransform(svg);
}

function applyTransform(svg) {
  const vp = svg.querySelector('.graph-viewport');
  if (!vp) return;
  vp.setAttribute('transform', `translate(${_transform.x},${_transform.y}) scale(${_transform.scale})`);
}

function requestTransformAnimation() {
  if (_transformRaf) return;

  const tick = () => {
    if (!_targetTransform || !_svgRoot) {
      _transformRaf = 0;
      return;
    }

    const ease = 0.22;
    _transform.x += (_targetTransform.x - _transform.x) * ease;
    _transform.y += (_targetTransform.y - _transform.y) * ease;
    _transform.scale += (_targetTransform.scale - _transform.scale) * ease;

    applyTransform(_svgRoot);

    const done =
      Math.abs(_transform.x - _targetTransform.x) < 0.2 &&
      Math.abs(_transform.y - _targetTransform.y) < 0.2 &&
      Math.abs(_transform.scale - _targetTransform.scale) < 0.002;

    if (done) {
      _transform = { ..._targetTransform };
      _targetTransform = null;
      applyTransform(_svgRoot);
      _transformRaf = 0;
      return;
    }

    _transformRaf = requestAnimationFrame(tick);
  };

  _transformRaf = requestAnimationFrame(tick);
}

function setTargetTransform(next) {
  _targetTransform = next;
  requestTransformAnimation();
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

    const base = _targetTransform || _transform;
    const intensity = clamp(Math.abs(e.deltaY) / 700, 0.06, 0.32);
    const zoomFactor = e.deltaY < 0 ? (1 + intensity) : 1 / (1 + intensity);
    const newScale = clamp(base.scale * zoomFactor, 0.15, 5);

    const next = {
      x: mx - (mx - base.x) * (newScale / base.scale),
      y: my - (my - base.y) * (newScale / base.scale),
      scale: newScale
    };

    setTargetTransform(next);
  }, { passive: false });

  svg.addEventListener('mousedown', e => {
    if (e.button !== 0) return;

    const nodeG = e.target.closest('.graph-node');
    if (nodeG) {
      const id = nodeG.dataset.module;
      if (!id) return;

      const origin = _nodeOffsets.get(id) || { x: 0, y: 0 };
      _nodeDrag = {
        id,
        startX: e.clientX,
        startY: e.clientY,
        originX: origin.x,
        originY: origin.y,
        moved: false
      };
      return;
    }

    _panDrag = {
      startX: e.clientX,
      startY: e.clientY,
      tx: _targetTransform?.x ?? _transform.x,
      ty: _targetTransform?.y ?? _transform.y
    };

    svg.style.cursor = 'grabbing';
  });

  svg.addEventListener('dblclick', e => {
    const nodeG = e.target.closest('.graph-node');
    if (!nodeG) return;

    const id = nodeG.dataset.module;
    if (!id) return;

    const hasChildren = (_hierarchy.childrenByParent.get(id)?.size || 0) > 0;
    if (!hasChildren) return;

    _viewRoot = id;
    _lastFingerprint = null;
    renderGraph(_containerEl, _topoData, {
      relationFilter: _relationFilter,
      onModuleClick: _onModuleClick
    });
  });

  viewport.addEventListener('mouseover', e => {
    const nodeG = e.target.closest('.graph-node');
    if (!nodeG) return;
    highlightConnected(nodeG.dataset.module, true);
  });

  viewport.addEventListener('mouseout', e => {
    const nodeG = e.target.closest('.graph-node');
    if (!nodeG) return;
    highlightConnected(nodeG.dataset.module, false);
  });

  if (!_boundGlobal) {
    _boundGlobal = true;

    window.addEventListener('mousemove', e => {
      if (_nodeDrag) {
        const dx = (e.clientX - _nodeDrag.startX) / Math.max(_transform.scale, 0.0001);
        const dy = (e.clientY - _nodeDrag.startY) / Math.max(_transform.scale, 0.0001);

        if (Math.abs(dx) > 1 || Math.abs(dy) > 1) _nodeDrag.moved = true;

        _nodeOffsets.set(_nodeDrag.id, {
          x: _nodeDrag.originX + dx,
          y: _nodeDrag.originY + dy
        });

        _userHasInteracted = true;
        applyNodeOffsets();
        updateEdgePaths();
        return;
      }

      if (_panDrag) {
        _userHasInteracted = true;
        _targetTransform = null;
        _transform.x = _panDrag.tx + (e.clientX - _panDrag.startX);
        _transform.y = _panDrag.ty + (e.clientY - _panDrag.startY);
        if (_svgRoot) applyTransform(_svgRoot);
      }
    });

    window.addEventListener('mouseup', () => {
      if (_nodeDrag) {
        if (!_nodeDrag.moved) {
          const node = _drawState.nodeEls.get(_nodeDrag.id);
          if (node) handleNodeClick(node);
        }
        _nodeDrag = null;
      }

      if (_panDrag) {
        _panDrag = null;
        if (_svgRoot) _svgRoot.style.cursor = '';
      }
    });
  }
}

function handleNodeClick(nodeG) {
  const name = nodeG.dataset.module;
  if (!name) return;
  if (isScopeGatewayNodeId(name)) return;

  _selectedEdgeKey = null;
  _drawState.edgeEls.forEach(item => {
    item.el.classList.remove('selected-edge');
    if (item.hitEl) item.hitEl.classList.remove('selected-edge-hit');
  });

  _svgRoot?.querySelectorAll('.graph-node.selected').forEach(n => n.classList.remove('selected'));
  nodeG.classList.add('selected');
  _selectedNode = name;

  if (_onModuleClick) _onModuleClick(name, _topoData);
}

function highlightConnected(moduleName, on) {
  if (!_svgRoot || !_topoData) return;

  const neighbors = new Set();
  for (const e of _renderEdges) {
    if (e.from === moduleName) neighbors.add(e.to);
    if (e.to === moduleName) neighbors.add(e.from);
  }

  _svgRoot.querySelectorAll('.graph-node').forEach(n => {
    const id = n.dataset.module;
    if (id === moduleName) return;

    if (!on) {
      n.classList.remove('connected', 'dimmed');
      return;
    }

    const isNeighbor = neighbors.has(id);
    n.classList.toggle('connected', isNeighbor);
    n.classList.toggle('dimmed', !isNeighbor);
  });

  _svgRoot.querySelectorAll('.graph-edge').forEach(path => {
    if (!on) {
      path.classList.remove('dimmed-edge', 'highlight-edge');
      return;
    }

    const from = path.dataset.from;
    const to = path.dataset.to;
    const incident = from === moduleName || to === moduleName;
    path.classList.toggle('highlight-edge', incident);
    path.classList.toggle('dimmed-edge', !incident);
  });
}

// ═══════════════════════════════════════
//  层级导航
// ═══════════════════════════════════════

function buildTrail(rootId) {
  if (!rootId) return [];

  const trail = [];
  let cur = rootId;
  let guard = 0;

  while (cur && guard < 64) {
    trail.unshift(cur);
    cur = _hierarchy.parentByChild.get(cur) || null;
    guard++;
  }

  return trail;
}

function renderNav(container) {
  let nav = container.querySelector('.graph-nav');
  if (!nav) {
    nav = document.createElement('div');
    nav.className = 'graph-nav';
    container.appendChild(nav);
  }

  const byName = new Map([..._allNodeMap.entries()].map(([id, node]) => [id, node?.displayName || node?.name || id]));
  const trail = buildTrail(_viewRoot);
  const trailText = trail.length
    ? trail.map(id => byName.get(id) || id).join(' / ')
    : '全貌';

  const parent = _viewRoot ? (_hierarchy.parentByChild.get(_viewRoot) || null) : null;
  const canGoUp = !!_viewRoot;

  nav.innerHTML = `
    <button class="graph-nav-btn" data-nav="home">全貌</button>
    <button class="graph-nav-btn" data-nav="up" ${canGoUp ? '' : 'disabled'}>上一级</button>
    <span class="graph-nav-path">层级：${escapeHtml(trailText)}</span>
    ${_viewRoot ? '<span class="graph-nav-hint">ENTRY=外部进入当前层，EXIT=当前层流向外部</span>' : ''}
  `;

  nav.onclick = e => {
    const btn = e.target.closest('[data-nav]');
    if (!btn) return;

    const action = btn.dataset.nav;
    if (action === 'home') {
      _viewRoot = null;
    } else if (action === 'up') {
      _viewRoot = _viewRoot ? (_hierarchy.parentByChild.get(_viewRoot) || null) : null;
    }

    _lastFingerprint = null;
    renderGraph(_containerEl, _topoData, {
      relationFilter: _relationFilter,
      onModuleClick: _onModuleClick
    });
  };
}

function escapeHtml(input) {
  return String(input ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

export function resetGraphView() {
  _viewRoot = null;
  _userHasInteracted = false;

  if (_svgRoot?._laid) {
    fitView(_svgRoot, _svgRoot._laid);
    return;
  }

  if (_containerEl && _topoData) {
    _lastFingerprint = null;
    renderGraph(_containerEl, _topoData, {
      relationFilter: _relationFilter,
      onModuleClick: _onModuleClick
    });
  }
}
