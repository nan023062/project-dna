function bindDelegatedDocumentEvents(definitions, root = document) {
  if (!root || typeof root.addEventListener !== 'function') {
    return () => {};
  }

  const listeners = [];

  for (const definition of definitions) {
    const listener = event => {
      const element = event.target?.closest?.(definition.selector);
      if (!element) return;
      if (definition.within && !element.closest(definition.within)) return;
      if (definition.shouldHandle && !definition.shouldHandle({ event, element })) return;
      if (definition.preventDefault) event.preventDefault();

      definition.handler({ event, element });
    };

    root.addEventListener(definition.eventName, listener);
    listeners.push({ eventName: definition.eventName, listener });
  }

  return () => {
    for (const { eventName, listener } of listeners) {
      root.removeEventListener(eventName, listener);
    }
  };
}

export { bindDelegatedDocumentEvents };
