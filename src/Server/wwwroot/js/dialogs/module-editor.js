import { $, api, escapeHtml } from '../utils.js';
import { ui } from '../ui/ui-manager.js';

const DIALOG_ID = 'module-editor';
let _manifest = null;
let _architecture = null;

function getAllModuleNames() {
  if (!_manifest?.disciplines) return [];
  const names = [];
  for (const modules of Object.values(_manifest.disciplines)) {
    for (const m of (modules.modules || modules || [])) {
      if (m.name) names.push(m.name);
    }
  }
  return names.sort();
}

export async function initEditSidebar() {
  _architecture = await api('/modules/disciplines').catch(() => []);
  _manifest = await api('/modules/manifest').catch(() => null);
}

export function openEditSidebar(opts = {}) {
  const isEdit = opts.isEdit || false;
  const title = isEdit ? '编辑模块' : '注册新模块';

  const disciplines = _architecture || [];
  const discOptions = disciplines.map(d =>
    `<option value="${escapeHtml(d.id)}">${escapeHtml(d.displayName || d.id)}</option>`
  ).join('');

  const formHtml = `
    <div class="edit-dialog-form">
      <div class="form-group">
        <label>部门</label>
        <select id="editModDiscipline">${discOptions}</select>
      </div>
      <div class="form-group">
        <label>层级</label>
        <select id="editModLayer"></select>
      </div>
      <div class="form-group">
        <label>模块 ID</label>
        <input id="editModId" readonly style="opacity:0.6;" />
      </div>
      <div class="form-group">
        <label>模块名</label>
        <input id="editModName" placeholder="模块名称" />
      </div>
      <div class="form-group">
        <label>路径</label>
        <input id="editModPath" placeholder="相对路径" />
      </div>
      <div class="form-group">
        <label>维护者</label>
        <input id="editModMaintainer" placeholder="维护者（可选）" />
      </div>
      <div class="form-group">
        <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
          <input type="checkbox" id="editModIsCrossWorkModule" />
          CrossWork 工作组模块
        </label>
        <div style="font-size:11px;color:var(--text-secondary);margin-top:2px;">
          勾选后此模块可访问任意普通模块，但不被其他模块依赖
        </div>
      </div>
      <div id="editCwOwnershipInfo" style="display:none;padding:6px 10px;background:#0f172a;border:1px solid #334155;border-radius:6px;font-size:12px;color:#94a3b8;margin-bottom:8px;">
        <div>归属：<b id="editCwDisciplineComputed" style="color:#e2e8f0;">-</b></div>
        <div>层级：<b id="editCwLayerComputed" style="color:#e2e8f0;">-</b></div>
        <div style="margin-top:2px;font-size:11px;color:#64748b;">由工作组成员自动推算，不可手动修改</div>
      </div>
      <div id="editParticipantsSection" style="display:none;">
        <div class="form-group">
          <label>工作组成员</label>
          <div id="editParticipantsList"></div>
          <button type="button" class="btn btn-secondary btn-sm" id="editBtnAddParticipant" style="margin-top:4px;">+ 添加成员</button>
        </div>
      </div>
      <div id="editDepsSection">
        <div class="form-group">
          <label>依赖</label>
          <div id="editDepTags" class="dep-tags-container"></div>
          <div style="position:relative;">
            <input id="editDepSearch" placeholder="搜索模块名添加依赖…" autocomplete="off" />
            <div id="editDepSuggestions" class="dep-suggestions" style="display:none;"></div>
          </div>
        </div>
      </div>
      <div class="actions" style="display:flex;gap:8px;margin-top:12px;">
        <button class="btn btn-primary btn-sm" id="editBtnSave">保存</button>
        <button class="btn btn-secondary btn-sm" id="editBtnDelete">删除</button>
        <button class="btn btn-secondary btn-sm" id="editBtnCancel">取消</button>
      </div>
    </div>
  `;

  const dialog = ui.dialogs.open({
    id: DIALOG_ID,
    title,
    content: formHtml,
    className: 'module-editor-dialog'
  });

  dialog.bodyEl.querySelector('#editBtnSave').addEventListener('click', saveFromSidebar);
  dialog.bodyEl.querySelector('#editBtnDelete').addEventListener('click', deleteFromSidebar);
  dialog.bodyEl.querySelector('#editBtnCancel').addEventListener('click', closeEditSidebar);

  const searchInput = dialog.bodyEl.querySelector('#editDepSearch');
  searchInput.addEventListener('input', onDepSearchInput);
  searchInput.addEventListener('keydown', onDepSearchKeydown);

  const discSelect = dialog.bodyEl.querySelector('#editModDiscipline');
  discSelect.addEventListener('change', onEditDisciplineChanged);

  const cwCheckbox = dialog.bodyEl.querySelector('#editModIsCrossWorkModule');
  cwCheckbox.addEventListener('change', toggleCrossWorkSections);

  dialog.bodyEl.querySelector('#editBtnAddParticipant')
    .addEventListener('click', () => addParticipantRow());

  $('editModId').value = opts.id || '';
  $('editModName').value = opts.name || '';
  $('editModPath').value = opts.path || '';
  $('editModMaintainer').value = '';
  cwCheckbox.checked = !!opts.isCrossWorkModule;

  if (opts.discipline) {
    discSelect.value = opts.discipline;
  } else if (opts.path) {
    const guessed = guessDiscipline(opts.path);
    if (guessed) discSelect.value = guessed;
  }

  renderEditLayerOptions();
  if (opts.layer !== undefined) {
    $('editModLayer').value = String(opts.layer);
  }

  if (isEdit && _manifest && opts.discipline) {
    const modules = _manifest.disciplines?.[opts.discipline]?.modules || [];
    const existing = modules.find(m => m.name === opts.name);
    if (existing) {
      $('editModMaintainer').value = existing.maintainer || '';
      cwCheckbox.checked = !!existing.isCrossWorkModule;
      (existing.dependencies || []).forEach(dep => addDepTag(dep));
      if (existing.isCrossWorkModule && existing.participants) {
        existing.participants.forEach(p => addParticipantRow(p));
      }
    }
  }

  toggleCrossWorkSections();
}

