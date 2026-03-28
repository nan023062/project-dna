import { $, api } from '../utils.js';

export async function loadGovernanceStats() {
  try {
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
