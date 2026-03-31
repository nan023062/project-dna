const DEFAULT_FIELD_IDS = {
  summary: 'memSummary',
  nodeType: 'memLayer',
  type: 'memType',
  background: 'memFieldBackground',
  goal: 'memFieldGoal',
  rules: 'memFieldRules',
  steps: 'memFieldSteps',
  notes: 'memFieldNotes',
  content: 'memContent'
};

const DEFAULT_BIND_FIELD_IDS = [
  DEFAULT_FIELD_IDS.summary,
  DEFAULT_FIELD_IDS.nodeType,
  DEFAULT_FIELD_IDS.type,
  DEFAULT_FIELD_IDS.background,
  DEFAULT_FIELD_IDS.goal,
  DEFAULT_FIELD_IDS.rules,
  DEFAULT_FIELD_IDS.steps,
  DEFAULT_FIELD_IDS.notes
];

function splitLines(value) {
  return (value || '')
    .split('\n')
    .map(item => item.trim())
    .filter(Boolean);
}

function readStructuredMemoryFields(getById, fieldIds = DEFAULT_FIELD_IDS) {
  return {
    summary: getById(fieldIds.summary)?.value?.trim() || '',
    nodeType: getById(fieldIds.nodeType)?.value || '',
    type: getById(fieldIds.type)?.value || '',
    background: getById(fieldIds.background)?.value?.trim() || '',
    goal: getById(fieldIds.goal)?.value?.trim() || '',
    rules: splitLines(getById(fieldIds.rules)?.value || ''),
    steps: splitLines(getById(fieldIds.steps)?.value || ''),
    notes: getById(fieldIds.notes)?.value?.trim() || '',
    content: getById(fieldIds.content)?.value?.trim() || ''
  };
}

function bindStructuredFieldListeners({
  getById,
  queryAll,
  onUpdate,
  fieldIds = DEFAULT_BIND_FIELD_IDS,
  disciplineSelector = '#memDisciplines input[type="checkbox"]'
}) {
  for (const fieldId of fieldIds) {
    const element = getById(fieldId);
    if (!element) continue;

    element.addEventListener('input', onUpdate);
    if (element.tagName === 'SELECT') {
      element.addEventListener('change', onUpdate);
    }
  }

  for (const element of queryAll(disciplineSelector)) {
    element.addEventListener('change', onUpdate);
  }
}

function clearStructuredFieldValues(getById, fieldIds = DEFAULT_FIELD_IDS) {
  getById(fieldIds.background).value = '';
  getById(fieldIds.goal).value = '';
  getById(fieldIds.rules).value = '';
  getById(fieldIds.steps).value = '';
  getById(fieldIds.notes).value = '';
}

function fillStructuredTemplate(getById, template, fieldIds = DEFAULT_FIELD_IDS) {
  getById(fieldIds.summary).value = template.summary || '';
  getById(fieldIds.background).value = template.background || '';
  getById(fieldIds.goal).value = template.goal || '';
  getById(fieldIds.rules).value = Array.isArray(template.rules) ? template.rules.join('\n') : '';
  getById(fieldIds.steps).value = Array.isArray(template.steps) ? template.steps.join('\n') : '';
  getById(fieldIds.notes).value = template.notes || '';
}

function isStructuredDraftEmpty(getById, fieldIds = DEFAULT_FIELD_IDS) {
  return ![
    getById(fieldIds.background).value,
    getById(fieldIds.goal).value,
    getById(fieldIds.rules).value,
    getById(fieldIds.steps).value,
    getById(fieldIds.notes).value,
    getById(fieldIds.content).value
  ].some(value => String(value || '').trim().length > 0);
}

export {
  DEFAULT_FIELD_IDS,
  splitLines,
  readStructuredMemoryFields,
  bindStructuredFieldListeners,
  clearStructuredFieldValues,
  fillStructuredTemplate,
  isStructuredDraftEmpty
};
