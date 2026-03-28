import { $, api, escapeHtml } from '../utils.js';

let _manifest = null;
let _selectedModuleKey = null;
let _selectedCrossworkId = null;
let _selectedDisciplineId = null;
let _validationBound = false;

function getDisciplines() {
  return Object.keys(_manifest?.disciplines || {});
}

function flattenModules() {
  const result = [];
  const disciplines = _manifest?.disciplines || {};
  Object.entries(disciplines).forEach(([discipline, group]) => {
    (group.modules || []).forEach(m => result.push({ discipline, ...m }));
  });
  return result;
}

function parseLines(value) {
  return (value || '').split('\n').map(s => s.trim()).filter(Boolean);
}

function joinLines(items) {
  return (items || []).join('\n');
}

function clearModuleForm() {
  _selectedModuleKey = null;
  $('modDiscipline').value = getDisciplines()[0] || '';
  $('modId').value = '';
  $('modName').value = '';
  $('modPath').value = '';
  $('modMaintainer').value = '';
  $('modDependencies').value = '';
  renderLayerOptions();
}

function clearCrossworkForm() {
  _selectedCrossworkId = null;
  $('cwId').value = '';
  $('cwName').value = '';
  $('cwFeature').value = '';
  $('cwDescription').value = '';
  $('cwParticipants').innerHTML = '';
  addParticipantRow();
}

function renderModuleList() {
  const list = $('moduleList');
  const modules = flattenModules();
  if (modules.length === 0) {
    list.innerHTML = '<div class="empty" style="padding:12px;">暂无模块</div>';
    return;
  }

  list.innerHTML = modules.map(m => {
    const key = `${m.discipline}::${m.name}`;
    return `
      <div class="module-admin-row ${_selectedModuleKey === key ? 'active' : ''}" data-key="${escapeHtml(key)}">
        <span>${escapeHtml(m.name)}</span>
        <span style="color:#94a3b8;">${escapeHtml(m.discipline)} · L${m.layer}</span>
      </div>
    `;
  }).join('');

  list.querySelectorAll('.module-admin-row').forEach(row => {
    row.addEventListener('click', () => selectModuleByKey(row.dataset.key));
  });
}

function renderCrossworkList() {
  const list = $('crossworkList');
  const items = _manifest?.crossWorks || [];
  if (items.length === 0) {
    list.innerHTML = '<div class="empty" style="padding:12px;">暂无 CrossWork</div>';
    return;
  }

  list.innerHTML = items.map(cw => `
    <div class="module-admin-row ${_selectedCrossworkId === cw.id ? 'active' : ''}" data-id="${escapeHtml(cw.id || '')}">
      <span>${escapeHtml(cw.name || '未命名')}</span>
      <span style="color:#94a3b8;">${escapeHtml((cw.participants || []).length.toString())} 参与方</span>
    </div>
  `).join('');

  list.querySelectorAll('.module-admin-row').forEach(row => {
    row.addEventListener('click', () => selectCrosswork(row.dataset.id));
  });
}

function renderLayerOptions() {
  const discipline = $('modDiscipline').value;
  const layers = _manifest?.disciplines?.[discipline]?.layers || [];
  const current = $('modLayer').value;
  $('modLayer').innerHTML = layers.map(l => `<option value="${l.level}">L${l.level} ${escapeHtml(l.name || '')}</option>`).join('');
  if (current && layers.some(l => String(l.level) === current)) {
    $('modLayer').value = current;
  }
  updateModuleHint();
}

function selectModuleByKey(key) {
  _selectedModuleKey = key;
  renderModuleList();

  const [discipline, name] = key.split('::');
  const group = _manifest?.disciplines?.[discipline];
  const m = (group?.modules || []).find(x => x.name === name);
  if (!m) return;

  $('modDiscipline').value = discipline;
  renderLayerOptions();
  $('modId').value = m.id || '';
  $('modName').value = m.name || '';
  $('modPath').value = m.path || '';
  $('modLayer').value = String(m.layer ?? '');
  $('modMaintainer').value = m.maintainer || '';
  $('modDependencies').value = joinLines(m.dependencies || []);
}

