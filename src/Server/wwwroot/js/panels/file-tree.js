import { $, api, escapeHtml } from '../utils.js';
import { openRegisteredEditSidebar } from '../app-runtime.js';
import { showTooltip, hideTooltip } from '../widgets/tooltip.js';

let _roots = null;
let _expandedPaths = new Set();
let _loadedPaths = new Set();
const FILE_TREE_STATE_KEY = 'dna:file-tree-state:v1';
const MAX_CACHED_PATHS = 2000;

export async function loadFileTree(options = {}) {
  const preserveState = options.preserveState !== false;
  const showLoading = options.showLoading !== false;
  const container = $('fileTreeContent');
  if (!container) return;

  const snapshot = preserveState ? snapshotTreeState(container) : null;
  const hasRuntimeState = Boolean(
    preserveState &&
    snapshot &&
    (_expandedPaths.size > 0 || _loadedPaths.size > 0 || container.querySelector('.tree-node'))
  );
  const cachedState = preserveState && !hasRuntimeState ? loadCachedState() : null;
  const stateToUse = hasRuntimeState ? snapshot : cachedState;
  if (showLoading && !_roots) {
    container.innerHTML = '<div class="empty" style="padding:16px;">扫描中…</div>';
  }

  try {
    const data = await api('/files/tree');
    _roots = data.roots || [];

    if (!preserveState) {
      _expandedPaths.clear();
      _loadedPaths.clear();
      renderTree();
      persistState(container);
      return;
    }

    if (stateToUse) {
      _expandedPaths = normalizePathSet(stateToUse.expandedPaths);
      _loadedPaths = normalizePathSet(stateToUse.loadedPaths);
      await hydrateExpandedChildren();
    } else {
      _expandedPaths.clear();
      _loadedPaths.clear();
    }

    renderTree();
    if (stateToUse) restoreTreeState(container, stateToUse);
    persistState(container);
  } catch (err) {
    container.innerHTML = `<div class="empty error" style="padding:16px;">扫描失败: ${escapeHtml(err.message || String(err))}</div>`;
  }
}

export async function refreshFileTree() {
  await loadFileTree({ preserveState: true, showLoading: false });
}

function renderTree() {
  const container = $('fileTreeContent');
  if (!container || !_roots) return;

  if (_roots.length === 0) {
    container.innerHTML = '<div class="empty" style="padding:16px;">未发现目录。请先注册模块，或在设置中配置扫描根目录。</div>';
    return;
  }

  renderTreeIncremental(container, _roots);
}

function buildTreeNode(node) {
  const el = document.createElement('div');
  el.className = 'tree-node';

  const hasChildren = node.hasChildren || (node.children && node.children.length > 0);
  const isExpanded = _expandedPaths.has(node.path);
  const statusKey = (node.status || 'candidate').toString().toLowerCase();

  const row = document.createElement('div');
  row.className = `tree-row status-${statusKey}`;

  const toggleIcon = hasChildren ? (isExpanded ? '▾' : '▸') : '·';
  const displayName = node.module?.name || node.name;
  const statusText = node.statusLabel || getStatusLabel(statusKey);
  const badge = node.badge || '';

  row.innerHTML = `
    <span class="tree-icon" data-role="toggle">${toggleIcon}</span>
    <span class="tree-name" data-role="label">${escapeHtml(displayName)}</span>
    <span class="tree-kind tree-kind-${statusKey}">${escapeHtml(statusText)}</span>
    ${badge ? `<span class="tree-badge">${escapeHtml(badge)}</span>` : ''}
  `;

  bindRowTooltip(row, node, statusText, hasChildren);

  const toggleEl = row.querySelector('[data-role="toggle"]');
  const labelEl = row.querySelector('[data-role="label"]');

  if (hasChildren && toggleEl) {
    toggleEl.addEventListener('click', (e) => {
      e.stopPropagation();
      toggleExpandByPath(node.path);
    });
  }

  const actions = node.actions || {};

  if (actions.canRegister && labelEl) {
    labelEl.addEventListener('dblclick', (e) => {
      e.stopPropagation();
      openModuleEditor({
        path: node.path,
        name: node.name,
        discipline: actions.suggestedDiscipline || null,
        layer: actions.suggestedLayer ?? null
      });
    });
    row.title = hasChildren
      ? '双击名称注册模块；点击箭头展开/收起'
      : '双击名称注册模块';
  }

  if (actions.canEdit && node.module && labelEl) {
    labelEl.addEventListener('click', (e) => {
      e.stopPropagation();
      openModuleEditor({
        path: node.path,
        name: node.module.name,
        id: node.module.id,
        discipline: node.module.discipline,
        layer: node.module.layer,
        isCrossWorkModule: !!node.module.isCrossWorkModule,
        isEdit: true
      });
    });
    row.title = hasChildren
      ? '点击名称编辑模块；点击箭头展开/收起'
      : '点击名称编辑模块';
  }

  el.appendChild(row);

  if (hasChildren && isExpanded && node.children) {
    const childContainer = document.createElement('div');
    childContainer.className = 'tree-children';
    for (const child of node.children) {
      childContainer.appendChild(buildTreeNode(child));
    }
    el.appendChild(childContainer);
  }

  return el;
}

