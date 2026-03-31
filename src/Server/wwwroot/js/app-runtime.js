const runtime = {
  refreshHandler: null,
  editSidebarOpener: null,
  fileTreeActions: null,
  moduleAdminActions: null
};

export function registerRefreshHandler(handler) {
  runtime.refreshHandler = typeof handler === 'function' ? handler : null;
}

export function requestRefresh(force = true) {
  return runtime.refreshHandler?.(force);
}

export function registerEditSidebarOpener(opener) {
  runtime.editSidebarOpener = typeof opener === 'function' ? opener : null;
}

export function openRegisteredEditSidebar(options = {}) {
  return runtime.editSidebarOpener?.(options);
}

export function registerFileTreeActions(actions) {
  runtime.fileTreeActions = actions || null;
}

export function getFileTreeActions() {
  return runtime.fileTreeActions;
}

export function registerModuleAdminActions(actions) {
  runtime.moduleAdminActions = actions || null;
}

export function getModuleAdminActions() {
  return runtime.moduleAdminActions;
}
