/**
 * 拓扑面板 — 图形视图 + 关系切换 + 详情侧栏
 */

import { $ } from '../utils.js';
import { renderGraph } from '../graph/graph-renderer.js';

const DEPT_NODE_PREFIX = '__dept__:';
const ENTRY_NODE_PREFIX = '__entry__:';
const EXIT_NODE_PREFIX = '__exit__:';

const relationFilter = {
  dependency: true,
  composition: true,
  aggregation: true,
  parentChild: true,
  collaboration: true
};

let selectedModule = null;
let selectedEdge = null;

export function renderTopology(topoData) {
  const grid = $('topoGrid');
  if (grid) grid.style.display = 'none';

  const sidebar = $('archSidebar');
  if (sidebar) sidebar.style.display = 'flex';

  let gc = $('graphContainer');
  if (!gc) {
    gc = document.createElement('div');
    gc.id = 'graphContainer';
    gc.classList.add('graph-container');
    const layout = document.querySelector('#panelTopology .arch-layout');
    if (layout) layout.insertBefore(gc, layout.firstChild);
  }
  gc.style.display = 'flex';

  ensureRelationToolbar();
  bindRelationToolbar(gc, topoData, sidebar);
  renderGraphWithSidebar(gc, topoData, sidebar);
}

function renderGraphWithSidebar(graphContainer, topoData, sidebar) {
  renderGraph(graphContainer, topoData, {
    relationFilter,
    onModuleClick: moduleName => {
      selectedEdge = null;
      selectedModule = moduleName;
      renderModuleDetail(sidebar, topoData, moduleName);
    },
    onEdgeClick: edge => {
      selectedModule = null;
      selectedEdge = edge;
      renderEdgeDetail(sidebar, topoData, edge);
    }
  });

  if (selectedModule && (findModuleByName(topoData, selectedModule) || isDepartmentNodeId(selectedModule))) {
    renderModuleDetail(sidebar, topoData, selectedModule);
    return;
  }
  if (selectedEdge) {
    renderEdgeDetail(sidebar, topoData, selectedEdge);
    return;
  }
  renderOverview(sidebar, topoData);
}

function ensureRelationToolbar() {
  if (document.getElementById('topoRelationToolbar')) return;

  const summaryBar = document.querySelector('#panelTopology .summary-bar');
  if (!summaryBar) return;

  const toolbar = document.createElement('div');
  toolbar.id = 'topoRelationToolbar';
  toolbar.className = 'topo-view-toggle';
  toolbar.innerHTML = `
    <button class="topo-view-btn active" data-rel="dependency">依赖</button>
    <button class="topo-view-btn active" data-rel="composition">组合</button>
    <button class="topo-view-btn active" data-rel="aggregation">聚合</button>
    <button class="topo-view-btn active" data-rel="parentChild">父子</button>
    <button class="topo-view-btn active" data-rel="collaboration">协作</button>
  `;
  summaryBar.appendChild(toolbar);
}

function bindRelationToolbar(graphContainer, topoData, sidebar) {
  const toolbar = document.getElementById('topoRelationToolbar');
  if (!toolbar) return;

  toolbar.onclick = e => {
    const btn = e.target.closest('.topo-view-btn');
    if (!btn) return;

    const rel = btn.dataset.rel;
    if (!rel || !(rel in relationFilter)) return;

    relationFilter[rel] = !relationFilter[rel];
    if (!relationFilter.dependency &&
        !relationFilter.composition &&
        !relationFilter.aggregation &&
        !relationFilter.parentChild &&
        !relationFilter.collaboration) {
      relationFilter[rel] = true;
    }

    toolbar.querySelectorAll('.topo-view-btn').forEach(item => {
      const k = item.dataset.rel;
      item.classList.toggle('active', !!relationFilter[k]);
    });

    renderGraphWithSidebar(graphContainer, topoData, sidebar);
  };
}