async function toggleExpandByPath(path) {
  const node = getNodeByPath(path);
  if (!node) return;

  if (_expandedPaths.has(path)) {
    _expandedPaths.delete(path);
    renderTree();
    persistState($('fileTreeContent'));
    return;
  }

  _expandedPaths.add(path);

  if (!_loadedPaths.has(path)) {
    try {
      const data = await api(`/files/children?path=${encodeURIComponent(path)}`);
      node.children = data.children || [];
      _loadedPaths.add(path);

      for (const child of node.children) {
        updateNodeInRoots(child);
      }
    } catch {
      node.children = [];
    }
  }

  renderTree();
  persistState($('fileTreeContent'));
}

function updateNodeInRoots(child) {
  // no-op: children are already attached to the node object by reference
}

function renderTreeIncremental(container, roots) {
  if (container.querySelector('.empty')) {
    container.innerHTML = '';
  }
  renderNodeList(container, roots, 'root');
}

function renderNodeList(parentEl, nodes, parentKey) {
  const existingByKey = new Map();
  for (const child of Array.from(parentEl.children)) {
    const key = child.dataset?.nodeKey;
    if (key) existingByKey.set(key, child);
  }

  const usedKeys = new Set();
  for (let i = 0; i < nodes.length; i++) {
    const node = nodes[i];
    const key = getNodeKey(node, parentKey, i);
    let el = existingByKey.get(key);
    const rowSig = computeNodeRowSignature(node);

    const shouldReplace = !el || el.dataset.rowSig !== rowSig;
    if (shouldReplace) {
      const newEl = buildTreeNode(node);
      applyNodeMeta(newEl, key, rowSig);
      if (el) el.replaceWith(newEl);
      el = newEl;
    }

    const currentAtIndex = parentEl.children[i];
    if (currentAtIndex !== el) {
      parentEl.insertBefore(el, currentAtIndex || null);
    }

    syncChildrenIncremental(el, node, key);
    usedKeys.add(key);
  }

  for (const child of Array.from(parentEl.children)) {
    const key = child.dataset?.nodeKey;
    if (!key || !usedKeys.has(key)) child.remove();
  }
}

function syncChildrenIncremental(nodeEl, node, nodeKey) {
  const hasChildren = node.hasChildren || (node.children && node.children.length > 0);
  const isExpanded = _expandedPaths.has(node.path);
  const existing = nodeEl.querySelector(':scope > .tree-children');

  if (!hasChildren || !isExpanded || !Array.isArray(node.children) || node.children.length === 0) {
    if (existing) existing.remove();
    return;
  }

  const childContainer = existing || document.createElement('div');
  childContainer.className = 'tree-children';
  if (!existing) nodeEl.appendChild(childContainer);
  renderNodeList(childContainer, node.children, nodeKey);
}

function applyNodeMeta(el, key, rowSig) {
  el.dataset.nodeKey = key;
  el.dataset.rowSig = rowSig;
}

function getNodeKey(node, parentKey, index) {
  const path = String(node?.path || '').trim();
  if (path) return path;
  return `${parentKey}/${String(node?.name || 'node')}#${index}`;
}

function computeNodeRowSignature(node) {
  return JSON.stringify({
    path: node.path || '',
    name: node.name || '',
    status: node.status || '',
    statusLabel: node.statusLabel || '',
    badge: node.badge || '',
    hasChildren: Boolean(node.hasChildren || (node.children && node.children.length > 0)),
    isExpanded: _expandedPaths.has(node.path),
    module: node.module
      ? {
          id: node.module.id || '',
          name: node.module.name || '',
          discipline: node.module.discipline || '',
          layer: node.module.layer ?? null,
          isCrossWorkModule: Boolean(node.module.isCrossWorkModule)
        }
      : null
  });
}

function findNode(nodes, path) {
  if (!nodes) return null;
  for (const n of nodes) {
    if (n.path === path) return n;
    const found = findNode(n.children, path);
    if (found) return found;
  }
  return null;
}

function getNodeByPath(path) {
  return findNode(_roots, path);
}

function snapshotTreeState(container) {
  return {
    expandedPaths: new Set(_expandedPaths),
    loadedPaths: new Set(_loadedPaths),
    scrollTop: container.scrollTop
  };
}

function restoreTreeState(container, snapshot) {
  if (!snapshot) return;
  container.scrollTop = snapshot.scrollTop || 0;
}

async function hydrateExpandedChildren() {
  // 扩展路径按深度排序，优先加载父节点，再加载子节点，避免子节点找不到挂载点。
  const targets = Array.from(_expandedPaths).sort((a, b) => getPathDepth(a) - getPathDepth(b));
  for (const path of targets) {
    await ensurePathReady(path);
  }

  // 刷新后剔除不存在路径，避免缓存逐次膨胀。
  const validExpanded = new Set();
  for (const path of _expandedPaths) {
    if (findNode(_roots, path)) validExpanded.add(path);
  }
  _expandedPaths = validExpanded;

  const validLoaded = new Set();
  for (const path of _loadedPaths) {
    if (findNode(_roots, path)) validLoaded.add(path);
  }
  _loadedPaths = validLoaded;
}

