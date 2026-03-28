import { $, api } from '../utils.js';

let lastCondenseResult = null;

export async function loadGovernanceStats() {
  try {
    await loadCondenseSchedule();
    const stats = await api('/memory/stats');
    if (!stats) return;

    $('statFresh').textContent = stats.byFreshness?.Fresh || 0;
    $('statAging').textContent = stats.byFreshness?.Aging || 0;
    $('statStale').textContent = stats.byFreshness?.Stale || 0;
    $('statConflict').textContent = stats.conflictCount || 0;
    
    renderGovernanceContent(stats);
  } catch (err) {
    console.error('加载治理统计失败', err);
    $('govContent').innerHTML = `<div class="empty error">加载失败: ${err.message}</div>`;
  }
}

function renderGovernanceContent(stats) {
  const html = [];
  
  if (stats.conflictCount > 0) {
    html.push(`
      <div class="gov-section">
        <h3>⚠️ 存在冲突的记忆 (${stats.conflictCount})</h3>
        <div class="desc">这些记忆存在相互矛盾的描述，建议人工介入清理或通过 AI 重新蒸馏。</div>
      </div>
    `);
  }
  
  if (stats.byFreshness?.Stale > 0) {
    html.push(`
      <div class="gov-section" style="margin-top: 20px;">
        <h3>⏳ 陈旧记忆 (${stats.byFreshness.Stale})</h3>
        <div class="desc">这些记忆关联的代码文件已经发生了大量变更，记忆内容可能已经过时。</div>
      </div>
    `);
  }
  
  if (html.length === 0) {
    html.push(`<div class="empty">🎉 当前项目记忆库非常健康，没有发现冲突或陈旧知识。</div>`);
  }
  
  $('govContent').innerHTML = html.join('');
}

export async function checkFreshness() {
  try {
    const res = await api('/memory/governance/check-freshness', { method: 'POST' });
    alert(res.message);
    loadGovernanceStats();
  } catch (err) {
    alert('操作失败: ' + err.message);
  }
}

export async function detectConflicts() {
  try {
    const res = await api('/memory/governance/detect-conflicts', { method: 'POST' });
    alert(res.message);
    loadGovernanceStats();
  } catch (err) {
    alert('操作失败: ' + err.message);
  }
}

export async function archiveStale() {
  if (!confirm('确定要归档所有超过 30 天的陈旧记忆吗？归档后将不再参与主动检索。')) return;
  try {
    const res = await api('/memory/governance/archive-stale', { method: 'POST' });
    alert(res.message);
    loadGovernanceStats();
  } catch (err) {
    alert('操作失败: ' + err.message);
  }
}

export async function condenseNodeKnowledge() {
  const nodeIdOrName = prompt('请输入模块名或节点 ID：');
  if (!nodeIdOrName) return;

  try {
    const res = await api('/governance/condense/node', {
      method: 'POST',
      body: JSON.stringify({
        nodeIdOrName: nodeIdOrName.trim(),
        maxSourceMemories: 200
      })
    });
    lastCondenseResult = { mode: 'single', data: res };
    renderCondenseResult();
    loadGovernanceStats();
  } catch (err) {
    alert('压缩失败: ' + err.message);
  }
}

export async function condenseAllKnowledge() {
  if (!confirm('确定执行全量知识压缩吗？该操作会归档已提炼的短期记忆。')) return;

  try {
    const res = await api('/governance/condense/all', {
      method: 'POST',
      body: JSON.stringify({ maxSourceMemories: 200 })
    });
    lastCondenseResult = { mode: 'all', data: res };
    renderCondenseResult();
    loadGovernanceStats();
  } catch (err) {
    alert('全量压缩失败: ' + err.message);
  }
}

export async function configureCondenseSchedule() {
  let config;
  try {
    const current = await api('/config');
    config = current?.governance?.condenseSchedule || { enabled: true, hourLocal: 2, maxSourceMemories: 200 };
  } catch {
    config = { enabled: true, hourLocal: 2, maxSourceMemories: 200 };
  }

  const enabledInput = prompt('启用自动压缩调度？输入 true/false', String(config.enabled));
  if (enabledInput == null) return;
  const enabled = String(enabledInput).trim().toLowerCase() !== 'false';

  const hourInput = prompt('每日执行小时（0-23，本地时间）', String(config.hourLocal));
  if (hourInput == null) return;
  const hourLocal = Number.parseInt(hourInput, 10);
  if (!Number.isFinite(hourLocal) || hourLocal < 0 || hourLocal > 23) {
    alert('小时输入无效，请输入 0-23');
    return;
  }

  const maxInput = prompt('每节点最多参与提炼的源记忆数（20-2000）', String(config.maxSourceMemories));
  if (maxInput == null) return;
  const maxSourceMemories = Number.parseInt(maxInput, 10);
  if (!Number.isFinite(maxSourceMemories) || maxSourceMemories < 20 || maxSourceMemories > 2000) {
    alert('maxSourceMemories 输入无效，请输入 20-2000');
    return;
  }

  try {
    await api('/config/governance/condense-schedule', {
      method: 'POST',
      body: JSON.stringify({ enabled, hourLocal, maxSourceMemories })
    });
    await loadCondenseSchedule();
    alert('调度配置已更新');
  } catch (err) {
    alert('更新失败: ' + err.message);
  }
}

async function loadCondenseSchedule() {
  const host = $('govSchedule');
  if (!host) return;
  try {
    const cfg = await api('/config');
    const s = cfg?.governance?.condenseSchedule;
    if (!s) {
      host.textContent = '调度: 未配置';
      return;
    }
    host.textContent = `调度: ${s.enabled ? '开启' : '关闭'} · 每日 ${String(s.hourLocal).padStart(2, '0')}:00 · max=${s.maxSourceMemories}`;
  } catch {
    host.textContent = '调度: 读取失败';
  }
}

function renderCondenseResult() {
  const host = $('govCondenseResult');
  if (!host) return;

  if (!lastCondenseResult) {
    host.innerHTML = '<div class="empty">尚未执行知识压缩。</div>';
    return;
  }

  if (lastCondenseResult.mode === 'single') {
    const r = lastCondenseResult.data;
    host.innerHTML = `
      <div class="gov-section">
        <h3>知识压缩结果（单模块）</h3>
        <div class="desc">
          节点: ${r.nodeName || r.nodeId} ｜ 源记忆: ${r.sourceCount} ｜ 归档: ${r.archivedCount} ｜ 新摘要: ${r.newIdentityMemoryId || '无'}
        </div>
        ${r.summary ? `<div class="desc" style="margin-top:6px;">摘要: ${escapeHtmlText(r.summary)}</div>` : ''}
      </div>
    `;
    return;
  }

  const d = lastCondenseResult.data || {};
  const rows = (d.results || []).slice(0, 20).map(r => `
    <li>${escapeHtmlText(r.nodeName || r.nodeId)}：source=${r.sourceCount}, archived=${r.archivedCount}, identity=${r.newIdentityMemoryId || '无'}</li>
  `).join('');

  host.innerHTML = `
    <div class="gov-section">
      <h3>知识压缩结果（全量）</h3>
      <div class="desc">节点总数: ${d.total || 0} ｜ 产生摘要: ${d.condensed || 0} ｜ 归档记忆: ${d.archived || 0}</div>
      <ul style="margin:8px 0 0 18px; padding:0; font-size:12px; color:var(--text-secondary);">
        ${rows || '<li>无明细</li>'}
      </ul>
    </div>
  `;
}

function escapeHtmlText(text) {
  return String(text)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;');
}