function renderOverview(sidebar, topoData) {
  if (!sidebar) return;

  const disciplines = (topoData.disciplines || []).slice().sort((a, b) =>
    (a.displayName || a.id || '').localeCompare((b.displayName || b.id || ''), 'zh-CN'));
  const crossWorks = (topoData.crossWorks || []).slice().sort((a, b) =>
    (a.name || '').localeCompare((b.name || ''), 'zh-CN'));
  const modules = (topoData.modules || []).slice().sort((a, b) =>
    (a.displayName || a.name || '').localeCompare((b.displayName || b.name || ''), 'zh-CN'));
  const groups = modules.filter(m => getNodeTypeName(m) === 'Technical');
  const teams = modules.filter(m => getNodeTypeName(m) === 'Team');

  sidebar.innerHTML = `
    <div class="sidebar-header">
      <div class="sidebar-title">架构详情</div>
    </div>
    <div class="sidebar-section">
      <div class="sidebar-section-title">部门（${disciplines.length}）</div>
      <div class="sidebar-kv-list">
        ${disciplines.map(d => `
          <div class="sidebar-kv-row sidebar-kv-stack">
            <span class="sidebar-k">${escapeHtml(d.displayName || d.id || '-')}</span>
            <span class="sidebar-v">技术组 ${d.moduleCount || 0} · 协作声明 ${d.crossWorkCount || 0}${d.roleId ? ` · 角色 ${escapeHtml(d.roleId)}` : ''}</span>
            <div class="sidebar-link-list">
              ${renderNodeLinks(d.modules || [], topoData, '技术组')}
            </div>
          </div>`).join('') || '<div class="sidebar-empty">暂无部门信息</div>'}
      </div>
    </div>
    <div class="sidebar-section">
      <div class="sidebar-section-title">执行团队（${teams.length}）</div>
      <div class="sidebar-link-list">
        ${teams.map(m => `
          <button class="sidebar-link-btn" data-module-id="${escapeAttr(m.name)}">${escapeHtml(m.displayName || m.name)}</button>
        `).join('') || '<div class="sidebar-empty">暂无执行团队</div>'}
      </div>
    </div>
    <div class="sidebar-section">
      <div class="sidebar-section-title">技术组（${groups.length}）</div>
      <div class="sidebar-link-list">
        ${groups.map(m => `
          <button class="sidebar-link-btn" data-module-id="${escapeAttr(m.name)}">${escapeHtml(m.displayName || m.name)}</button>
        `).join('') || '<div class="sidebar-empty">暂无技术组</div>'}
      </div>
    </div>
    <div class="sidebar-section">
      <div class="sidebar-section-title">协作声明（${crossWorks.length}）</div>
      <div class="sidebar-kv-list">
        ${crossWorks.map(cw => `
          <div class="sidebar-kv-row">
            <span class="sidebar-k">${escapeHtml(cw.name || '-')}</span>
            <span class="sidebar-v">参与 ${cw.participants?.length || 0}</span>
          </div>`).join('') || '<div class="sidebar-empty">暂无小组信息</div>'}
      </div>
    </div>
  `;

  sidebar.querySelectorAll('[data-module-id]').forEach(btn => {
    btn.addEventListener('click', () => {
      const id = btn.dataset.moduleId;
      if (!id) return;
      selectedModule = id;
      renderModuleDetail(sidebar, topoData, id);
    });
  });
}