export function closeEditSidebar() {
  ui.dialogs.close(DIALOG_ID);
}

export function onEditDisciplineChanged() {
  renderEditLayerOptions();
}

function renderEditLayerOptions() {
  const discId = $('editModDiscipline')?.value;
  const disc = (_architecture || []).find(d => d.id === discId);
  const layers = disc?.layers || [];
  const layerSelect = $('editModLayer');
  if (!layerSelect) return;
  const current = layerSelect.value;
  layerSelect.innerHTML = layers.map(l =>
    `<option value="${l.level}">L${l.level} ${escapeHtml(l.name || '')}</option>`
  ).join('');
  if (current && layers.some(l => String(l.level) === current)) {
    layerSelect.value = current;
  }
}

// ═══════════════════════════════════════════
//  CrossWork 工作组切换
// ═══════════════════════════════════════════

function toggleCrossWorkSections() {
  const isCW = !!$('editModIsCrossWorkModule')?.checked;
  const participantsSection = $('editParticipantsSection');
  const depsSection = $('editDepsSection');
  const layerGroup = $('editModLayer')?.closest('.form-group');
  const discGroup = $('editModDiscipline')?.closest('.form-group');
  const ownershipInfo = $('editCwOwnershipInfo');

  if (participantsSection) participantsSection.style.display = isCW ? 'block' : 'none';
  if (depsSection) depsSection.style.display = isCW ? 'none' : 'block';
  if (layerGroup) layerGroup.style.display = isCW ? 'none' : '';
  if (discGroup) discGroup.style.display = isCW ? 'none' : '';
  if (ownershipInfo) ownershipInfo.style.display = isCW ? 'block' : 'none';

  if (isCW) computeCwOwnership();
}