async function ensurePathReady(path) {
  const node = await ensureNodeByPath(path);
  if (!node) return;
  await ensureNodeChildren(node.path);
}

async function ensureNodeByPath(path) {
  const normalized = normalizePath(path);
  if (!normalized) return null;

  const existing = findNode(_roots, normalized);
  if (existing) return existing;

  const parentPath = getParentPath(normalized);
  if (!parentPath) return null;

  const parent = await ensureNodeByPath(parentPath);
  if (!parent) return null;

  await ensureNodeChildren(parent.path || parentPath);
  return findNode(_roots, normalized);
}

async function ensureNodeChildren(path) {
  const normalized = normalizePath(path);
  if (!normalized) return;

  const node = findNode(_roots, normalized);
  if (!node) return;
  if (_loadedPaths.has(normalized)) return;
  if (!node.hasChildren && (!node.children || node.children.length === 0)) return;

  try {
    const data = await api(`/files/children?path=${encodeURIComponent(normalized)}`);
    node.children = data.children || [];
    _loadedPaths.add(normalized);
  } catch {
    node.children = node.children || [];
  }
}

function persistState(container) {
  const safeContainer = container || $('fileTreeContent');
  const payload = {
    expandedPaths: Array.from(_expandedPaths).slice(0, MAX_CACHED_PATHS),
    loadedPaths: Array.from(_loadedPaths).slice(0, MAX_CACHED_PATHS),
    scrollTop: safeContainer?.scrollTop || 0
  };
  try {
    window.localStorage?.setItem(FILE_TREE_STATE_KEY, JSON.stringify(payload));
  } catch {
    // ignore: storage may be unavailable
  }
}

function loadCachedState() {
  try {
    const raw = window.localStorage?.getItem(FILE_TREE_STATE_KEY);
    if (!raw) return null;
    const data = JSON.parse(raw);
    return {
      expandedPaths: new Set(Array.isArray(data?.expandedPaths) ? data.expandedPaths : []),
      loadedPaths: new Set(Array.isArray(data?.loadedPaths) ? data.loadedPaths : []),
      scrollTop: Number.isFinite(data?.scrollTop) ? data.scrollTop : 0
    };
  } catch {
    return null;
  }
}

function normalizePathSet(paths) {
  const normalized = new Set();
  for (const path of paths || []) {
    const value = normalizePath(path);
    if (value) normalized.add(value);
  }
  return normalized;
}

function normalizePath(path) {
  if (typeof path !== 'string') return '';
  return path.trim();
}

function getParentPath(path) {
  const normalized = normalizePath(path);
  if (!normalized) return '';
  const index = normalized.lastIndexOf('/');
  if (index <= 0) return '';
  return normalized.slice(0, index);
}

function getPathDepth(path) {
  const normalized = normalizePath(path);
  if (!normalized) return 0;
  return normalized.split('/').filter(Boolean).length;
}

function openModuleEditor(opts) {
  openRegisteredEditSidebar(opts);
}

function bindRowTooltip(row, node, statusText, hasChildren) {
  const owner = `file-tree:${node.path || node.name}`;
  row.addEventListener('mouseenter', (e) => {
    showTooltip(owner, 1, buildFolderTip(node, statusText, hasChildren), e);
  });
  row.addEventListener('mousemove', (e) => showTooltip(owner, 1, null, e));
  row.addEventListener('mouseleave', () => hideTooltip(owner));
}

function buildFolderTip(node, statusText, hasChildren) {
  const actions = node.actions || {};
  const isCW = !!node.module?.isCrossWorkModule;
  const actionText = actions.canEdit
    ? '点击名称：编辑模块'
    : actions.canRegister
      ? '双击名称：注册模块'
      : '不可编辑';

  let cwInfo = '';
  if (isCW) {
    cwInfo = `
      <div class="file-tree-tip-row"><span>角色</span><b>CrossWork 工作组</b></div>
      <div class="file-tree-tip-row"><span>特权</span><b>可访问任意普通模块</b></div>
      <div class="file-tree-tip-row"><span>约束</span><b>不被其他模块依赖 / 不访问其他工作组</b></div>
    `;
  }

  return `
    <div class="file-tree-tip">
      <div class="file-tree-tip-title">${escapeHtml(node.module?.name || node.name)}</div>
      <div class="file-tree-tip-row"><span>类型</span><b>${escapeHtml(statusText)}</b></div>
      <div class="file-tree-tip-row"><span>路径</span><code>${escapeHtml(node.path || '/')}</code></div>
      <div class="file-tree-tip-row"><span>子目录</span><b>${hasChildren ? '可展开' : '无'}</b></div>
      ${cwInfo}
      <div class="file-tree-tip-row"><span>操作</span><b>${escapeHtml(actionText)}</b></div>
    </div>
  `;
}

function getStatusLabel(statusKey) {
  switch (statusKey) {
    case 'registered': return '普通模块';
    case 'crosswork': return '工作组模块';
    case 'container': return '模块容器';
    default: return '候选目录';
  }
}