function renderModuleDetail(sidebar, topoData, moduleName) {
  if (isDepartmentNodeId(moduleName)) {
    renderDepartmentDetail(sidebar, topoData, moduleName);
    return;
  }

  const module = findModuleByName(topoData, moduleName);
  if (!sidebar || !module) {
    renderOverview(sidebar, topoData);
    return;
  }

  const edges = getAllEdges(topoData);
  const depOut = edges.filter(e => e.relation === 'dependency' && e.from === module.name);
  const depIn = edges.filter(e => e.relation === 'dependency' && e.to === module.name);
  const containOut = edges.filter(e => isParentChildEdge(e) && e.from === module.name);
  const containIn = edges.filter(e => isParentChildEdge(e) && e.to === module.name);
  const compositionOut = containOut.filter(e => edgeKind(e) === 'composition');
  const compositionIn = containIn.filter(e => edgeKind(e) === 'composition');
  const aggregationOut = containOut.filter(e => edgeKind(e) === 'aggregation');
  const aggregationIn = containIn.filter(e => edgeKind(e) === 'aggregation');
  const collab = edges.filter(e =>
    e.relation === 'collaboration' && (e.from === module.name || e.to === module.name));

  const crossWorks = (topoData.crossWorks || []).filter(cw =>
    (cw.participants || []).some(p =>
      p.moduleId === module.name || p.moduleName === module.displayName));
  const discipline = (topoData.disciplines || []).find(d => d.id === module.discipline);
  const workflow = module.workflow || [];
  const rules = module.rules || module.constraints || [];
  const publicApi = module.publicApi || [];
  const prohibitions = module.prohibitions || [];
  const managedPathScopes = module.managedPathScopes || [];
  const typeName = getNodeTypeName(module);
  const typeLabel = getNodeTypeLabel(module);
  const fileAuthority = module.fileAuthority === 'execute' ? '执行（可改文件）' : '治理（不可直接改文件）';
  const metadataEntries = Object.entries(module.metadata || {});

  sidebar.innerHTML = `
    <div class="sidebar-header">
      <div class="sidebar-title">${escapeHtml(module.displayName || module.name)}</div>
      <button class="sidebar-close" title="返回总览">×</button>
    </div>

    <div class="sidebar-meta">
      <span class="sidebar-badge">${escapeHtml(typeLabel)}</span>
      <span class="sidebar-badge">${escapeHtml(module.disciplineDisplayName || module.discipline || '-')}</span>
      ${module.boundary ? `<span class="sidebar-badge">${escapeHtml(module.boundary)}</span>` : ''}
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">摘要</div>
      <div class="sidebar-text">${escapeHtml(module.summary || '暂无摘要')}</div>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">基础信息</div>
      <div class="sidebar-kv-list">
        <div class="sidebar-kv-row"><span class="sidebar-k">节点ID</span><span class="sidebar-v">${escapeHtml(module.nodeId || '-')}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">节点类型</span><span class="sidebar-v">${escapeHtml(typeName)}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">路径</span><span class="sidebar-v">${escapeHtml(module.relativePath || '-')}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">维护者</span><span class="sidebar-v">${escapeHtml(module.maintainer || '-')}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">所在部门</span><span class="sidebar-v">${escapeHtml(discipline?.displayName || module.disciplineDisplayName || '-')}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">文件权限</span><span class="sidebar-v">${escapeHtml(fileAuthority)}</span></div>
      </div>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">关系总览</div>
      <div class="sidebar-kv-list">
        <div class="sidebar-kv-row"><span class="sidebar-k">依赖</span><span class="sidebar-v">出 ${depOut.length} · 入 ${depIn.length}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">组合</span><span class="sidebar-v">父 ${compositionIn.length} · 子 ${compositionOut.length}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">聚合</span><span class="sidebar-v">父 ${aggregationIn.length} · 子 ${aggregationOut.length}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">父子</span><span class="sidebar-v">父 ${containIn.length} · 子 ${containOut.length}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">协作</span><span class="sidebar-v">${collab.length} 条边 · ${crossWorks.length} 个协作声明</span></div>
      </div>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">依赖目标</div>
      <div class="sidebar-link-list">
        ${renderNodeLinks(depOut.map(e => e.to), topoData)}
      </div>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">包含关系</div>
      <div class="sidebar-link-list">
        ${renderNodeLinks(compositionIn.map(e => e.from), topoData, '组合父')}
        ${renderNodeLinks(compositionOut.map(e => e.to), topoData, '组合子')}
        ${renderNodeLinks(aggregationIn.map(e => e.from), topoData, '聚合父')}
        ${renderNodeLinks(aggregationOut.map(e => e.to), topoData, '聚合子')}
        ${renderNodeLinks(containIn.map(e => e.from), topoData, '父')}
        ${renderNodeLinks(containOut.map(e => e.to), topoData, '子')}
      </div>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">协作声明</div>
      <div class="sidebar-kv-list">
        ${crossWorks.map(cw => `
          <div class="sidebar-kv-row">
            <span class="sidebar-k">${escapeHtml(cw.name || '-')}</span>
            <span class="sidebar-v">参与 ${cw.participants?.length || 0}</span>
          </div>`).join('') || '<div class="sidebar-empty">暂无协作小组</div>'}
      </div>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">工作流（Workflow）</div>
      <ul class="sidebar-list">
        ${workflow.map(x => `<li>${escapeHtml(x)}</li>`).join('') || '<li class="sidebar-empty">暂无</li>'}
      </ul>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">规则（Rules）</div>
      <ul class="sidebar-list">
        ${rules.map(x => `<li>${escapeHtml(x)}</li>`).join('') || '<li class="sidebar-empty">暂无</li>'}
      </ul>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">公开接口（PublicApi）</div>
      <ul class="sidebar-list">
        ${publicApi.map(x => `<li>${escapeHtml(x)}</li>`).join('') || '<li class="sidebar-empty">暂无</li>'}
      </ul>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">禁令（Prohibitions）</div>
      <ul class="sidebar-list">
        ${prohibitions.map(x => `<li>${escapeHtml(x)}</li>`).join('') || '<li class="sidebar-empty">暂无</li>'}
      </ul>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">责任路径域</div>
      <ul class="sidebar-list">
        ${managedPathScopes.map(x => `<li>${escapeHtml(x)}</li>`).join('') || '<li class="sidebar-empty">暂无</li>'}
      </ul>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">元数据</div>
      <div class="sidebar-kv-list">
        ${metadataEntries.map(([k, v]) => `
          <div class="sidebar-kv-row"><span class="sidebar-k">${escapeHtml(k)}</span><span class="sidebar-v">${escapeHtml(String(v))}</span></div>
        `).join('') || '<div class="sidebar-empty">暂无</div>'}
      </div>
    </div>
  `;

  const closeBtn = sidebar.querySelector('.sidebar-close');
  if (closeBtn) {
    closeBtn.addEventListener('click', () => {
      selectedModule = null;
      renderOverview(sidebar, topoData);
    });
  }

  sidebar.querySelectorAll('[data-module-id]').forEach(btn => {
    btn.addEventListener('click', () => {
      const id = btn.dataset.moduleId;
      if (!id) return;
      selectedModule = id;
      renderModuleDetail(sidebar, topoData, id);
    });
  });
}

