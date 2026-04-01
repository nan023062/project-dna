import { $, api, escapeHtml } from '../utils.js';

function setText(id, value, fallback = '-') {
  const element = $(id);
  if (!element) return;
  const text = value == null || value === '' ? fallback : String(value);
  element.textContent = text;
}

function formatDate(value) {
  if (!value) return '-';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString();
}

function renderHealthItems(items) {
  const host = $('overviewHealthList');
  if (!host) return;

  host.innerHTML = items.map(item => `
    <div class="overview-health-item ${item.tone}">
      <div class="title">${escapeHtml(item.title)}</div>
      <div class="desc">${escapeHtml(item.desc)}</div>
    </div>
  `).join('');
}

function renderNextActions(items) {
  const host = $('overviewNextActions');
  if (!host) return;

  host.innerHTML = items.map(item => `<li>${escapeHtml(item)}</li>`).join('');
}

export async function loadServiceOverview() {
  const [status, memoryStats, whitelist] = await Promise.all([
    api('/status'),
    api('/memory/stats'),
    api('/connection/whitelist')
  ]);

  const allowlist = status?.allowlist || {};
  const users = status?.users || {};
  const byFreshness = memoryStats?.byFreshness || {};

  setText('overviewServiceState', status?.configured ? '已配置并运行中' : '服务在线，等待项目配置');
  setText('overviewWhitelistCount', `${allowlist.enabled ?? 0} / ${allowlist.total ?? 0}`);
  setText('overviewUserCount', `${users.total ?? 0}（admin ${users.admin ?? 0}）`);
  setText('overviewMemoryCount', memoryStats?.total ?? 0);

  setText('overviewTransport', status?.transport);
  setText('overviewMode', status?.productMode);
  setText('overviewProjectRoot', status?.projectRoot || '未配置');
  setText('overviewStorePath', status?.storePath || status?.dataPath || '未配置');
  setText('overviewDataPath', status?.dataPath || '未配置');
  setText('overviewModuleCount', status?.moduleCount ?? 0);
  setText('overviewUptime', status?.uptime || '-');
  setText('overviewStartedAt', formatDate(status?.startedAt));
  setText('overviewAllowlistUpdatedAt', formatDate(allowlist.updatedAtUtc));
  setText('overviewFreshness', `Fresh ${byFreshness.Fresh || 0} / Aging ${byFreshness.Aging || 0} / Stale ${byFreshness.Stale || 0}`);
  setText('overviewWhitelistSummary', `启用 ${allowlist.enabled ?? 0}，admin ${allowlist.admin ?? 0}`);
  setText('overviewUserSummary', `共 ${users.total ?? 0} 个账号，editor ${users.editor ?? 0}，viewer ${users.viewer ?? 0}`);

  const healthItems = [];
  if (!status?.configured) {
    healthItems.push({
      tone: 'warn',
      title: '项目未配置',
      desc: 'Server 已启动，但尚未绑定目标项目根目录。'
    });
  } else {
    healthItems.push({
      tone: 'ok',
      title: '服务在线',
      desc: `项目 ${status.projectName || '已配置项目'} 正在运行，累计模块 ${status.moduleCount ?? 0} 个。`
    });
  }

  if ((allowlist.enabled ?? 0) === 0) {
    healthItems.push({
      tone: 'warn',
      title: '连接权限为空',
      desc: '当前没有启用的白名单条目，Client 将无法接入。'
    });
  } else {
    healthItems.push({
      tone: 'ok',
      title: '连接权限可用',
      desc: `当前白名单启用 ${allowlist.enabled ?? 0} 条，最近更新时间 ${formatDate(allowlist.updatedAtUtc)}。`
    });
  }

  if ((memoryStats?.conflictCount ?? 0) > 0 || (byFreshness.Stale ?? 0) > 0) {
    healthItems.push({
      tone: 'warn',
      title: '知识治理待处理',
      desc: `冲突 ${memoryStats?.conflictCount ?? 0} 条，陈旧知识 ${byFreshness.Stale ?? 0} 条。`
    });
  } else {
    healthItems.push({
      tone: 'ok',
      title: '知识治理稳定',
      desc: '当前未发现明显冲突，治理侧可作为例行维护。'
    });
  }

  renderHealthItems(healthItems);

  const actions = [
    status?.configured
      ? '优先检查“连接权限”中的回环地址与本机网卡地址是否都已放行。'
      : '先完成项目根目录与数据目录配置，再继续进行知识治理。',
    '确认 Client 桌面端能够连接 Server，并正确显示当前角色边界。',
    whitelist?.entries?.length
      ? '若这是单人本地管理员场景，保持至少一个 admin 白名单条目与一个 admin 账号。'
      : '先创建第一条白名单，保证本机可进入管理台与 Client。'
  ];
  renderNextActions(actions);
}