function computeCwOwnership() {
  const participants = getParticipants();
  const discEl = $('editCwDisciplineComputed');
  const layerEl = $('editCwLayerComputed');
  if (!discEl || !layerEl) return;

  if (participants.length === 0) {
    discEl.textContent = '添加成员后自动计算';
    layerEl.textContent = '-';
    return;
  }

  const moduleMap = getModuleInfoMap();
  const disciplines = new Set();
  let maxLayer = 0;
  let allFound = true;

  for (const p of participants) {
    const info = moduleMap[p.moduleName.toLowerCase()];
    if (!info) { allFound = false; continue; }
    disciplines.add(info.discipline);
    if (info.layer > maxLayer) maxLayer = info.layer;
  }

  if (!allFound && disciplines.size === 0) {
    discEl.textContent = '成员模块未找到';
    layerEl.textContent = '-';
    return;
  }

  if (disciplines.size === 1) {
    const disc = [...disciplines][0];
    const discDef = (_architecture || []).find(d => d.id === disc);
    discEl.textContent = `${discDef?.displayName || disc}（部门内工作组）`;
    layerEl.textContent = `L${maxLayer}（成员最高层级）`;
  } else {
    discEl.textContent = '项目（跨部门工作组）';
    layerEl.textContent = '无（跨部门不参与分层）';
  }
}

function getModuleInfoMap() {
  const map = {};
  if (!_manifest?.disciplines) return map;
  for (const [discId, discData] of Object.entries(_manifest.disciplines)) {
    for (const m of (discData.modules || discData || [])) {
      if (m.name) {
        map[m.name.toLowerCase()] = {
          name: m.name,
          discipline: discId,
          layer: m.layer ?? 0
        };
      }
    }
  }
  return map;
}

function addParticipantRow(participant = null) {
  const list = $('editParticipantsList');
  if (!list) return;

  const row = document.createElement('div');
  row.className = 'participant-row';
  row.style.cssText = 'border:1px solid var(--border);border-radius:6px;padding:8px;margin-bottom:6px;';

  const allNames = getAllModuleNames();
  const moduleOptions = allNames.map(n =>
    `<option value="${escapeHtml(n)}"${participant?.moduleName === n ? ' selected' : ''}>${escapeHtml(n)}</option>`
  ).join('');

  row.innerHTML = `
    <div style="display:flex;gap:6px;margin-bottom:4px;">
      <select class="pw-module" style="flex:2;">${moduleOptions}</select>
      <input class="pw-role" placeholder="职责" value="${escapeHtml(participant?.role || '')}" style="flex:1;" />
      <button type="button" class="btn btn-secondary btn-sm pw-remove" style="flex-shrink:0;">x</button>
    </div>
    <div style="display:flex;gap:6px;">
      <input class="pw-contract" placeholder="契约（可选）" value="${escapeHtml(participant?.contract || '')}" style="flex:1;" />
      <input class="pw-deliverable" placeholder="交付物（可选）" value="${escapeHtml(participant?.deliverable || '')}" style="flex:1;" />
    </div>
  `;
  row.querySelector('.pw-remove').addEventListener('click', () => { row.remove(); computeCwOwnership(); });
  row.querySelector('.pw-module').addEventListener('change', () => computeCwOwnership());
  list.appendChild(row);
}

function getParticipants() {
  const list = $('editParticipantsList');
  if (!list) return [];
  return Array.from(list.querySelectorAll('.participant-row')).map(row => ({
    moduleName: row.querySelector('.pw-module')?.value?.trim() || '',
    role: row.querySelector('.pw-role')?.value?.trim() || '',
    contract: row.querySelector('.pw-contract')?.value?.trim() || null,
    deliverable: row.querySelector('.pw-deliverable')?.value?.trim() || null
  })).filter(p => p.moduleName);
}

// ═══════════════════════════════════════════
//  依赖标签输入
// ═══════════════════════════════════════════

function addDepTag(name) {
  if (!name) return;
  const container = $('editDepTags');
  if (!container) return;
  const existing = Array.from(container.querySelectorAll('.dep-tag-name')).map(el => el.textContent);
  if (existing.some(n => n.toLowerCase() === name.toLowerCase())) return;

  const tag = document.createElement('span');
  tag.className = 'dep-tag';
  tag.innerHTML = `<span class="dep-tag-name">${escapeHtml(name)}</span><button type="button" class="dep-tag-remove">x</button>`;
  tag.querySelector('.dep-tag-remove').addEventListener('click', () => tag.remove());
  container.appendChild(tag);

  const searchInput = $('editDepSearch');
  if (searchInput) searchInput.value = '';
  hideDepSuggestions();
}

function getSelectedDeps() {
  const container = $('editDepTags');
  if (!container) return [];
  return Array.from(container.querySelectorAll('.dep-tag-name')).map(el => el.textContent);
}