function addParticipantRow(participant = null) {
  const row = document.createElement('div');
  row.className = 'module-admin-form';
  row.style.border = '1px solid var(--border)';
  row.style.borderRadius = '6px';
  row.style.padding = '8px';
  row.innerHTML = `
    <input type="text" class="cw-module" placeholder="模块名" value="${escapeHtml(participant?.moduleName || '')}" />
    <input type="text" class="cw-role" placeholder="职责" value="${escapeHtml(participant?.role || '')}" />
    <input type="text" class="cw-contract-type" placeholder="契约类型（可选）" value="${escapeHtml(participant?.contractType || '')}" />
    <textarea class="cw-contract" placeholder="契约内容（可选）">${escapeHtml(participant?.contract || '')}</textarea>
    <input type="text" class="cw-deliverable" placeholder="交付物（可选）" value="${escapeHtml(participant?.deliverable || '')}" />
    <button type="button" class="btn btn-secondary btn-sm">删除参与方</button>
  `;
  row.querySelector('button').addEventListener('click', () => row.remove());
  $('cwParticipants').appendChild(row);
}

function selectCrosswork(id) {
  _selectedCrossworkId = id;
  renderCrossworkList();

  const cw = (_manifest?.crossWorks || []).find(x => x.id === id);
  if (!cw) return;

  $('cwId').value = cw.id || '';
  $('cwName').value = cw.name || '';
  $('cwFeature').value = cw.feature || '';
  $('cwDescription').value = cw.description || '';
  $('cwParticipants').innerHTML = '';
  (cw.participants || []).forEach(p => addParticipantRow(p));
  if ((cw.participants || []).length === 0) addParticipantRow();
}

function readParticipants() {
  return Array.from($('cwParticipants').children).map(row => ({
    moduleName: row.querySelector('.cw-module')?.value?.trim() || '',
    role: row.querySelector('.cw-role')?.value?.trim() || '',
    contractType: row.querySelector('.cw-contract-type')?.value?.trim() || null,
    contract: row.querySelector('.cw-contract')?.value?.trim() || null,
    deliverable: row.querySelector('.cw-deliverable')?.value?.trim() || null
  })).filter(p => p.moduleName);
}

function getDisciplineLayers(discipline) {
  return _manifest?.disciplines?.[discipline]?.layers || [];
}

function validateModuleForm() {
  const errors = [];
  const discipline = $('modDiscipline').value;
  const name = $('modName').value.trim();
  const path = $('modPath').value.trim();
  const layerRaw = $('modLayer').value;
  const layer = Number.parseInt(layerRaw, 10);
  const dependencies = parseLines($('modDependencies').value);

  const disciplines = getDisciplines();
  if (!discipline || !disciplines.includes(discipline)) {
    errors.push('Discipline 无效，请先选择已存在的职能域。');
  }

  const layers = getDisciplineLayers(discipline);
  if (layers.length === 0) {
    errors.push(`Discipline '${discipline || '-'}' 未定义任何 layer。`);
  } else {
    const allowed = new Set(layers.map(l => Number(l.level)));
    if (!Number.isInteger(layer) || !allowed.has(layer)) {
      errors.push(`Layer 必须从 '${discipline}' 的预定义层级中选择。`);
    }
  }

  if (!name) errors.push('模块名不能为空。');
  if (!path) errors.push('模块路径不能为空。');

  if (name) {
    const duplicateName = flattenModules().find(m =>
      m.name?.toLowerCase() === name.toLowerCase() &&
      `${m.discipline}::${m.name}` !== _selectedModuleKey);
    if (duplicateName) {
      errors.push(`模块名重复：已存在于 discipline '${duplicateName.discipline}'。`);
    }
  }

  if (path) {
    const duplicatePath = flattenModules().find(m =>
      (m.path || '').toLowerCase() === path.toLowerCase() &&
      `${m.discipline}::${m.name}` !== _selectedModuleKey);
    if (duplicatePath) {
      errors.push(`模块路径重复：已被模块 '${duplicatePath.name}' 使用。`);
    }
  }

  if (name && dependencies.some(dep => dep.toLowerCase() === name.toLowerCase())) {
    errors.push('Dependencies 不能包含自身模块名。');
  }

  return { ok: errors.length === 0, errors };
}

function updateModuleHint() {
  const hint = $('modConstraintHint');
  if (!hint) return;
  const result = validateModuleForm();
  if (result.ok) {
    hint.textContent = '约束校验通过';
    hint.classList.remove('error');
    return;
  }
  hint.textContent = result.errors[0];
  hint.classList.add('error');
}

