/**
 * 树形数据构建（纯展示逻辑，不做业务计算）
 * 职责：将扁平模块列表转换为层级树结构，使用 server 提供的 layerScore 排序
 * 
 * 架构层级分数(layerScore)和依赖映射(depMap/rdepMap)由 server 计算，
 * 前端只做路径→树形的展示转换和基于 server 数据的排序。
 */

export function buildTree(modules, edges = []) {
  const root = { name: '项目根', children: [], module: null, path: '.' };

  for (const m of modules) {
    const relPath = m.relativePath.replace(/\\/g, '/');
    if (relPath === '.') { root.module = m; continue; }

    const parts = relPath.split('/').filter(Boolean);
    let node = root;

    for (let i = 0; i < parts.length; i++) {
      let child = node.children.find(c => c.name === parts[i]);
      if (!child) {
        child = { name: parts[i], children: [], module: null, path: parts.slice(0, i + 1).join('/') };
        node.children.push(child);
      }
      node = child;
    }
    node.module = m;
  }

  propagateLayerScore(root);
  sortTree(root);
  return root;
}

function propagateLayerScore(node) {
  for (const c of node.children) propagateLayerScore(c);

  if (node.module) {
    node._effectiveScore = node.module.layerScore ?? 0;
  } else {
    let max = 0;
    for (const c of node.children)
      if (c._effectiveScore > max) max = c._effectiveScore;
    node._effectiveScore = max;
  }
}

function sortTree(node) {
  node.children.sort((a, b) => {
    if (a._effectiveScore !== b._effectiveScore) return b._effectiveScore - a._effectiveScore;

    const aHasKids = a.children.length > 0 ? 0 : 1;
    const bHasKids = b.children.length > 0 ? 0 : 1;
    if (aHasKids !== bHasKids) return aHasKids - bHasKids;

    return a.name.localeCompare(b.name);
  });
  for (const c of node.children) sortTree(c);
}

export function getCrossDeps(moduleName, edges) {
  return edges.filter(e => e.from === moduleName && e.kind === 'cross');
}

export function getCycleDeps(moduleName, edges) {
  return edges.filter(e => e.from === moduleName && e.kind === 'cycle');
}
