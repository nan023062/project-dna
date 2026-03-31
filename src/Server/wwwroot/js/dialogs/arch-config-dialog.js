import { $, api, escapeHtml } from '../utils.js';
import { requestRefresh } from '../app-runtime.js';
import { ui } from '/dna-shared/js/ui/ui-manager.js';
import { loadModuleManagement, newDiscipline, saveDiscipline, deleteDiscipline, addLayerRow } from '../panels/arch-config.js';

const DIALOG_ID = 'arch-config';

export function openArchConfigDialog() {
  const formHtml = `
    <div style="min-width:400px;">
      <div id="disciplineList" class="module-admin-list"></div>
      <div class="module-admin-form">
        <input id="discId" placeholder="部门 ID（如 engineering / art / design）" />
        <input id="discDisplayName" placeholder="显示名称（如 工程部、美术部）" />
        <div class="module-admin-form" style="gap:4px;">
          <label style="font-size:12px;color:var(--text-secondary);">工种</label>
          <select id="discRoleId">
            <option value="coder">coder（程序）</option>
            <option value="designer">designer（策划）</option>
            <option value="art">art（美术）</option>
          </select>
        </div>
        <label style="font-size:12px;color:var(--text-secondary);margin-top:8px;">层级定义</label>
        <div id="discLayers" style="display:flex;flex-direction:column;gap:6px;"></div>
        <button type="button" class="btn btn-secondary btn-sm" style="align-self:flex-start;" id="archAddLayerBtn">+ 添加层级</button>
      </div>
      <div class="module-admin-actions" style="margin-top:8px;">
        <button class="btn btn-secondary btn-sm" id="archNewBtn">新建</button>
        <button class="btn btn-primary btn-sm" id="archSaveBtn">保存</button>
        <button class="btn btn-secondary btn-sm" id="archDeleteBtn">删除</button>
      </div>
    </div>
  `;

  const dialog = ui.dialogs.open({
    id: DIALOG_ID,
    title: '组织架构配置（部门 + 层级 + 工种）',
    content: formHtml,
    className: 'arch-config-dialog',
    onClose: () => { requestRefresh(); }
  });

  dialog.bodyEl.querySelector('#archAddLayerBtn').addEventListener('click', addLayerRow);
  dialog.bodyEl.querySelector('#archNewBtn').addEventListener('click', newDiscipline);
  dialog.bodyEl.querySelector('#archSaveBtn').addEventListener('click', saveDiscipline);
  dialog.bodyEl.querySelector('#archDeleteBtn').addEventListener('click', deleteDiscipline);

  loadModuleManagement();
}