export function onDepSearchInput() {
  const searchInput = $('editDepSearch');
  const query = searchInput?.value?.trim().toLowerCase();
  const sugBox = $('editDepSuggestions');
  if (!query || query.length < 1 || !sugBox) { hideDepSuggestions(); return; }

  const currentName = $('editModName')?.value?.trim().toLowerCase() || '';
  const alreadySelected = new Set(getSelectedDeps().map(n => n.toLowerCase()));
  const allNames = getAllModuleNames().filter(n =>
    n.toLowerCase().includes(query) &&
    n.toLowerCase() !== currentName &&
    !alreadySelected.has(n.toLowerCase())
  );

  if (allNames.length === 0) { hideDepSuggestions(); return; }

  sugBox.innerHTML = allNames.slice(0, 10).map(n =>
    `<div class="dep-suggestion">${escapeHtml(n)}</div>`
  ).join('');
  sugBox.style.display = 'block';

  sugBox.querySelectorAll('.dep-suggestion').forEach(el => {
    el.addEventListener('click', () => addDepTag(el.textContent));
  });
}

export function onDepSearchKeydown(e) {
  if (e.key === 'Enter') {
    e.preventDefault();
    const sugBox = $('editDepSuggestions');
    const first = sugBox?.querySelector('.dep-suggestion');
    if (first) addDepTag(first.textContent);
  } else if (e.key === 'Escape') {
    hideDepSuggestions();
  }
}

function hideDepSuggestions() {
  const sugBox = $('editDepSuggestions');
  if (sugBox) sugBox.style.display = 'none';
}

// ═══════════════════════════════════════════

export async function saveFromSidebar() {
  const isCW = !!$('editModIsCrossWorkModule')?.checked;
  const discipline = isCW ? computeCwDisciplineValue() : $('editModDiscipline')?.value;
  const module = {
    id: $('editModId')?.value?.trim() || null,
    name: $('editModName')?.value?.trim(),
    path: $('editModPath')?.value?.trim(),
    layer: isCW ? 0 : parseInt($('editModLayer')?.value || '0', 10),
    isCrossWorkModule: isCW,
    participants: isCW ? getParticipants() : [],
    dependencies: isCW ? [] : getSelectedDeps(),
    maintainer: $('editModMaintainer')?.value?.trim() || null
  };

  function computeCwDisciplineValue() {
    const participants = getParticipants();
    const moduleMap = getModuleInfoMap();
    const disciplines = new Set();
    for (const p of participants) {
      const info = moduleMap[p.moduleName.toLowerCase()];
      if (info) disciplines.add(info.discipline);
    }
    return disciplines.size === 1 ? [...disciplines][0] : 'root';
  }

  if (!module.name) { alert('模块名不能为空'); return; }
  if (!module.path) { alert('模块路径不能为空'); return; }

  const res = await api('/modules', { method: 'POST', body: { discipline, module } });
  if (res?.error) { alert('保存失败: ' + res.error); return; }

  alert('模块保存成功');
  closeEditSidebar();

  if (window.refresh) window.refresh();
  if (window.FileTree?.loadFileTree) window.FileTree.loadFileTree();
  if (window.ModuleAdmin?.loadModuleManagement) window.ModuleAdmin.loadModuleManagement();
}

export async function deleteFromSidebar() {
  const name = $('editModName')?.value?.trim();
  if (!name) return;
  if (!confirm(`确认删除模块 "${name}" ?`)) return;

  const res = await api(`/modules/${encodeURIComponent(name)}`, { method: 'DELETE' });
  if (res?.error) { alert('删除失败: ' + res.error); return; }

  closeEditSidebar();
  if (window.refresh) window.refresh();
  if (window.FileTree?.loadFileTree) window.FileTree.loadFileTree();
}

function guessDiscipline(path) {
  const lower = path.toLowerCase();
  if (lower.startsWith('src/') || lower.startsWith('code/')) return 'engineering';
  if (lower.startsWith('art/')) return 'art';
  if (lower.startsWith('design/')) return 'design';
  if (lower.startsWith('devops/') || lower.startsWith('ci/')) return 'devops';
  if (lower.startsWith('qa/') || lower.startsWith('test/')) return 'qa';
  if (lower.startsWith('tools/')) return 'tech-support';
  return null;
}