function bindValidationHandlers() {
  if (_validationBound) return;
  _validationBound = true;

  ['modDiscipline', 'modName', 'modPath', 'modLayer', 'modDependencies'].forEach(id => {
    const el = $(id);
    if (!el) return;
    el.addEventListener('input', updateModuleHint);
    el.addEventListener('change', updateModuleHint);
  });
}

export async function loadModuleManagement() {
  try {
    _manifest = await api('/modules/manifest');
    renderDisciplineList();
    const disciplines = getDisciplines();
    const options = disciplines.map(d => `<option value="${escapeHtml(d)}">${escapeHtml(d)}</option>`).join('');
    $('modDiscipline').innerHTML = options;
    renderLayerOptions();
    renderModuleList();
    renderCrossworkList();
    bindValidationHandlers();
    updateModuleHint();

    if (!$('cwParticipants').children.length) addParticipantRow();
  } catch (err) {
    $('moduleList').innerHTML = `<div class="empty error" style="padding:12px;">加载失败: ${escapeHtml(err.message || String(err))}</div>`;
  }
}

export async function saveModule() {
  const check = validateModuleForm();
  if (!check.ok) {
    updateModuleHint();
    alert('保存失败: ' + check.errors[0]);
    return;
  }

  const discipline = $('modDiscipline').value;
  const module = {
    id: $('modId').value.trim() || null,
    name: $('modName').value.trim(),
    path: $('modPath').value.trim(),
    layer: parseInt($('modLayer').value || '0', 10),
    dependencies: parseLines($('modDependencies').value),
    maintainer: $('modMaintainer').value.trim() || null
  };

  const res = await api('/modules', { method: 'POST', body: { discipline, module } });
  if (res?.error) {
    alert('保存失败: ' + res.error);
    return;
  }

  await loadModuleManagement();
  if (window.refresh) window.refresh();
  alert('模块保存成功');
}

export async function deleteModule() {
  const name = $('modName').value.trim();
  if (!name) return;
  if (!confirm(`确认删除模块 ${name} ?`)) return;

  const res = await api(`/modules/${encodeURIComponent(name)}`, { method: 'DELETE' });
  if (res?.error) {
    alert('删除失败: ' + res.error);
    return;
  }

  clearModuleForm();
  await loadModuleManagement();
  if (window.refresh) window.refresh();
}

export async function saveCrosswork() {
  const req = {
    id: $('cwId').value.trim() || null,
    name: $('cwName').value.trim(),
    description: $('cwDescription').value.trim() || null,
    feature: $('cwFeature').value.trim() || null,
    participants: readParticipants()
  };

  if (!req.name) {
    alert('CrossWork 名称必填');
    return;
  }

  const res = await api('/modules/crossworks', { method: 'POST', body: req });
  if (res?.error) {
    alert('保存失败: ' + res.error);
    return;
  }

  await loadModuleManagement();
  if (window.refresh) window.refresh();
  alert('CrossWork 保存成功');
}

export async function deleteCrosswork() {
  const id = $('cwId').value.trim();
  if (!id) return;
  if (!confirm(`确认删除 CrossWork ${id} ?`)) return;

  const res = await api(`/modules/crossworks/${encodeURIComponent(id)}`, { method: 'DELETE' });
  if (res?.error) {
    alert('删除失败: ' + res.error);
    return;
  }

  clearCrossworkForm();
  await loadModuleManagement();
  if (window.refresh) window.refresh();
}

export function newModule() {
  clearModuleForm();
}

export function newCrosswork() {
  clearCrossworkForm();
}

export function onDisciplineChanged() {
  renderLayerOptions();
  updateModuleHint();
}

export function addCrossworkParticipant() {
  addParticipantRow();
}

// ═══════════════════════════════════════════
//  Discipline（部门/层级）管理
// ═══════════════════════════════════════════