function renderDepartmentDetail(sidebar, topoData, departmentNodeId) {
  if (!sidebar) return;

  const disciplineId = departmentNodeId.slice(DEPT_NODE_PREFIX.length);
  const discipline = (topoData.disciplines || []).find(d =>
    String(d.id || '').toLowerCase() === disciplineId.toLowerCase());

  const modules = (topoData.modules || []).filter(m =>
    String(m.discipline || '').toLowerCase() === disciplineId.toLowerCase());
  const moduleIds = new Set(modules.map(m => m.name));

  const edges = getAllEdges(topoData);
  const depIn = edges.filter(e => e.relation === 'dependency' && moduleIds.has(e.to) && !moduleIds.has(e.from));
  const depOut = edges.filter(e => e.relation === 'dependency' && moduleIds.has(e.from) && !moduleIds.has(e.to));
  const depInner = edges.filter(e => e.relation === 'dependency' && moduleIds.has(e.from) && moduleIds.has(e.to));
  const collab = edges.filter(e => e.relation === 'collaboration' && (moduleIds.has(e.from) || moduleIds.has(e.to)));
  const contain = edges.filter(e => e.relation === 'containment' && (moduleIds.has(e.from) || moduleIds.has(e.to)));

  sidebar.innerHTML = `
    <div class="sidebar-header">
      <div class="sidebar-title">${escapeHtml(discipline?.displayName || disciplineId)}</div>
      <button class="sidebar-close" title="返回总览">×</button>
    </div>

    <div class="sidebar-meta">
      <span class="sidebar-badge">Department（部门）</span>
      <span class="sidebar-badge">治理节点</span>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">部门概览</div>
      <div class="sidebar-kv-list">
        <div class="sidebar-kv-row"><span class="sidebar-k">部门ID</span><span class="sidebar-v">${escapeHtml(disciplineId)}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">技术组数量</span><span class="sidebar-v">${modules.length}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">依赖关系</span><span class="sidebar-v">内部 ${depInner.length} · 入 ${depIn.length} · 出 ${depOut.length}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">协作关系</span><span class="sidebar-v">${collab.length}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">父子关系</span><span class="sidebar-v">${contain.length}</span></div>
      </div>
    </div>

    <div class="sidebar-section">
      <div class="sidebar-section-title">部门内技术组</div>
      <div class="sidebar-link-list">
        ${modules.map(m => `
          <button class="sidebar-link-btn" data-module-id="${escapeAttr(m.name)}">${escapeHtml(m.displayName || m.name)}</button>
        `).join('') || '<div class="sidebar-empty">暂无技术组</div>'}
      </div>
    </div>
  `;

  const closeBtn = sidebar.querySelector('.sidebar-close');
  if (closeBtn) {
    closeBtn.addEventListener('click', () => {
      selectedModule = null;
      renderOverview(sidebar, topoData);
    });
  }

  sidebar.querySelectorAll('[data-module-id]').forEach(btn => {
    btn.addEventListener('click', () => {
      const id = btn.dataset.moduleId;
      if (!id) return;
      selectedModule = id;
      renderModuleDetail(sidebar, topoData, id);
    });
  });
}

