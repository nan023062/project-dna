const DEFAULT_NODE_TYPE = 'Technical';
const DEFAULT_MEMORY_TYPE = 'Semantic';
const ALL_NODE_TYPES_OPTION_HTML = '<option value="">All Node Types</option>';

const NODE_TYPE_NAME_TO_VALUE = {
  Project: 0,
  Department: 1,
  Technical: 2,
  Team: 3
};

const NODE_TYPE_VALUE_TO_NAME = Object.fromEntries(
  Object.entries(NODE_TYPE_NAME_TO_VALUE).map(([key, value]) => [value, key])
);

const TYPE_NAME_TO_VALUE = {
  Structural: 0,
  Semantic: 1,
  Episodic: 2,
  Working: 3,
  Procedural: 4
};

const TYPE_VALUE_TO_NAME = Object.fromEntries(
  Object.entries(TYPE_NAME_TO_VALUE).map(([key, value]) => [value, key])
);

const SOURCE_NAME_TO_VALUE = {
  Human: 2
};

const NODE_TYPE_OPTIONS = [
  { value: 'Project', label: 'Project' },
  { value: 'Department', label: 'Department' },
  { value: 'Technical', label: 'Technical' },
  { value: 'Team', label: 'Team' }
];

function normalizeNodeTypeName(nodeType) {
  if (typeof nodeType === 'number') return NODE_TYPE_VALUE_TO_NAME[nodeType] ?? DEFAULT_NODE_TYPE;
  if (nodeType === 'ProjectVision') return 'Project';
  if (nodeType === 'DisciplineStandard') return 'Department';
  if (nodeType === 'CrossDiscipline') return 'Team';
  if (nodeType === 'FeatureSystem') return DEFAULT_NODE_TYPE;
  if (nodeType === 'Implementation') return 'Team';
  if (nodeType === 'Group') return DEFAULT_NODE_TYPE;
  return nodeType || DEFAULT_NODE_TYPE;
}

function normalizeTypeName(type) {
  if (typeof type === 'number') return TYPE_VALUE_TO_NAME[type] ?? DEFAULT_MEMORY_TYPE;
  return type || DEFAULT_MEMORY_TYPE;
}

function syncNodeTypeSelectOptions(getById) {
  const filterSelect = getById('memFilterLayer');
  if (filterSelect) {
    const current = normalizeNodeTypeName(filterSelect.value || '');
    filterSelect.innerHTML = [
      ALL_NODE_TYPES_OPTION_HTML,
      ...NODE_TYPE_OPTIONS.map(option => `<option value="${option.value}">${option.label}</option>`)
    ].join('');
    filterSelect.value = NODE_TYPE_OPTIONS.some(option => option.value === current) ? current : '';
  }

  const editorSelect = getById('memLayer');
  if (editorSelect) {
    const current = normalizeNodeTypeName(editorSelect.value || '');
    editorSelect.innerHTML = NODE_TYPE_OPTIONS
      .map(option => `<option value="${option.value}">${option.label}</option>`)
      .join('');
    editorSelect.value = NODE_TYPE_OPTIONS.some(option => option.value === current) ? current : DEFAULT_NODE_TYPE;
  }
}

function parseMemoryTimestamp(memory) {
  const created = memory?.createdAt ?? memory?.created_at;
  if (!created) return 0;
  const timestamp = Date.parse(created);
  return Number.isFinite(timestamp) ? timestamp : 0;
}

function sortMemoriesByRecent(memories = []) {
  return memories
    .map((memory, index) => ({ memory, index, timestamp: parseMemoryTimestamp(memory) }))
    .sort((left, right) => {
      if (right.timestamp !== left.timestamp) return right.timestamp - left.timestamp;
      return left.index - right.index;
    })
    .map(item => item.memory);
}

export {
  NODE_TYPE_NAME_TO_VALUE,
  NODE_TYPE_VALUE_TO_NAME,
  TYPE_NAME_TO_VALUE,
  TYPE_VALUE_TO_NAME,
  SOURCE_NAME_TO_VALUE,
  NODE_TYPE_OPTIONS,
  normalizeNodeTypeName,
  normalizeTypeName,
  syncNodeTypeSelectOptions,
  parseMemoryTimestamp,
  sortMemoriesByRecent
};
