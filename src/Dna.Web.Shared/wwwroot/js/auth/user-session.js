function normalizeUserRole(user) {
  return String(user?.role || '').trim().toLowerCase();
}

function isAdminUser(user) {
  return normalizeUserRole(user) === 'admin';
}

function formatUserIdentity(user, fallback = '-') {
  if (!user) return fallback;

  const username = String(user.username || user.id || '').trim();
  const role = String(user.role || '').trim();
  if (username && role) return `${username} (${role})`;
  return username || role || fallback;
}

export {
  normalizeUserRole,
  isAdminUser,
  formatUserIdentity
};