function renderEdgeDetail(sidebar, topoData, edge) {
  if (!sidebar || !edge) {
    renderOverview(sidebar, topoData);
    return;
  }

  const relationName = getRelationName(edge);
  const relationMeaning = getRelationMeaning(edge);
  const fromName = resolveNodeDisplayName(topoData, edge.from);
  const toName = resolveNodeDisplayName(topoData, edge.to);
  const uniqueSources = [...new Set(edge.sources || [])];
  const uniqueTargets = [...new Set(edge.targets || [])];
  const isGatewayEdge = isGatewayNodeId(edge.from) || isGatewayNodeId(edge.to);

  sidebar.innerHTML = `
    <div class="sidebar-header">
      <div class="sidebar-title">连线详情</div>
      <button class="sidebar-close" title="返回总览">×</button>
    </div>
    <div class="sidebar-meta">
      <span class="sidebar-badge">${escapeHtml(relationName)}</span>
      ${edge.isComputed ? '<span class="sidebar-badge">推导边</span>' : '<span class="sidebar-badge">显式边</span>'}
    </div>
    <div class="sidebar-section">
      <div class="sidebar-section-title">当前视图连线</div>
      <div class="sidebar-kv-list">
        <div class="sidebar-kv-row"><span class="sidebar-k">起点</span><span class="sidebar-v">${escapeHtml(fromName)}</span></div>
        <div class="sidebar-kv-row"><span class="sidebar-k">终点</span><span class="sidebar-v">${escapeHtml(toName)}</span></div>
      </div>
      <div class="sidebar-text" style="margin-top:8px;">${escapeHtml(relationMeaning)}</div>
    </div>
    <div class="sidebar-section">
      <div class="sidebar-section-title">原始连线端点</div>
      <div class="sidebar-kv-list">
        <div class="sidebar-kv-row sidebar-kv-stack">
          <span class="sidebar-k">${isGatewayNodeId(edge.from) ? '外部来源' : '来源集合'}</span>
          <div class="sidebar-link-list">
            ${renderNodeLinksById(uniqueSources, topoData)}
          </div>
        </div>
        <div class="sidebar-kv-row sidebar-kv-stack">
          <span class="sidebar-k">${isGatewayNodeId(edge.to) ? '外部去向' : '目标集合'}</span>
          <div class="sidebar-link-list">
            ${renderNodeLinksById(uniqueTargets, topoData)}
          </div>
        </div>
      </div>
    </div>
    ${isGatewayEdge ? `
    <div class="sidebar-section">
      <div class="sidebar-section-title">ENTRY/EXIT 说明</div>
      <div class="sidebar-text">ENTRY 表示“外部模块进入当前层的关系入口”；EXIT 表示“当前层模块流向外部模块的关系出口”。点击连线后可在上方看到具体外部模块集合。</div>
    </div>` : ''}
  `;

  const closeBtn = sidebar.querySelector('.sidebar-close');
  if (closeBtn) {
    closeBtn.addEventListener('click', () => {
      selectedEdge = null;
      selectedModule = null;
      renderOverview(sidebar, topoData);
    });
  }

  sidebar.querySelectorAll('[data-module-id]').forEach(btn => {
    btn.addEventListener('click', () => {
      const id = btn.dataset.moduleId;
      if (!id) return;
      selectedEdge = null;
      selectedModule = id;
      renderModuleDetail(sidebar, topoData, id);
    });
  });
}

function renderNodeLinks(ids, topoData, prefix = '') {
  const unique = [...new Set(ids || [])];
  if (unique.length === 0) return '<div class="sidebar-empty">暂无</div>';
  return unique.map(id => {
    const m = findModuleByName(topoData, id);
    const title = m?.displayName || id;
    const label = prefix ? `${prefix}: ${title}` : title;
    return `<button class="sidebar-link-btn" data-module-id="${escapeAttr(id)}">${escapeHtml(label)}</button>`;
  }).join('');
}

