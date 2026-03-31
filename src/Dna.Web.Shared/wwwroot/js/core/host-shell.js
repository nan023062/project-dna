function formatCompactNumber(value, locale = 'zh-CN') {
  if (!Number.isFinite(value)) return '-';

  try {
    return new Intl.NumberFormat(locale, {
      notation: 'compact',
      maximumFractionDigits: 1
    }).format(value);
  } catch {
    return String(value);
  }
}

function registerFullscreenTabs(ui, tabDefinitions, getById) {
  for (const definition of tabDefinitions) {
    const tabButtonEl = definition.tabButtonEl
      ?? document.querySelector(definition.tabButtonSelector ?? `[data-tab="${definition.id}"]`);
    const panelEl = definition.panelEl
      ?? getById(definition.panelId ?? `panel${definition.id.charAt(0).toUpperCase()}${definition.id.slice(1)}`);

    ui.fullscreen.registerTab(definition.id, {
      tabButtonEl,
      panelEl,
      onActivate: definition.onActivate ?? null,
      onDeactivate: definition.onDeactivate ?? null
    });
  }
}

function renderSidebarMessage(getById, containerId, title, message) {
  const sidebar = getById(containerId);
  if (!sidebar) return;

  sidebar.style.display = 'flex';
  sidebar.innerHTML = `
    <div class="sidebar-section">
      <div class="sidebar-title">${title}</div>
      <div class="sidebar-text">${message}</div>
    </div>
  `;
}

function resetMetricValues(getById, metricIds) {
  for (const metricId of metricIds) {
    const element = getById(metricId);
    if (!element) continue;

    element.textContent = '-';
    element.title = '';
  }
}

function applyMetricValues(getById, metrics, formatter = formatCompactNumber) {
  for (const metric of metrics) {
    const element = getById(metric.id);
    if (!element) continue;

    element.textContent = formatter(metric.value);
    element.title = String(metric.titleValue ?? metric.value ?? '');
  }
}

export {
  formatCompactNumber,
  registerFullscreenTabs,
  renderSidebarMessage,
  resetMetricValues,
  applyMetricValues
};
