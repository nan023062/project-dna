function getActiveModelName(providers, activeProviderId, fallback = 'Unavailable') {
  const activeProvider = (providers || []).find(provider => provider.id === activeProviderId);
  return activeProvider?.model || fallback;
}

function updateModelTag(getById, model, fallback = 'Unavailable') {
  const element = getById('chatModelTag');
  if (element) element.textContent = model || fallback;
}

function getProviderBadgeClass(providerType) {
  return providerType === 'anthropic' ? 'anthropic' : 'openai';
}

function renderModelDropdownHtml({
  providers,
  activeProviderId,
  emptyHtml,
  selectActionAttribute = 'data-chat-action',
  selectActionName = 'select-provider',
  settingsActionAttribute = 'data-chat-action',
  settingsActionName = 'open-llm-settings',
  settingsLabel = 'Settings',
  footerNote = ''
}) {
  let html = '';

  if (!providers || providers.length === 0) {
    html = emptyHtml || '<div class="model-dd-empty">No provider configured.</div>';
  } else {
    for (const provider of providers) {
      const activeClass = provider.id === activeProviderId ? ' active' : '';
      html += `<div class="model-dd-item${activeClass}" ${selectActionAttribute}="${selectActionName}" data-provider-id="${encodeURIComponent(provider.id)}">`
        + `<span class="model-dd-name">${provider.model}</span>`
        + `<span class="model-dd-provider">${provider.name}</span>`
        + '</div>';
    }
  }

  html += '<div class="model-dd-divider"></div>';
  if (footerNote) {
    html += `<div class="model-dd-empty">${footerNote}</div>`;
  }
  html += `<div class="model-dd-item settings" ${settingsActionAttribute}="${settingsActionName}" data-close-dropdown="true">${settingsLabel}</div>`;

  return html;
}

export {
  getActiveModelName,
  updateModelTag,
  getProviderBadgeClass,
  renderModelDropdownHtml
};