function renderNodeLinksById(ids, topoData) {
  const unique = [...new Set(ids || [])];
  if (unique.length === 0) return '<div class="sidebar-empty">暂无</div>';
  return unique.map(id => {
    const m = findModuleByName(topoData, id);
    if (!m) return `<div class="sidebar-empty">${escapeHtml(resolveNodeDisplayName(topoData, id))}</div>`;
    return `<button class="sidebar-link-btn" data-module-id="${escapeAttr(m.name)}">${escapeHtml(m.displayName || m.name)}</button>`;
  }).join('');
}

function getAllEdges(topoData) {
  if (Array.isArray(topoData.relationEdges) && topoData.relationEdges.length > 0) {
    return topoData.relationEdges.map(e => ({
      ...e,
      kind: e.kind || inferContainmentKind(e)
    }));
  }

  const dep = (topoData.edges || []).map(e => ({ ...e, relation: 'dependency' }));
  const contain = (topoData.containmentEdges || []).map(e => ({
    ...e,
    relation: 'containment',
    kind: e.kind || inferContainmentKind(e)
  }));
  const collab = (topoData.collaborationEdges || []).map(e => ({ ...e, relation: 'collaboration' }));
  return dep.concat(contain, collab);
}

function isParentChildEdge(edge) {
  return edge?.relation === 'containment';
}

function edgeKind(edge) {
  if (!isParentChildEdge(edge)) return '';
  return inferContainmentKind(edge);
}

function inferContainmentKind(edge) {
  if (!edge || edge.relation !== 'containment') return '';
  const raw = String(edge.kind || '').toLowerCase();
  if (raw === 'composition' || raw === 'aggregation') return raw;
  if (edge.isComputed) return 'aggregation';
  return 'composition';
}

function findModuleByName(topoData, name) {
  return (topoData.modules || []).find(m =>
    m.name === name || m.displayName === name || m.nodeId === name);
}

function isDepartmentNodeId(name) {
  return typeof name === 'string' && name.startsWith(DEPT_NODE_PREFIX);
}

function isGatewayNodeId(name) {
  return typeof name === 'string' &&
    (name.startsWith(ENTRY_NODE_PREFIX) || name.startsWith(EXIT_NODE_PREFIX));
}

function resolveNodeDisplayName(topoData, id) {
  if (!id) return '-';
  if (id.startsWith(DEPT_NODE_PREFIX)) {
    const disciplineId = id.slice(DEPT_NODE_PREFIX.length);
    const discipline = (topoData.disciplines || []).find(d => String(d.id || '').toLowerCase() === disciplineId.toLowerCase());
    return discipline?.displayName || disciplineId;
  }
  if (id.startsWith(ENTRY_NODE_PREFIX)) return 'ENTRY';
  if (id.startsWith(EXIT_NODE_PREFIX)) return 'EXIT';
  const m = findModuleByName(topoData, id);
  return m?.displayName || id;
}

function getRelationName(edge) {
  if (edge.relation === 'dependency') return '依赖';
  if (edge.relation === 'collaboration') return '协作';
  if (edge.relation === 'containment') {
    if (inferContainmentKind(edge) === 'aggregation') return '聚合';
    return '组合';
  }
  return edge.relation || '关系';
}

function getRelationMeaning(edge) {
  if (edge.relation === 'dependency') {
    return '依赖：起点模块需要调用或依赖终点模块提供的能力。箭头方向=依赖方向。';
  }
  if (edge.relation === 'collaboration') {
    return '协作：两个节点在同一执行链路或协作声明中共同工作。';
  }
  if (edge.relation === 'containment') {
    if (inferContainmentKind(edge) === 'aggregation') {
      return '聚合：父节点聚合管理子节点，生命周期通常可独立。';
    }
    return '组合：父节点强拥有子节点，生命周期更紧耦合。';
  }
  return '关系语义未定义。';
}

function getNodeTypeName(module) {
  if (!module) return 'Technical';
  if (module.typeName) return module.typeName === 'Group' ? 'Technical' : module.typeName;
  if (module.type) return module.type === 'Group' ? 'Technical' : module.type;
  return module.isCrossWorkModule ? 'Team' : 'Technical';
}

function getNodeTypeLabel(module) {
  if (module?.typeLabel) return module.typeLabel;
  const typeName = getNodeTypeName(module);
  if (typeName === 'Project') return '项目';
  if (typeName === 'Department') return '部门';
  if (typeName === 'Team') return '执行团队';
  return '技术组';
}

function escapeHtml(input) {
  return String(input ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function escapeAttr(input) {
  return escapeHtml(input).replaceAll('`', '&#96;');
}