function renderDisciplineList() {
  const list = $('disciplineList');
  if (!list) return;
  const disciplines = getDisciplines();
  if (disciplines.length === 0) {
    list.innerHTML = '<div class="empty" style="padding:12px;">暂无部门，请先创建</div>';
    return;
  }

  list.innerHTML = disciplines.map(id => {
    const g = _manifest.disciplines[id];
    const label = g?.displayName || id;
    const layerCount = (g?.layers || []).length;
    const modCount = (g?.modules || []).length;
    return `
      <div class="module-admin-row ${_selectedDisciplineId === id ? 'active' : ''}" data-id="${escapeHtml(id)}">
        <span>${escapeHtml(label)} (${escapeHtml(id)})</span>
        <span style="color:#94a3b8;">${layerCount} 层 · ${modCount} 模块</span>
      </div>
    `;
  }).join('');

  list.querySelectorAll('.module-admin-row').forEach(row => {
    row.addEventListener('click', () => selectDiscipline(row.dataset.id));
  });
}

function selectDiscipline(id) {
  _selectedDisciplineId = id;
  renderDisciplineList();

  const g = _manifest?.disciplines?.[id];
  if (!g) return;

  $('discId').value = id;
  $('discDisplayName').value = g.displayName || '';
  if ($('discRoleId')) $('discRoleId').value = g.roleId || 'coder';

  const container = $('discLayers');
  container.innerHTML = '';
  (g.layers || []).forEach(l => addLayerRowWithData(l.level, l.name));
  if ((g.layers || []).length === 0) addLayerRowWithData(0, '');
}

function clearDisciplineForm() {
  _selectedDisciplineId = null;
  $('discId').value = '';
  $('discDisplayName').value = '';
  if ($('discRoleId')) $('discRoleId').value = 'coder';
  $('discLayers').innerHTML = '';
  addLayerRowWithData(0, '');
}

function addLayerRowWithData(level, name) {
  const container = $('discLayers');
  const row = document.createElement('div');
  row.style.display = 'flex';
  row.style.gap = '8px';
  row.style.alignItems = 'center';
  row.innerHTML = `
    <input type="number" class="layer-level" placeholder="Level" value="${level}" style="width:70px;" />
    <input type="text" class="layer-name" placeholder="层级名称（如 foundation）" value="${escapeHtml(name || '')}" style="flex:1;" />
    <button type="button" class="btn btn-secondary btn-sm" style="padding:2px 8px;">x</button>
  `;
  row.querySelector('button').addEventListener('click', () => row.remove());
  container.appendChild(row);
}

function readLayers() {
  const container = $('discLayers');
  return Array.from(container.children).map(row => ({
    level: parseInt(row.querySelector('.layer-level')?.value || '0', 10),
    name: row.querySelector('.layer-name')?.value?.trim() || ''
  })).filter(l => l.name);
}

export function newDiscipline() {
  clearDisciplineForm();
  renderDisciplineList();
}

export function addLayerRow() {
  const container = $('discLayers');
  const existing = Array.from(container.children)
    .map(r => parseInt(r.querySelector('.layer-level')?.value || '0', 10));
  const nextLevel = existing.length > 0 ? Math.max(...existing) + 1 : 0;
  addLayerRowWithData(nextLevel, '');
}

export async function saveDiscipline() {
  const id = $('discId').value.trim().toLowerCase();
  if (!id) { alert('部门 ID 不能为空'); return; }

  const layers = readLayers();
  if (layers.length === 0) { alert('至少需要定义一个层级'); return; }

  const levels = layers.map(l => l.level);
  if (new Set(levels).size !== levels.length) { alert('层级 Level 不能重复'); return; }

  const res = await api('/modules/disciplines', {
    method: 'POST',
    body: { id, displayName: $('discDisplayName').value.trim() || null, roleId: $('discRoleId')?.value || 'coder', layers }
  });

  if (res?.error) { alert('保存失败: ' + res.error); return; }

  _selectedDisciplineId = id;
  await loadModuleManagement();
  if (window.refresh) window.refresh();
  alert('部门保存成功');
}

export async function deleteDiscipline() {
  const id = $('discId').value.trim();
  if (!id) return;
  if (!confirm(`确认删除部门 "${id}" ？（该部门下不能有模块）`)) return;

  const res = await api(`/modules/disciplines/${encodeURIComponent(id)}`, { method: 'DELETE' });
  if (res?.error) { alert('删除失败: ' + res.error); return; }

  clearDisciplineForm();
  await loadModuleManagement();
  if (window.refresh) window.refresh();
}
