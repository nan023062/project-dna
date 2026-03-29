/**
 * 拓扑面板 — 图形视图 + 关系切换 + 详情侧栏
 */

import { $ } from '../utils.js';
import { renderGraph } from '../graph/graph-renderer.js';

const relationFilter = {
  dependency: true,
  containment: true,
  collaboration: true
};

let selectedModule = null;

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
      selectedModule = moduleName;
      renderModuleDetail(sidebar, topoData, moduleName);
    }
  });

  if (selectedModule && findModuleByName(topoData, selectedModule)) {
    renderModuleDetail(sidebar, topoData, selectedModule);
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
    <button class="topo-view-btn active" data-rel="containment">包含</button>
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
    if (!relationFilter.dependency && !relationFilter.containment && !relationFilter.collaboration) {
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
  const groups = modules.filter(m => getNodeTypeName(m) === 'Group');
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
  const module = findModuleByName(topoData, moduleName);
  if (!sidebar || !module) {
    renderOverview(sidebar, topoData);
    return;
  }

  const edges = getAllEdges(topoData);
  const depOut = edges.filter(e => e.relation === 'dependency' && e.from === module.name);
  const depIn = edges.filter(e => e.relation === 'dependency' && e.to === module.name);
  const containOut = edges.filter(e => e.relation === 'containment' && e.from === module.name);
  const containIn = edges.filter(e => e.relation === 'containment' && e.to === module.name);
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
        <div class="sidebar-kv-row"><span class="sidebar-k">包含</span><span class="sidebar-v">父 ${containIn.length} · 子 ${containOut.length}</span></div>
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
        ${renderNodeLinks(containIn.map(e => e.from), topoData, '父模块')}
        ${renderNodeLinks(containOut.map(e => e.to), topoData, '子模块')}
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

function getAllEdges(topoData) {
  if (Array.isArray(topoData.relationEdges) && topoData.relationEdges.length > 0) {
    return topoData.relationEdges;
  }

  const dep = (topoData.edges || []).map(e => ({ ...e, relation: 'dependency' }));
  const contain = (topoData.containmentEdges || []).map(e => ({ ...e, relation: 'containment' }));
  const collab = (topoData.collaborationEdges || []).map(e => ({ ...e, relation: 'collaboration' }));
  return dep.concat(contain, collab);
}

function findModuleByName(topoData, name) {
  return (topoData.modules || []).find(m =>
    m.name === name || m.displayName === name || m.nodeId === name);
}

function getNodeTypeName(module) {
  if (!module) return 'Group';
  if (module.typeName) return module.typeName;
  if (module.type) return module.type;
  return module.isCrossWorkModule ? 'Team' : 'Group';
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
