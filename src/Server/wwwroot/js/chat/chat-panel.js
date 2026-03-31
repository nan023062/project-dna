/**
 * 鑱婂ぉ闈㈡澘妯″潡
 * - 绠＄悊鑱婂ぉ闈㈡澘 UI 鐘舵€侊紙鎵撳紑/鍏抽棴/鏂板璇濓級
 * - 鍙戦€佹秷鎭笌鎺ユ敹 SSE 娴佸紡鍝嶅簲
 * - 娓叉煋鐢ㄦ埛/鍔╂墜娑堟伅銆佸伐鍏疯皟鐢ㄧ姸鎬?
 */

import { $, api, apiFetch } from '../utils.js';
import { getProviderList, getActiveProviderId, switchProvider, openLlmSettings } from './llm-settings.js';
import { bindDelegatedDocumentEvents } from '/dna-shared/js/core/dom-actions.js';
import {
  renderModelDropdownHtml,
  updateModelTag as updateSharedModelTag
} from '/dna-shared/js/chat/provider-ui.js';

let chatOpen = true;
let messages = [];
let isStreaming = false;
let currentController = null;
let chatMode = 'agent';
let sessionId = generateSessionId();
let messageQueue = [];
let lastRunInterrupted = false;
let isQueueProcessing = false;
let actionSequence = 0;
const actionButtonMap = new Map();
let viewingSessionList = false;
let cachedChatHTML = '';
let chatUiEventsBound = false;

window.addEventListener('beforeunload', () => { saveCurrentSession(); });

async function handleChatUiAction(action, element) {
  switch (action) {
    case 'show-session-list':
      await showSessionList();
      break;
    case 'load-session': {
      const id = element?.dataset.sessionId ? decodeURIComponent(element.dataset.sessionId) : '';
      if (id) {
        await loadSession(id);
      }
      break;
    }
    case 'toggle-tool-group':
    case 'toggle-tool-card':
      element.parentElement?.classList.toggle('expanded');
      break;
    case 'edit-queue-item': {
      const index = Number(element?.dataset.queueIndex);
      if (Number.isInteger(index)) {
        editQueueItem(index);
      }
      break;
    }
    case 'remove-queue-item': {
      const index = Number(element?.dataset.queueIndex);
      if (Number.isInteger(index)) {
        removeQueueItem(index);
      }
      break;
    }
    case 'continue-chat':
      continueChatFromLimit();
      break;
    case 'open-llm-settings':
      await openLlmSettings();
      if (element?.dataset.closeDropdown === 'true') {
        closeDd();
      }
      break;
    case 'select-provider': {
      const providerId = element?.dataset.providerId ? decodeURIComponent(element.dataset.providerId) : '';
      if (providerId) {
        await selectProvider(providerId);
      }
      break;
    }
    case 'keep-edit':
      await keepEdit(element?.dataset.editId || '', element);
      break;
    case 'undo-edit':
      await undoEdit(element?.dataset.editId || '', element?.dataset.path || '', element);
      break;
    case 'begin-task-knowledge':
      beginTaskFromKnowledgeCard(element?.dataset.moduleName || '', element);
      break;
    case 'ask-clarifying':
      askClarifyingFromKnowledgeCard(element?.dataset.question || '', element);
      break;
    case 'queue-dependency-validation':
      queueDependencyValidationFromKnowledgeCard(
        element?.dataset.caller || '',
        element?.dataset.callee || '',
        element
      );
      break;
    case 'run-governance-check':
      runGovernanceCheckFromKnowledgeCard(element);
      break;
    case 'run-suggested-action':
      runSuggestedActionFromKnowledgeCard(
        element?.dataset.prompt || '',
        element?.dataset.display || '',
        element
      );
      break;
    default:
      break;
  }
}

function bindChatUiEvents() {
  if (chatUiEventsBound || typeof document === 'undefined') {
    return;
  }

  chatUiEventsBound = true;

  bindDelegatedDocumentEvents([
    {
      eventName: 'click',
      selector: '[data-chat-action]',
      preventDefault: true,
      shouldHandle: ({ element }) => Boolean(element.closest('#chatMessages, #chatQueue, #chatModelDropdown')),
      handler: ({ element }) => void handleChatUiAction(element.dataset.chatAction, element)
    }
  ]);
}

export function toggleChat() {
  // 鍙充晶 AI 闈㈡澘鏀逛负甯搁┗锛氫笉鍐嶆敮鎸佸紑鍏炽€?
  chatOpen = true;
  const panel = $('chatPanel');
  const btn = $('chatToggleBtn');
  panel.classList.remove('collapsed');
  if (btn) btn.classList.toggle('active', chatOpen);

  if (chatOpen && messages.length === 0) {
    showWelcome();
  }
}

export function isChatOpen() { return chatOpen; }

export function newChat() {
  saveCurrentSession();
  messages = [];
  sessionId = generateSessionId();
  isStreaming = false;
  viewingSessionList = false;
  if (currentController) {
    currentController.abort();
    currentController = null;
  }
  showWelcome();
  updateSendButton();
}

export async function loadSession(id) {
  try {
    const session = await api(`/agent/sessions/${encodeURIComponent(id)}`);
    sessionId = session.id;
    chatMode = (session.mode || 'agent').toLowerCase();
    messages = session.messages || [];
    viewingSessionList = false;
    lastRunInterrupted = false;
    const btns = document.querySelectorAll('.chat-mode-btn');
    btns.forEach(b => b.classList.toggle('active', b.dataset.mode === chatMode));
    renderMessages();
  } catch (err) {
    console.warn('[chat] loadSession failed:', err);
  }
}

export async function showSessionList() {
  const container = $('chatMessages');

  if (viewingSessionList) {
    exitSessionList();
    return;
  }

  saveCurrentSession();

  cachedChatHTML = container.innerHTML;
  viewingSessionList = true;

  container.innerHTML = '<div class="session-list-loading">鍔犺浇涓?..</div>';

  try {
    const data = await api('/agent/sessions');
    const resp = { ok: true };
    if (!resp.ok) {
      container.innerHTML = '<div class="chat-welcome"><p>鏃犳硶鍔犺浇鍘嗗彶浼氳瘽</p></div>';
      return;
    }
    const sessions = data.sessions || [];

    let html = `<div class="session-list-header">
      <span class="session-list-title">鍘嗗彶浼氳瘽</span>
      <button class="session-list-back-btn" data-chat-action="show-session-list" title="杩斿洖褰撳墠瀵硅瘽">鉁?/button>
    </div>`;

    if (sessions.length === 0) {
      html += '<div class="session-list-empty">鏆傛棤鍘嗗彶浼氳瘽</div>';
    } else {
      html += '<div class="session-list-items">';
      for (const s of sessions) {
        const isActive = s.id === sessionId;
        const modeLabel = { agent: 'Agent', chat: 'Chat', plan: 'Plan' }[(s.mode || '').toLowerCase()] || s.mode || '';
        const time = s.updatedAt ? new Date(s.updatedAt).toLocaleString() : '';
        const title = s.title || '鏃犳爣棰?;
        const msgCount = s.messageCount || 0;
        html += `<div class="chat-session-item${isActive ? ' active' : ''}" data-chat-action="load-session" data-session-id="${encodeURIComponent(s.id)}">
          <div class="chat-session-title">${escapeHtml(title)}</div>
          <div class="chat-session-meta">${modeLabel}${msgCount > 0 ? ' 路 ' + msgCount + ' 鏉℃秷鎭? : ''}${time ? ' 路 ' + time : ''}</div>
          ${isActive ? '<div class="chat-session-badge">褰撳墠</div>' : ''}
        </div>`;
      }
      html += '</div>';
    }

    container.innerHTML = html;
  } catch (err) {
    console.warn('[chat] showSessionList failed:', err);
    container.innerHTML = '<div class="chat-welcome"><p>鍔犺浇鍘嗗彶浼氳瘽澶辫触</p></div>';
  }
}

function exitSessionList() {
  viewingSessionList = false;
  const container = $('chatMessages');
  if (cachedChatHTML) {
    container.innerHTML = cachedChatHTML;
    cachedChatHTML = '';
  } else {
    if (messages.length > 0) renderMessages();
    else showWelcome();
  }
  scrollToBottom();
}

function renderMessages() {
  const container = $('chatMessages');
  container.innerHTML = '';
  let toolCount = 0;
  for (let i = 0; i < messages.length; i++) {
    const m = messages[i];
    if (m.role === 'user') appendUserMessage(m.content || '');
    else if (m.role === 'assistant' && m.content) {
      const div = document.createElement('div');
      div.className = 'chat-msg assistant';
      div.innerHTML = `<div class="chat-msg-bubble">${typeof marked !== 'undefined' ? marked.parse(m.content) : escapeHtml(m.content)}</div>`;
      container.appendChild(div);
    } else if (m.role === 'tool') {
      toolCount++;
    } else if (m.role === 'assistant' && (m.tool_calls?.length > 0 || m.toolCalls?.length > 0)) {
      toolCount = (m.tool_calls || m.toolCalls).length;
    }

    if (toolCount > 0 && (i === messages.length - 1 || (messages[i+1] && messages[i+1].role !== 'tool'))) {
      const group = document.createElement('div');
      group.className = 'chat-tool-group done';
      group.innerHTML = `<div class="chat-tool-summary" data-chat-action="toggle-tool-group"><span class="tool-group-icon">鈿?/span><span class="tool-summary-text">浣跨敤浜?${toolCount} 涓伐鍏?/span></div>`;
      container.appendChild(group);
      toolCount = 0;
    }
  }
  scrollToBottom();
}

function showWelcome() {
  const container = $('chatMessages');
  container.innerHTML = `
    <div class="chat-welcome">
      <div class="chat-welcome-icon">馃</div>
      <p><strong>Project DNA AI 鍔╂墜</strong></p>
      <p>鎴戝彲浠ュ府浣犵悊瑙ｉ」鐩粨鏋勩€侀槄璇诲拰缂栧啓浠ｇ爜銆?/p>
      <p style="color:#64748b;font-size:12px;margin-top:8px;">Shift+Enter 鎹㈣ 路 Enter 鍙戦€?/p>
    </div>`;
}

export async function sendChatMessage() {
  const input = $('chatInput');
  const text = input.value.trim();
  if (!text) return;

  input.value = '';
  autoResizeInput();

  enqueueMessage({
    text,
    resume: text === '缁х画' && lastRunInterrupted,
    displayText: text === '缁х画' && lastRunInterrupted ? '缁х画锛堟柇鐐圭画璺戯級' : text
  });
}

function enqueueMessage(item) {
  const text = String(item?.text || '').trim();
  if (!text) return;

  const entry = {
    text,
    resume: item?.resume === true,
    displayText: String(item?.displayText || text),
    actionId: String(item?.actionId || '').trim() || null
  };

  messageQueue.push(entry);
  appendQueuedMessage(entry.displayText);
  renderQueueUI();
  processQueue();
}

export function beginTaskFromKnowledgeCard(encodedModuleName, btn) {
  const moduleName = decodeCardText(encodedModuleName);
  if (!moduleName) return;

  if (chatMode !== 'agent') {
    switchChatMode('agent');
  }

  const safeModuleName = moduleName.replaceAll('"', '\\"');
  const actionId = registerActionButton(btn);
  enqueueMessage({
    text: `璇疯皟鐢?begin_task("${safeModuleName}") 骞惰繘鍏ヨ妯″潡缁х画鎵ц銆俙,
    displayText: `杩涘叆妯″潡 ${moduleName}`,
    actionId
  });
}

export function askClarifyingFromKnowledgeCard(encodedQuestion, btn) {
  const question = decodeCardText(encodedQuestion);
  if (!question) return;

  const actionId = registerActionButton(btn);
  enqueueMessage({
    text: question,
    displayText: `婢勬竻锛?{shortenText(question, 24)}`,
    actionId
  });
}

export function queueDependencyValidationFromKnowledgeCard(encodedCaller, encodedCallee, btn) {
  const caller = decodeCardText(encodedCaller);
  const callee = decodeCardText(encodedCallee);
  if (!caller || !callee) return;

  if (chatMode !== 'agent') {
    switchChatMode('agent');
  }

  const safeCaller = caller.replaceAll('"', '\\"');
  const safeCallee = callee.replaceAll('"', '\\"');
  const actionId = registerActionButton(btn);
  enqueueMessage({
    text: `璇疯皟鐢?validate_dependency(callerModule="${safeCaller}", calleeModule="${safeCallee}")锛屽苟璇存槑鏄惁鍏佽璁块棶銆佽竟鐣岀骇鍒拰鍚庣画寤鸿銆俙,
    displayText: `鏍￠獙渚濊禆 ${caller} 鈫?${callee}`,
    actionId
  });
}

export function runGovernanceCheckFromKnowledgeCard(btn) {
  if (chatMode !== 'agent') {
    switchChatMode('agent');
  }

  const actionId = registerActionButton(btn);
  enqueueMessage({
    text: '璇疯皟鐢?evolve() 鎵ц涓€娆℃不鐞嗛妫€锛屽苟杈撳嚭楂橀闄╂ā鍧楀拰鍙墽琛岀殑娓愯繘寮忛噸鏋勬楠ゃ€?,
    displayText: '鎵ц娌荤悊棰勬',
    actionId
  });
}

export function runSuggestedActionFromKnowledgeCard(encodedPrompt, encodedDisplay, btn) {
  const prompt = decodeCardText(encodedPrompt);
  if (!prompt) return;

  const displayText = decodeCardText(encodedDisplay) || shortenText(prompt, 30);
  if (chatMode !== 'agent') {
    switchChatMode('agent');
  }

  const actionId = registerActionButton(btn);
  enqueueMessage({
    text: prompt,
    displayText,
    actionId
  });
}

function decodeCardText(encodedValue) {
  let value = '';
  try { value = decodeURIComponent(encodedValue || ''); }
  catch { value = String(encodedValue || ''); }
  return value.trim();
}

function registerActionButton(btn) {
  if (!btn || typeof btn !== 'object') return null;

  if (btn.dataset.actionStatus === 'queued' || btn.dataset.actionStatus === 'running') {
    return btn.dataset.actionId || null;
  }

  if (!btn.dataset.originalLabel) {
    btn.dataset.originalLabel = (btn.textContent || '').trim() || '鎵ц';
  }

  const actionId = `qa_${Date.now().toString(36)}_${(actionSequence++).toString(36)}`;
  btn.dataset.actionId = actionId;
  actionButtonMap.set(actionId, btn);
  setActionButtonState(btn, 'queued');
  return actionId;
}

function updateActionButtonState(actionId, status) {
  if (!actionId) return;
  const btn = actionButtonMap.get(actionId);
  if (!btn || !btn.isConnected) {
    actionButtonMap.delete(actionId);
    return;
  }
  setActionButtonState(btn, status);
  if (status === 'done' || status === 'paused' || status === 'failed') {
    actionButtonMap.delete(actionId);
  }
}

function setActionButtonState(btn, status) {
  if (!btn) return;
  const baseLabel = btn.dataset.originalLabel || (btn.textContent || '').trim() || '鎵ц';
  btn.classList.remove('busy', 'queued', 'running', 'done', 'paused', 'failed');
  btn.dataset.actionStatus = status;

  if (status === 'queued') {
    btn.classList.add('queued');
    btn.disabled = true;
    btn.textContent = '鈴?宸插叆闃?;
    btn.title = '鍔ㄤ綔宸插姞鍏ユ秷鎭槦鍒?;
    return;
  }
  if (status === 'running') {
    btn.classList.add('running');
    btn.disabled = true;
    btn.textContent = '鈿?鎵ц涓?;
    btn.title = '鍔ㄤ綔姝ｅ湪鎵ц';
    return;
  }
  if (status === 'done') {
    btn.classList.add('done');
    btn.disabled = false;
    btn.textContent = '鉁?宸叉墽琛?;
    btn.title = '鍔ㄤ綔宸叉墽琛岋紝鍙啀娆＄偣鍑婚噸璺?;
    return;
  }
  if (status === 'paused') {
    btn.classList.add('paused');
    btn.disabled = false;
    btn.textContent = '鈫?宸蹭腑鏂?;
    btn.title = '鎵ц涓柇锛屽彲鐐瑰嚮閲嶈瘯';
    return;
  }
  if (status === 'failed') {
    btn.classList.add('failed');
    btn.disabled = false;
    btn.textContent = '鈿?澶辫触';
    btn.title = '鍔ㄤ綔鎵ц澶辫触锛屽彲鐐瑰嚮閲嶈瘯';
    return;
  }

  btn.disabled = false;
  btn.textContent = baseLabel;
  btn.title = '';
}

function finalizeActionQueueItem(actionId, interrupted) {
  if (!actionId) return;
  updateActionButtonState(actionId, interrupted ? 'paused' : 'done');
}

async function processQueue() {
  if (isQueueProcessing) return;
  isQueueProcessing = true;
  try {
    while (messageQueue.length > 0) {
      const item = messageQueue.shift();
      renderQueueUI();
      if (!item) continue;

      if (item.resume) {
        updateActionButtonState(item.actionId, 'running');
        appendUserMessage(item.displayText || item.text || '缁х画');
        const runState = await streamAssistantResponse({ resume: true });
        finalizeActionQueueItem(item.actionId, Boolean(runState?.interrupted));
        continue;
      }

      const text = item.text || '';
      const expanded = await expandMentions(text);
      updateActionButtonState(item.actionId, 'running');
      messages.push({ role: 'user', content: expanded });
      appendUserMessage(text);
      lastRunInterrupted = false;

      const runState = await streamAssistantResponse();
      finalizeActionQueueItem(item.actionId, Boolean(runState?.interrupted));
    }
  } finally {
    isQueueProcessing = false;
  }
}

function appendQueuedMessage(text) {
  let queueContainer = document.getElementById('chatQueue');
  if (!queueContainer) {
    queueContainer = document.createElement('div');
    queueContainer.id = 'chatQueue';
    queueContainer.className = 'chat-queue';
    const inputArea = document.querySelector('.chat-input-area');
    inputArea.parentNode.insertBefore(queueContainer, inputArea);
  }
}

function renderQueueUI() {
  const queueContainer = document.getElementById('chatQueue');
  if (!queueContainer) return;

  if (messageQueue.length === 0) {
    queueContainer.remove();
    return;
  }

  queueContainer.innerHTML = messageQueue.map((text, i) =>
    `<div class="chat-queue-item">
      <span class="queue-index">${i + 1}</span>
      <span class="queue-text">${escapeHtml((text.displayText || text.text || '').length > 40 ? (text.displayText || text.text || '').slice(0, 40) + '鈥? : (text.displayText || text.text || ''))}</span>
      <button class="queue-edit" data-chat-action="edit-queue-item" data-queue-index="${i}" title="缂栬緫">鉁?/button>
      <button class="queue-remove" data-chat-action="remove-queue-item" data-queue-index="${i}" title="鍒犻櫎">鉁?/button>
    </div>`
  ).join('');
}

export function editQueueItem(index) {
  if (index < 0 || index >= messageQueue.length) return;
  const text = messageQueue[index]?.text || '';
  const input = $('chatInput');
  input.value = text;
  input.focus();
  autoResizeInput();
  messageQueue.splice(index, 1);
  renderQueueUI();
}

export function removeQueueItem(index) {
  if (index < 0 || index >= messageQueue.length) return;
  messageQueue.splice(index, 1);
  renderQueueUI();
}

async function expandMentions(text) {
  const mentionRegex = /@([\w./\-]+\.\w+|[\w\-]+)/g;
  const mentions = [...text.matchAll(mentionRegex)];
  if (mentions.length === 0) return text;

  const attachments = [];
  for (const match of mentions) {
    const ref = match[1];
    try {
      if (ref.includes('.')) {
        const resp = await fetch(`/api/files/read?path=${encodeURIComponent(ref)}`);
        if (resp.ok) {
          const data = await resp.json();
          if (data.content) attachments.push(`@${ref}:\n\`\`\`\n${data.content.slice(0, 10000)}\n\`\`\``);
        }
      } else {
        const resp = await fetch(`/api/memory/query?tags=identity&limit=1`);
        if (resp.ok) {
          const data = await resp.json();
          if (data && data.length > 0) attachments.push(`@${ref} (妯″潡璁板繂):\n${data[0].content.slice(0, 5000)}`);
        }
      }
    } catch {}
  }

  return attachments.length > 0
    ? text + '\n\n--- 寮曠敤鐨勪笂涓嬫枃 ---\n' + attachments.join('\n\n')
    : text;
}

export function handleChatKeydown(e) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    sendChatMessage();
  }
}

export function autoResizeInput() {
  const ta = $('chatInput');
  if (!ta) return;
  ta.style.height = 'auto';
  ta.style.height = Math.min(ta.scrollHeight, 120) + 'px';
}

// 鈹€鈹€ Message rendering 鈹€鈹€

function appendUserMessage(text) {
  const container = $('chatMessages');
  removeWelcome(container);

  const div = document.createElement('div');
  div.className = 'chat-msg user';
  div.innerHTML = `<div class="chat-msg-bubble">${escapeHtml(text)}</div>`;
  container.appendChild(div);
  scrollToBottom();
}

let currentBubble = null;

function getOrCreateAssistantBlock() {
  let block = document.getElementById('chatCurrentAssistant');
  if (block) return block;

  const container = $('chatMessages');
  removeWelcome(container);
  block = document.createElement('div');
  block.className = 'chat-msg assistant';
  block.id = 'chatCurrentAssistant';
  container.appendChild(block);
  currentBubble = null;
  scrollToBottom();
  return block;
}

function getOrCreateBubble() {
  if (currentBubble) return currentBubble;
  const block = getOrCreateAssistantBlock();
  const bubble = document.createElement('div');
  bubble.className = 'chat-msg-bubble';
  bubble._rawContent = '';
  block.appendChild(bubble);
  currentBubble = bubble;
  return bubble;
}

function appendAssistantMessage() {
  return getOrCreateAssistantBlock();
}

function appendToAssistant(text) {
  const bubble = getOrCreateBubble();

  const cursor = bubble.querySelector('.chat-cursor');
  if (cursor) cursor.remove();

  bubble._rawContent += text;

  if (typeof marked !== 'undefined') {
    bubble.innerHTML = marked.parse(bubble._rawContent) + '<span class="chat-cursor"></span>';
  } else {
    bubble.innerHTML = escapeHtml(bubble._rawContent) + '<span class="chat-cursor"></span>';
  }
  scrollToBottom();
}

function sealCurrentBubble() {
  if (!currentBubble) return;
  const cursor = currentBubble.querySelector('.chat-cursor');
  if (cursor) cursor.remove();
  if (currentBubble._rawContent && typeof marked !== 'undefined')
    currentBubble.innerHTML = marked.parse(currentBubble._rawContent);
  if (!currentBubble._rawContent && currentBubble.textContent.trim() === '')
    currentBubble.remove();
  currentBubble = null;
}

const TOOL_ICONS = {
  edit_file: '馃摑', write_file: '馃搫', read_file: '馃摉', list_files: '馃搧',
  grep: '馃攳', search_files: '馃攳', find_files: '馃搧', run_command: '鈻?, restore_checkpoint: '鈫?,
  begin_task: '馃幆', get_topology: '馃椇', validate_dependency: '馃敆',
  query_retrieval: '馃Л',
  query_knowledge_graph: '馃',
  create_plan: '馃搵', update_plan: '馃搵'
};
let toolCardMap = {};

const TOOL_VERBS = {
  grep: '宸叉悳绱?,
  search_files: '宸叉悳绱?,
  read_file: '宸茶鍙?,
  list_files: '宸插垪鍑?,
  find_files: '宸叉煡鎵?,
  write_file: '宸插啓鍏?,
  edit_file: '宸茬紪杈?,
  run_command: '宸叉墽琛?,
  restore_checkpoint: '宸叉仮澶?,
  begin_task: '宸插紑濮嬩换鍔?,
  get_module_context: '宸茶幏鍙栦笂涓嬫枃',
  validate_dependency: '宸叉牎楠屼緷璧?,
  set_current_module: '宸插垏鎹㈡ā鍧?,
  get_current_module: '宸叉鏌ユā鍧?,
  get_call_stack: '宸叉鏌ユ爤',
  get_topology: '宸茶幏鍙栨嫇鎵?,
  get_topology_summary: '宸茶幏鍙栨嫇鎵戞憳瑕?,
  query_retrieval: '宸叉煡璇?,
  query_knowledge_graph: '宸叉煡璇㈢煡璇嗗浘璋?,
  get_execution_plan: '宸茬敓鎴愯鍒?,
  suspend_and_push: '宸叉寕璧?,
  complete_and_pop: '宸插畬鎴?,
  update_task_status: '宸叉洿鏂扮姸鎬?,
  remember: '宸茶褰曡蹇?,
  recall: '宸叉绱㈣蹇?,
  verify_memory: '宸查獙璇佽蹇?,
  get_feature_knowledge: '宸茶幏鍙栫壒鎬х煡璇?,
  write_history: '宸茶褰曞巻鍙?,
  write_lesson: '宸茶褰曟暀璁?,
  register_module: '宸叉敞鍐屾ā鍧?,
  auto_register_modules: '宸茶嚜鍔ㄦ敞鍐屾ā鍧?,
  upsert_discipline: '宸叉洿鏂伴儴闂?,
  relocate_module: '宸茶縼绉绘ā鍧?,
  remove_orphan: '宸茬Щ闄ゅ鍎?,
  plan_task: '宸插垱寤鸿鍒?,
  activate_plan: '宸叉縺娲昏鍒?,
  next_step: '宸茶幏鍙栦笅涓€姝?,
  complete_step: '宸插畬鎴愭楠?,
  create_plan: '宸插垱寤鸿鍒?,
  update_plan: '宸叉洿鏂拌鍒?
};

function appendToolCall(name, args, description, toolCallId) {
  sealCurrentBubble();
  const block = getOrCreateAssistantBlock();
  const cardId = 'tc_' + Date.now() + '_' + Math.random().toString(36).slice(2,5);
  const icon = TOOL_ICONS[name] || '馃敡';
  const desc = formatToolTitle(name, args, description);

  const card = document.createElement('div');
  card.className = 'tool-card running';
  card.id = cardId;
  card.innerHTML = `
    <div class="tool-card-header" data-chat-action="toggle-tool-card">
      <span class="tool-card-icon">${icon}</span>
      <span class="tool-card-title">${escapeHtml(desc)}</span>
      <div class="tool-card-spinner"><div class="spinner"></div></div>
      <span class="tool-card-arrow">鈻?/span>
    </div>
    <div class="tool-card-body"></div>`;
  block.appendChild(card);
  if (toolCallId) toolCardMap[String(toolCallId)] = cardId;
  toolCardMap[name + '_latest'] = cardId;
  scrollToBottom();
}

function formatToolTitle(name, args, description) {
  if (description && description.trim()) return description;
  const verb = TOOL_VERBS[name] || toTitleCase(name.replaceAll('_', ' '));
  const parsed = tryParseJson(args);
  const hint = extractToolHint(name, parsed);
  return hint ? `${verb} ${hint}` : verb;
}

function extractToolHint(name, parsed) {
  if (!parsed || typeof parsed !== 'object') return '';
  switch (name) {
    case 'grep':
    case 'search_files':
      return parsed.pattern ? `"${String(parsed.pattern).slice(0, 40)}"` : '';
    case 'read_file':
    case 'write_file':
    case 'edit_file':
      return parsed.path ? `"${String(parsed.path).slice(0, 60)}"` : '';
    case 'run_command':
      return parsed.command ? `"${String(parsed.command).slice(0, 50)}"` : '';
    case 'begin_task':
      return parsed.moduleName ? `"${String(parsed.moduleName).slice(0, 40)}"` : '';
    case 'query_retrieval':
      return parsed.query ? `"${String(parsed.query).slice(0, 40)}"` : '';
    case 'query_knowledge_graph':
      return parsed.query ? `"${String(parsed.query).slice(0, 40)}"` : '';
    default:
      return '';
  }
}

function tryParseJson(text) {
  if (!text || typeof text !== 'string') return null;
  try { return JSON.parse(text); } catch { return null; }
}

function toTitleCase(text) {
  return text
    .split(/\s+/)
    .filter(Boolean)
    .map(w => w.charAt(0).toUpperCase() + w.slice(1))
    .join(' ');
}

function markToolDone(name, detail, description, result, toolCallId) {
  const key = toolCallId ? String(toolCallId) : '';
  const cardId = (key && toolCardMap[key]) ? toolCardMap[key] : toolCardMap[name + '_latest'];
  const card = cardId ? document.getElementById(cardId) : null;
  if (!card) return;

  card.classList.remove('running');
  card.classList.add('done');
  const spinner = card.querySelector('.tool-card-spinner');
  if (spinner) spinner.innerHTML = '<span class="tool-card-check">鉁?/span>';

  const body = card.querySelector('.tool-card-body');
  if (body) {
    body.innerHTML = renderToolResult(name, detail, result);
  }
  if ((detail && detail.kind === 'diff') || name === 'query_knowledge_graph' || name === 'query_retrieval') {
    card.classList.add('expanded');
  }
  if (key) delete toolCardMap[key];
}

function renderToolResult(name, detail, result) {
  if (detail && detail.kind) return renderToolDetail(name, detail);
  if (name === 'query_retrieval') return renderRetrievalDetail(result);
  if (name === 'query_knowledge_graph') return renderKnowledgeGraphDetail(result);
  return '';
}

function renderRetrievalDetail(result) {
  const parsed = typeof result === 'string' ? tryParseJson(result) : result;
  if (!parsed || typeof parsed !== 'object') {
    const text = String(result || '').trim();
    if (!text) return '<div class="tool-search-info">缁熶竴妫€绱㈠畬鎴愩€?/div>';
    return `<div class="tool-search-info">缁熶竴妫€绱㈢粨鏋滆В鏋愬け璐ワ細${escapeHtml(text.slice(0, 180))}</div>`;
  }

  const locate = parsed.locate && typeof parsed.locate === 'object' ? parsed.locate : null;
  const plan = parsed.plan && typeof parsed.plan === 'object' ? parsed.plan : null;
  const answer = parsed.answer && typeof parsed.answer === 'object' ? parsed.answer : null;

  const locateCard = locate
    ? renderKnowledgeGraphDetail({
        intent: parsed.intent || locate.intent || 'mixed',
        role: locate.role || 'generic',
        confidence: locate.confidence,
        primaryModules: locate.primaryModules || [],
        relatedModules: locate.relatedModules || [],
        executionOrder: locate.executionOrder || [],
        suggestedActions: locate.suggestedActions || [],
        needsClarification: Boolean(locate.needsClarification),
        clarifyingQuestions: locate.clarifyingQuestions || [],
        evidence: locate.evidence || [],
        summary: locate.summary || ''
      })
    : '<div class="tool-search-info">褰撳墠鏈繑鍥炴ā鍧楀畾浣嶇粨鏋溿€?/div>';

  const planSection = plan ? renderRetrievalPlanSection(plan) : '';
  const answerSection = answer ? renderRetrievalAnswerSection(answer) : '';

  return `<div class="tool-retrieval">
    ${locateCard}
    ${planSection}
    ${answerSection}
  </div>`;
}

function renderRetrievalPlanSection(plan) {
  const executionOrder = normalizeStringArray(plan.executionOrder).slice(0, 8);
  const checklist = normalizeStringArray(plan.checklist).slice(0, 6);
  const risks = normalizeStringArray(plan.risks).slice(0, 4);
  const rollback = normalizeStringArray(plan.rollbackPlan).slice(0, 4);
  const assumptions = normalizeStringArray(plan.assumptions).slice(0, 4);

  const orderHtml = executionOrder.length > 0
    ? `<div class="kg-order">${executionOrder.map((m, idx) => `${idx > 0 ? '<span class="kg-order-sep">鈫?/span>' : ''}<span>${escapeHtml(m)}</span>`).join('')}</div>`
    : '<div class="tool-search-info">鏆傛棤鎵ц椤哄簭銆?/div>';

  const checklistHtml = checklist.length > 0
    ? `<ul class="kg-list">${checklist.map(v => `<li>${escapeHtml(v)}</li>`).join('')}</ul>`
    : '';

  const risksHtml = risks.length > 0
    ? `<ul class="kg-list">${risks.map(v => `<li>${escapeHtml(v)}</li>`).join('')}</ul>`
    : '';

  const rollbackHtml = rollback.length > 0
    ? `<ul class="kg-list">${rollback.map(v => `<li>${escapeHtml(v)}</li>`).join('')}</ul>`
    : '';

  const assumptionsHtml = assumptions.length > 0
    ? `<ul class="kg-list">${assumptions.map(v => `<li>${escapeHtml(v)}</li>`).join('')}</ul>`
    : '';

  return `<div class="kg-section">
    <div class="kg-label">寮€鍙戣鍒?/div>
    ${orderHtml}
    ${checklistHtml ? `<div class="kg-label" style="margin-top:8px;">鎵ц娓呭崟</div>${checklistHtml}` : ''}
    ${risksHtml ? `<div class="kg-label" style="margin-top:8px;">椋庨櫓</div>${risksHtml}` : ''}
    ${rollbackHtml ? `<div class="kg-label" style="margin-top:8px;">鍥炴粴璁″垝</div>${rollbackHtml}` : ''}
    ${assumptionsHtml ? `<div class="kg-label" style="margin-top:8px;">鍋囪</div>${assumptionsHtml}` : ''}
  </div>`;
}

function renderRetrievalAnswerSection(answer) {
  const answerText = String(answer.answer || '').trim();
  const confidence = clampConfidence(answer.confidence);
  const confidencePct = Math.round(confidence * 100);
  const unknowns = normalizeStringArray(answer.unknowns).slice(0, 4);
  const assumptions = normalizeStringArray(answer.assumptions).slice(0, 4);

  return `<div class="kg-section">
    <div class="kg-label">鎶€鏈瓟澶?/div>
    ${answerText ? `<div class="kg-summary">${escapeHtml(answerText)}</div>` : '<div class="tool-search-info">鏆傛棤绛斿鏂囨湰銆?/div>'}
    <div class="kg-tags" style="margin-top:6px;">
      <span class="kg-tag">answer confidence: ${confidencePct}%</span>
    </div>
    ${assumptions.length > 0 ? `<div class="kg-label" style="margin-top:8px;">绛斿鍋囪</div><ul class="kg-list">${assumptions.map(v => `<li>${escapeHtml(v)}</li>`).join('')}</ul>` : ''}
    ${unknowns.length > 0 ? `<div class="kg-label" style="margin-top:8px;">鏈‘瀹氶」</div><ul class="kg-list">${unknowns.map(v => `<li>${escapeHtml(v)}</li>`).join('')}</ul>` : ''}
  </div>`;
}

function renderKnowledgeGraphDetail(result) {
  const parsed = typeof result === 'string' ? tryParseJson(result) : result;
  if (!parsed || typeof parsed !== 'object') {
    const text = String(result || '').trim();
    if (!text) return '<div class="tool-search-info">鐭ヨ瘑鍥捐氨鏌ヨ瀹屾垚銆?/div>';
    return `<div class="tool-search-info">鐭ヨ瘑鍥捐氨缁撴灉瑙ｆ瀽澶辫触锛?{escapeHtml(text.slice(0, 180))}</div>`;
  }

  const confidence = clampConfidence(parsed.confidence);
  const confidencePct = Math.round(confidence * 100);
  const confidenceClass = confidencePct >= 75 ? 'high' : confidencePct >= 60 ? 'medium' : 'low';
  const primaryModules = normalizeStringArray(parsed.primaryModules).slice(0, 6);
  const relatedModules = normalizeStringArray(parsed.relatedModules).slice(0, 8);
  const executionOrder = normalizeStringArray(parsed.executionOrder).slice(0, 8);
  const suggestedActions = normalizeStringArray(parsed.suggestedActions).slice(0, 6);
  const clarifyingQuestions = normalizeStringArray(parsed.clarifyingQuestions).slice(0, 4);
  const governanceHints = Array.isArray(parsed.governanceHints) ? parsed.governanceHints.slice(0, 4) : [];

  const moduleQuickActions = primaryModules.slice(0, 3).map(moduleName => (
    `<button class="kg-action-btn" data-chat-action="begin-task-knowledge" data-module-name="${encodeURIComponent(moduleName)}">杩涘叆 ${escapeHtml(moduleName)}</button>`
  )).join('');
  const dependencyQuickActions = (primaryModules.length > 0 && relatedModules.length > 0)
    ? relatedModules.slice(0, 2).map(moduleName => (
        `<button class="kg-action-btn secondary" data-chat-action="queue-dependency-validation" data-caller="${encodeURIComponent(primaryModules[0])}" data-callee="${encodeURIComponent(moduleName)}">鏍￠獙 ${escapeHtml(primaryModules[0])} 鈫?${escapeHtml(moduleName)}</button>`
      )).join('')
    : '';
  const clarifyQuickActions = clarifyingQuestions.length > 0
    ? clarifyingQuestions.slice(0, 3).map(question => (
        `<button class="kg-action-btn secondary" data-chat-action="ask-clarifying" data-question="${encodeURIComponent(question)}" title="${escapeHtml(question)}">${escapeHtml(shortenText(question, 26))}</button>`
      )).join('')
    : '';
  const hasGovernanceRisk = governanceHints.some(h =>
    Number(h?.errorCount || 0) >= 3 || Number(h?.warningCount || 0) >= 8
  );
  const governanceQuickAction = hasGovernanceRisk
    ? '<button class="kg-action-btn warn" data-chat-action="run-governance-check">鍏堟墽琛屾不鐞嗛妫€</button>'
    : '';

  const primaryHtml = primaryModules.length > 0
    ? `<div class="kg-section"><div class="kg-label">鍊欓€夋ā鍧?/div><div class="kg-chip-row">${
        primaryModules.map(m => `<span class="kg-chip primary">${escapeHtml(m)}</span>`).join('')
      }</div></div>`
    : '';

  const relatedHtml = relatedModules.length > 0
    ? `<div class="kg-section"><div class="kg-label">鍏宠仈妯″潡</div><div class="kg-chip-row">${
        relatedModules.map(m => `<span class="kg-chip">${escapeHtml(m)}</span>`).join('')
      }</div></div>`
    : '';

  const orderHtml = executionOrder.length > 0
    ? `<div class="kg-section"><div class="kg-label">鎵ц椤哄簭</div><div class="kg-order">${
        executionOrder.map((m, idx) => `${idx > 0 ? '<span class="kg-order-sep">鈫?/span>' : ''}<span>${escapeHtml(m)}</span>`).join('')
      }</div></div>`
    : '';

  const governanceHtml = governanceHints.length > 0
    ? `<div class="kg-section"><div class="kg-label">娌荤悊鎻愮ず</div><div class="kg-govern-list">${
        governanceHints.map(h => renderGovernanceHint(h)).join('')
      }</div></div>`
    : '';

  const suggestedHtml = buildSuggestedActionList(
    suggestedActions,
    { primaryModules, relatedModules, executionOrder, clarifyingQuestions }
  );

  const clarifyHtml = parsed.needsClarification && clarifyingQuestions.length > 0
    ? `<div class="kg-section"><div class="kg-label">寤鸿鍏堟緞娓?/div><ul class="kg-list">${
        clarifyingQuestions.map(q => `<li>${escapeHtml(q)}</li>`).join('')
      }</ul></div>`
    : '';
  const clarifyActionHtml = parsed.needsClarification && clarifyQuickActions
    ? `<div class="kg-section"><div class="kg-label">婢勬竻蹇嵎鍔ㄤ綔锛堢偣鍑荤洿鍙戯級</div><div class="kg-action-row">${clarifyQuickActions}</div></div>`
    : '';
  const dependencyActionHtml = dependencyQuickActions
    ? `<div class="kg-section"><div class="kg-label">渚濊禆鏍￠獙蹇嵎鍔ㄤ綔</div><div class="kg-action-row">${dependencyQuickActions}</div></div>`
    : '';
  const moduleActionHtml = moduleQuickActions
    ? `<div class="kg-section"><div class="kg-label">妯″潡璺宠浆</div><div class="kg-action-row">${moduleQuickActions}</div></div>`
    : '';
  const governanceActionHtml = governanceQuickAction
    ? `<div class="kg-section"><div class="kg-label">娌荤悊蹇嵎鍔ㄤ綔</div><div class="kg-action-row">${governanceQuickAction}</div></div>`
    : '';

  return `<div class="tool-kg">
    <div class="kg-top-row">
      <div class="kg-tags">
        <span class="kg-tag">intent: ${escapeHtml(String(parsed.intent || 'mixed'))}</span>
        <span class="kg-tag">娓告垙瑙掕壊: ${escapeHtml(formatGameRoleTag(parsed.role || 'coder'))}</span>
      </div>
      <span class="kg-confidence-text ${confidenceClass}">${confidencePct}%</span>
    </div>
    <div class="kg-confidence-track">
      <div class="kg-confidence-fill ${confidenceClass}" style="width:${confidencePct}%"></div>
    </div>
    <div class="kg-summary">${escapeHtml(String(parsed.summary || ''))}</div>
    ${primaryHtml}
    ${relatedHtml}
    ${orderHtml}
    ${governanceHtml}
    ${suggestedHtml}
    ${clarifyHtml}
    ${clarifyActionHtml}
    ${moduleActionHtml}
    ${dependencyActionHtml}
    ${governanceActionHtml}
  </div>`;
}

function renderGovernanceHint(hint) {
  const moduleName = String(hint?.moduleName || '');
  const errorCount = Number(hint?.errorCount || 0);
  const warningCount = Number(hint?.warningCount || 0);
  const advice = String(hint?.advice || '');
  const levelClass = errorCount >= 3 || warningCount >= 8
    ? 'high'
    : (errorCount > 0 || warningCount > 0 ? 'medium' : 'low');

  return `<div class="kg-govern-item ${levelClass}">
    <div class="kg-govern-head">
      <span class="kg-govern-module">${escapeHtml(moduleName)}</span>
      <span class="kg-govern-score">E${errorCount} / W${warningCount}</span>
    </div>
    <div class="kg-govern-advice">${escapeHtml(advice)}</div>
  </div>`;
}

function normalizeStringArray(value) {
  if (!Array.isArray(value)) return [];
  return value
    .map(v => String(v || '').trim())
    .filter(Boolean);
}

function clampConfidence(value) {
  const n = Number(value);
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(1, n));
}

function shortenText(text, maxLen) {
  const value = String(text || '');
  const limit = Math.max(4, Number(maxLen) || 24);
  return value.length <= limit ? value : value.slice(0, limit - 1) + '鈥?;
}

function formatGameRoleTag(role) {
  const normalized = String(role || '').trim().toLowerCase();
  if (normalized === 'programmer' || normalized === 'coder') return '绋嬪簭';
  if (normalized === 'planner' || normalized === 'designer' || normalized === 'design') return '绛栧垝';
  if (normalized === 'artist' || normalized === 'art') return '缇庢湳';
  return '绋嬪簭';
}

function buildSuggestedActionList(suggestedActions, context) {
  if (!Array.isArray(suggestedActions) || suggestedActions.length === 0) return '';

  const rows = suggestedActions.map(action => {
    const actionText = String(action || '').trim();
    if (!actionText) return '';
    const inlineButtons = buildActionButtonsForSuggestion(actionText, context);
    return `<li class="kg-action-item">
      <div class="kg-action-text">${escapeHtml(actionText)}</div>
      ${inlineButtons ? `<div class="kg-action-inline">${inlineButtons}</div>` : ''}
    </li>`;
  }).filter(Boolean).join('');

  if (!rows) return '';
  return `<div class="kg-section"><div class="kg-label">寤鸿鍔ㄤ綔锛堝彲鐐瑰嚮鎵ц锛?/div><ul class="kg-list kg-action-list">${rows}</ul></div>`;
}

function buildActionButtonsForSuggestion(actionText, context) {
  const text = String(actionText || '').trim();
  if (!text) return '';

  const primaryModules = normalizeStringArray(context?.primaryModules);
  const relatedModules = normalizeStringArray(context?.relatedModules);
  const executionOrder = normalizeStringArray(context?.executionOrder);
  const clarifyingQuestions = normalizeStringArray(context?.clarifyingQuestions);
  const lower = text.toLowerCase();
  const buttons = [];

  const beginTaskModuleMatch = text.match(/begin_task\s*\(\s*"([^"]+)"\s*\)/i);
  if (beginTaskModuleMatch) {
    const moduleNames = beginTaskModuleMatch[1]
      .split(/[,锛宂/)
      .map(v => v.trim())
      .filter(Boolean)
      .slice(0, 3);
    moduleNames.forEach(moduleName => {
      buttons.push(`<button class="kg-action-btn secondary" data-chat-action="begin-task-knowledge" data-module-name="${encodeURIComponent(moduleName)}">杩涘叆 ${escapeHtml(moduleName)}</button>`);
    });
  } else if (/begin_task\s*\(\s*\)/i.test(text)) {
    buttons.push(`<button class="kg-action-btn secondary" data-chat-action="run-suggested-action" data-prompt="${encodeURIComponent('璇疯皟鐢?begin_task() 杩斿洖椤圭洰妯″潡閫熸煡琛ㄣ€?)}" data-display="${encodeURIComponent('鑾峰彇妯″潡閫熸煡琛?)}">鑾峰彇妯″潡閫熸煡琛?/button>`);
  }

  if (lower.includes('validate_dependency')) {
    if (primaryModules.length > 0 && relatedModules.length > 0) {
      relatedModules.slice(0, 2).forEach(moduleName => {
        buttons.push(`<button class="kg-action-btn secondary" data-chat-action="queue-dependency-validation" data-caller="${encodeURIComponent(primaryModules[0])}" data-callee="${encodeURIComponent(moduleName)}">鏍￠獙 ${escapeHtml(primaryModules[0])} 鈫?${escapeHtml(moduleName)}</button>`);
      });
    } else {
      buttons.push(`<button class="kg-action-btn secondary" data-chat-action="run-suggested-action" data-prompt="${encodeURIComponent('璇疯皟鐢?validate_dependency(callerModule, calleeModule) 骞惰鏄庝緷璧栨槸鍚﹀悎娉曘€?)}" data-display="${encodeURIComponent('鎵ц渚濊禆鏍￠獙')}">鎵ц渚濊禆鏍￠獙</button>`);
    }
  }

  if (lower.includes('evolve()') || lower.includes('娌荤悊')) {
    buttons.push('<button class="kg-action-btn warn" data-chat-action="run-governance-check">鎵ц娌荤悊棰勬</button>');
  }

  if (lower.includes('婢勬竻') && clarifyingQuestions.length > 0) {
    buttons.push(`<button class="kg-action-btn secondary" data-chat-action="ask-clarifying" data-question="${encodeURIComponent(clarifyingQuestions[0])}">鍙戦€佹緞娓呴棶棰?/button>`);
  }

  if ((lower.includes('鎵ц椤哄簭') || lower.includes('鎸夐『搴?)) && executionOrder.length > 0) {
    const firstModule = executionOrder[0];
    buttons.push(`<button class="kg-action-btn secondary" data-chat-action="begin-task-knowledge" data-module-name="${encodeURIComponent(firstModule)}">鎸夐『搴忎粠 ${escapeHtml(firstModule)} 寮€濮?/button>`);
  }

  if (buttons.length === 0 && lower.includes('杩涘叆妯″潡') && primaryModules.length > 0) {
    buttons.push(`<button class="kg-action-btn secondary" data-chat-action="begin-task-knowledge" data-module-name="${encodeURIComponent(primaryModules[0])}">杩涘叆 ${escapeHtml(primaryModules[0])}</button>`);
  }

  return buttons.slice(0, 3).join('');
}

function renderToolDetail(name, detail) {
  if (!detail || !detail.kind) return '';

  switch (detail.kind) {
    case 'diff':
      {
      const editId = String(detail.editId || '').trim();
      const path = String(detail.path || '').trim();
      const hasEditId = editId.length > 0;
      const undoAvailable = hasEditId && detail.undoAvailable !== false;
      const encodedEditId = encodeURIComponent(editId);
      const encodedPath = encodeURIComponent(path);
      const keepDisabled = hasEditId ? '' : 'disabled';
      const undoDisabled = undoAvailable ? '' : 'disabled';
      const undoTitle = undoAvailable
        ? '鎾ら攢鏈潯鏀瑰姩'
        : escapeHtml(String(detail.undoReason || '璇ユ敼鍔ㄤ笉鏀寔鍗曟潯 Undo'));
      const helper = detail.truncated
        ? '<span class="tool-diff-note">Diff 杩囬暱锛屽凡鑷姩鎴柇棰勮銆?/span>'
        : '';
      const undoHint = !undoAvailable
        ? `<span class="tool-diff-note warn">${escapeHtml(String(detail.undoReason || '璇ユ敼鍔ㄤ笉鏀寔鍗曟潯 Undo'))}</span>`
        : '';
      return `<div class="tool-diff">
        <div class="tool-diff-path">${escapeHtml(path)}</div>
        ${renderDiff(detail.oldStr || '', detail.newStr || '')}
        <div class="tool-card-actions">
          <button class="tool-keep-btn" ${keepDisabled} data-chat-action="keep-edit" data-edit-id="${encodedEditId}">Keep</button>
          <button class="tool-undo-btn" ${undoDisabled} title="${undoTitle}" data-chat-action="undo-edit" data-edit-id="${encodedEditId}" data-path="${encodedPath}">Undo</button>
          ${helper}
          ${undoHint}
        </div>
      </div>`;
      }

    case 'file_created':
      return `<div class="tool-file-info">
        <span class="tool-file-path">${escapeHtml(detail.path || '')}</span>
        <span class="tool-file-size">${detail.size || 0} 瀛楃</span>
      </div>`;

    case 'shell': {
      const exitClass = detail.exitCode === 0 ? 'success' : 'error';
      const output = (detail.output || '').slice(0, 2000);
      return `<div class="tool-shell">
        <div class="tool-shell-cmd">$ ${escapeHtml(detail.command || '')}</div>
        <pre class="tool-shell-output">${escapeHtml(output)}</pre>
        <div class="tool-shell-exit ${exitClass}">exit: ${detail.exitCode}</div>
      </div>`;
    }

    case 'file_read':
      return `<div class="tool-file-info">
        <span class="tool-file-path">${escapeHtml(detail.path || '')}</span>
        <span class="tool-file-size">${detail.lines || 0} 琛?/span>
      </div>`;

    case 'search':
      return `<div class="tool-search-info">鎼滅储 "${escapeHtml(detail.pattern || '')}" 鈥?${detail.matchCount || 0} 鏉″尮閰?/div>`;

    case 'find':
      return `<div class="tool-search-info">鏌ユ壘 "${escapeHtml(detail.pattern || '')}" 鈥?${detail.fileCount || 0} 涓枃浠?/div>`;

    default: return '';
  }
}

function renderDiff(oldStr, newStr) {
  const oldLines = String(oldStr || '').split('\n');
  const newLines = String(newStr || '').split('\n');
  const ops = buildGreedyDiffOps(oldLines, newLines);
  const hasChanges = ops.some(op => op.type !== 'context');
  if (!hasChanges) {
    return '<div class="diff-view unified"><div class="diff-empty">鏃犲彲瑙嗗寲宸紓锛堝唴瀹瑰彲鑳藉彧鍙樻洿浜嗘崲琛?绌虹櫧锛夈€?/div></div>';
  }

  let oldNo = 1;
  let newNo = 1;
  for (const op of ops) {
    if (op.type === 'add') {
      op.oldNo = null;
      op.newNo = newNo++;
      continue;
    }
    if (op.type === 'del') {
      op.oldNo = oldNo++;
      op.newNo = null;
      continue;
    }
    op.oldNo = oldNo++;
    op.newNo = newNo++;
  }

  const ranges = buildDiffRanges(ops, 3);
  const maxRenderLines = 900;
  let renderedLines = 0;
  let html = '<div class="diff-view unified">';
  let lastEnd = -1;

  for (const range of ranges) {
    if (renderedLines >= maxRenderLines) break;
    if (range.start > lastEnd + 1) {
      html += '<div class="diff-gap">鈥?/div>';
    }

    const header = buildHunkHeader(ops, range.start, range.end);
    html += `<div class="diff-hunk-header">${header}</div>`;

    for (let i = range.start; i <= range.end; i++) {
      if (renderedLines >= maxRenderLines) break;
      const op = ops[i];
      html += renderUnifiedDiffLine(op);
      renderedLines++;
    }
    lastEnd = range.end;
  }

  if (renderedLines >= maxRenderLines) {
    html += '<div class="diff-gap">鈥︼紙diff 杩囬暱锛屽凡鎴柇锛?/div>';
  }

  html += '</div>';
  return html;
}

function buildGreedyDiffOps(oldLines, newLines) {
  const ops = [];
  let i = 0;
  let j = 0;

  while (i < oldLines.length || j < newLines.length) {
    const oldLine = i < oldLines.length ? oldLines[i] : null;
    const newLine = j < newLines.length ? newLines[j] : null;

    if (oldLine !== null && newLine !== null && oldLine === newLine) {
      ops.push({ type: 'context', text: oldLine });
      i++;
      j++;
      continue;
    }

    const nextOldMatches = i + 1 < oldLines.length && newLine !== null && oldLines[i + 1] === newLine;
    const nextNewMatches = j + 1 < newLines.length && oldLine !== null && oldLine === newLines[j + 1];

    if (nextOldMatches && !nextNewMatches) {
      ops.push({ type: 'del', text: oldLine || '' });
      i++;
      continue;
    }
    if (nextNewMatches && !nextOldMatches) {
      ops.push({ type: 'add', text: newLine || '' });
      j++;
      continue;
    }

    if (oldLine !== null) {
      ops.push({ type: 'del', text: oldLine });
      i++;
    }
    if (newLine !== null) {
      ops.push({ type: 'add', text: newLine });
      j++;
    }
  }

  return ops;
}

function buildDiffRanges(ops, contextSize) {
  const changedIndexes = [];
  for (let i = 0; i < ops.length; i++) {
    if (ops[i].type !== 'context') changedIndexes.push(i);
  }

  if (changedIndexes.length === 0) {
    return [{ start: 0, end: Math.max(0, Math.min(ops.length - 1, contextSize * 2)) }];
  }

  const ranges = [];
  for (const idx of changedIndexes) {
    const start = Math.max(0, idx - contextSize);
    const end = Math.min(ops.length - 1, idx + contextSize);
    const last = ranges[ranges.length - 1];
    if (last && start <= last.end + 1) {
      last.end = Math.max(last.end, end);
    } else {
      ranges.push({ start, end });
    }
  }
  return ranges;
}

function buildHunkHeader(ops, start, end) {
  let oldStart = 0;
  let newStart = 0;
  let oldCount = 0;
  let newCount = 0;
  let started = false;

  for (let i = start; i <= end; i++) {
    const op = ops[i];
    if (!started) {
      oldStart = op.oldNo ?? (op.oldNo === 0 ? 0 : (op.newNo || 0));
      newStart = op.newNo ?? (op.newNo === 0 ? 0 : (op.oldNo || 0));
      started = true;
    }
    if (op.type !== 'add') oldCount++;
    if (op.type !== 'del') newCount++;
  }

  const oldInfo = `${Math.max(1, oldStart || 1)},${Math.max(0, oldCount)}`;
  const newInfo = `${Math.max(1, newStart || 1)},${Math.max(0, newCount)}`;
  return `@@ -${oldInfo} +${newInfo} @@`;
}

function renderUnifiedDiffLine(op) {
  const oldNo = op.oldNo == null ? '' : String(op.oldNo);
  const newNo = op.newNo == null ? '' : String(op.newNo);
  const sign = op.type === 'add' ? '+' : (op.type === 'del' ? '-' : ' ');
  const cls = op.type === 'add' ? 'add' : (op.type === 'del' ? 'del' : 'context');
  return `<div class="diff-line diff-${cls}">
    <span class="diff-ln old">${oldNo}</span>
    <span class="diff-ln new">${newNo}</span>
    <span class="diff-sign">${sign}</span>
    <span class="diff-text">${escapeHtml(op.text || '')}</span>
  </div>`;
}

export async function keepEdit(encodedEditId, btn) {
  const editId = decodeCardText(encodedEditId);
  if (!editId || !btn) return;

  const actions = btn.closest('.tool-card-actions');
  const undoBtn = actions ? actions.querySelector('.tool-undo-btn') : null;
  const undoWasDisabled = Boolean(undoBtn && undoBtn.disabled);
  setDiffActionHint(actions, '', '');
  btn.disabled = true;
  btn.textContent = 'Keeping...';
  if (undoBtn) undoBtn.disabled = true;

  const result = await postEditAction('/api/agent/edits/keep', editId);
  if (!result.success) {
    btn.disabled = false;
    btn.textContent = 'Keep';
    if (undoBtn) undoBtn.disabled = undoWasDisabled;
    setDiffActionHint(actions, result.message || 'Keep 澶辫触', 'warn');
    return;
  }

  btn.textContent = 'Kept';
  btn.classList.add('done');
  if (undoBtn) {
    undoBtn.disabled = true;
    undoBtn.textContent = 'Undo';
  }
  const diff = btn.closest('.tool-diff');
  if (diff) diff.classList.add('kept');
  setDiffActionHint(actions, '宸蹭繚鐣欒鏀瑰姩', 'ok');
}

export async function undoEdit(encodedEditId, encodedPath, btn) {
  const editId = decodeCardText(encodedEditId);
  const path = decodeCardText(encodedPath) || '璇ユ枃浠?;
  if (!editId || !btn) return;

  const actions = btn.closest('.tool-card-actions');
  const keepBtn = actions ? actions.querySelector('.tool-keep-btn') : null;
  const keepWasDisabled = Boolean(keepBtn && keepBtn.disabled);
  setDiffActionHint(actions, '', '');
  btn.disabled = true;
  btn.textContent = 'Undoing...';
  if (keepBtn) keepBtn.disabled = true;

  const result = await postEditAction('/api/agent/edits/undo', editId);
  if (!result.success) {
    btn.disabled = false;
    btn.textContent = 'Undo';
    if (keepBtn) keepBtn.disabled = keepWasDisabled;
    setDiffActionHint(actions, result.message || `鎾ら攢 ${path} 澶辫触`, 'warn');
    return;
  }

  btn.textContent = 'Undone';
  btn.classList.add('done');
  if (keepBtn) {
    keepBtn.disabled = true;
    keepBtn.textContent = 'Keep';
  }
  const diff = btn.closest('.tool-diff');
  if (diff) diff.classList.add('undone');
  setDiffActionHint(actions, result.message || `宸叉挙閿€ ${path}`, 'ok');
}

async function postEditAction(url, editId) {
  try {
    const resp = await apiFetch(url, {
      method: 'POST',
      body: { editId }
    });
    const data = await resp.json().catch(() => ({}));
    if (!resp.ok) {
      return { success: false, message: data.message || `璇锋眰澶辫触 (${resp.status})` };
    }
    if (data && typeof data.success === 'boolean') {
      return data;
    }
    return { success: true, message: '' };
  } catch (err) {
    return { success: false, message: '缃戠粶寮傚父锛? + (err?.message || String(err)) };
  }
}

function setDiffActionHint(actions, text, type) {
  if (!actions) return;
  let hint = actions.querySelector('.tool-action-hint');
  if (!text) {
    if (hint) hint.remove();
    return;
  }
  if (!hint) {
    hint = document.createElement('span');
    hint.className = 'tool-action-hint';
    actions.appendChild(hint);
  }
  hint.classList.toggle('warn', type === 'warn');
  hint.classList.toggle('ok', type === 'ok');
  hint.textContent = text;
}

function finalizeToolGroup() {
  toolCardMap = {};
}

function appendContinuePrompt(limit) {
  const container = $('chatMessages');
  const div = document.createElement('div');
  div.className = 'chat-continue-prompt';
  div.innerHTML = `<span>宸茶揪宸ュ叿璋冪敤涓婇檺锛?{limit} 杞級銆?/span><button class="chat-continue-btn" data-chat-action="continue-chat">缁х画</button>`;
  container.appendChild(div);
  scrollToBottom();
}

export function continueChatFromLimit() {
  const prompt = document.querySelector('.chat-continue-prompt');
  if (prompt) prompt.remove();
  enqueueMessage({ text: '缁х画', resume: true, displayText: '缁х画锛堟柇鐐圭画璺戯級' });
}

function appendError(message) {
  const container = $('chatMessages');
  const div = document.createElement('div');
  div.className = 'chat-error';
  div.textContent = message;
  container.appendChild(div);
  scrollToBottom();
}

function removeWelcome(container) {
  const welcome = container.querySelector('.chat-welcome');
  if (welcome) welcome.remove();
  const noProvider = container.querySelector('.chat-no-provider');
  if (noProvider) noProvider.remove();
}

function scrollToBottom() {
  const container = $('chatMessages');
  requestAnimationFrame(() => {
    container.scrollTop = container.scrollHeight;
  });
}

// 鈹€鈹€ SSE streaming 鈹€鈹€

async function streamAssistantResponse(options = {}) {
  const resume = options.resume === true;
  let interruptedThisRun = false;
  isStreaming = true;
  updateSendButton();
  setStatus(resume ? '缁窇涓?..' : '鎬濊€冧腑...', 'thinking');

  currentController = new AbortController();
  let fullText = '';
  let assistantStarted = false;
  let pendingToolCalls = [];
  let toolResults = [];

  try {
    const resp = await apiFetch('/agent/chat', {
      method: 'POST',
      body: { messages, mode: chatMode, sessionId, resume },
      signal: currentController.signal
    });

    if (!resp.ok) {
      const err = await resp.json().catch(() => ({}));
      appendError(err.error || `璇锋眰澶辫触 (${resp.status})`);
      interruptedThisRun = true;
      isStreaming = false;
      currentController = null;
      updateSendButton();
      lastRunInterrupted = true;
      saveCurrentSession();
      return { interrupted: true };
    }

    const reader = resp.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() || '';

      for (const line of lines) {
        if (!line.startsWith('data: ')) continue;
        const data = line.slice(6).trim();
        if (!data) continue;

        let evt;
        try { evt = JSON.parse(data); }
        catch { continue; }

        if (evt.type === 'text') {
          assistantStarted = true;
          setStatus('');
          fullText += evt.content;
          appendToAssistant(evt.content);
        }
        else if (evt.type === 'mode_switched') {
          const toMode = evt.toMode || 'plan';
          switchChatMode(toMode);
          const reason = evt.reason || `宸茶嚜鍔ㄥ垏鎹㈠埌 ${toMode} 妯″紡`;
          setStatus(reason, 'working');
        }
        else if (evt.type === 'tool_start') {
          assistantStarted = true;
          setStatus(evt.description || `${TOOL_VERBS[evt.name] || toTitleCase(String(evt.name || '').replaceAll('_', ' '))}...`, 'working');
          appendToolCall(evt.name, evt.args, evt.description, evt.id);
          pendingToolCalls.push({ id: evt.id, name: evt.name, arguments: evt.args || '{}' });
        }
        else if (evt.type === 'tool_end') {
          markToolDone(evt.name, evt.detail, evt.description, evt.result, evt.id);
          toolResults.push({
            toolCallId: evt.id,
            name: evt.name,
            content: typeof evt.result === 'string' ? evt.result : (evt.summary || '')
          });
        }
        else if (evt.type === 'rounds_exhausted') {
          interruptedThisRun = true;
          appendContinuePrompt(evt.limit);
        }
        else if (evt.type === 'error') {
          interruptedThisRun = true;
          appendError(evt.content || evt.message || '鏈煡閿欒');
        }
        else if (evt.type === 'done') {
          setStatus('');
          finalizeToolGroup();
          if (assistantStarted || pendingToolCalls.length > 0) {
            commitToolHistory(fullText, pendingToolCalls, toolResults);
            pendingToolCalls = [];
            toolResults = [];
            fullText = '';
            assistantStarted = false;
          }
        }
      }
    }
  } catch (err) {
    if (err.name !== 'AbortError') {
      interruptedThisRun = true;
      appendError('杩炴帴涓柇: ' + err.message);
    }
  }

  if (fullText || pendingToolCalls.length > 0) {
    commitToolHistory(fullText, pendingToolCalls, toolResults);
  }

  sealCurrentBubble();
  const div = document.getElementById('chatCurrentAssistant');
  if (div) div.removeAttribute('id');

  isStreaming = false;
  currentController = null;
  updateSendButton();
  lastRunInterrupted = interruptedThisRun;
  saveCurrentSession();
  return { interrupted: interruptedThisRun };
}

function commitToolHistory(text, toolCalls, toolResults) {
  if (toolCalls.length > 0) {
    messages.push({
      role: 'assistant',
      content: text || null,
      tool_calls: toolCalls.map(tc => ({
        id: tc.id,
        type: 'function',
        function: {
          name: tc.name || 'unknown_tool',
          arguments: typeof tc.arguments === 'string' ? tc.arguments : '{}'
        }
      }))
    });
    for (const tr of toolResults) {
      messages.push({
        role: 'tool',
        tool_call_id: tr.toolCallId,
        name: tr.name || 'unknown_tool',
        content: tr.content
      });
    }
  } else if (text) {
    messages.push({ role: 'assistant', content: text });
  }
}

export function stopChat() {
  if (currentController) {
    currentController.abort();
    currentController = null;
  }
  isStreaming = false;
  updateStreamingUI(false);
  finalizeToolGroup();
  setStatus('宸插仠姝?, 'warn');
  setTimeout(() => setStatus(''), 3000);
}

function updateStreamingUI(streaming) {
  const sendBtn = $('chatSendBtn');
  const stopBtn = $('chatStopBtn');
  if (sendBtn) sendBtn.classList.toggle('hidden', streaming);
  if (stopBtn) stopBtn.classList.toggle('hidden', !streaming);
}

function setStatus(text, type) {
  const el = $('chatStatus');
  if (!el) return;
  if (!text) { el.classList.add('hidden'); el.textContent = ''; return; }
  el.classList.remove('hidden');
  el.textContent = text;
  el.className = `chat-status ${type || ''}`;
}

function updateSendButton() {
  updateStreamingUI(isStreaming);
}

export function switchChatMode(mode) {
  chatMode = mode;
  const btns = document.querySelectorAll('.chat-mode-btn');
  btns.forEach(b => b.classList.toggle('active', b.dataset.mode === mode));
}

export function openModelDropdown() {
  const dd = $('chatModelDropdown');
  if (!dd) return;
  if (!dd.classList.contains('hidden')) { dd.classList.add('hidden'); return; }

  const providers = getProviderList();
  const activeId = getActiveProviderId();

  dd.innerHTML = renderModelDropdownHtml({
    providers,
    activeProviderId: activeId,
    emptyHtml: '<div class="model-dd-empty">鏈厤缃ā鍨?鈥斺€?<a href="#" data-chat-action="open-llm-settings">鍘昏缃?/a></div>',
    settingsLabel: '鈿?妯″瀷璁剧疆',
    footerNote: '宸ュ叿杞鍥哄畾锛?00锛圡ax锛?'
  });
  dd.classList.remove('hidden');

  setTimeout(() => document.addEventListener('click', closeDropdownOutside, { once: true }), 0);
}

function closeDropdownOutside(e) {
  const dd = $('chatModelDropdown');
  if (dd && !dd.contains(e.target) && e.target.id !== 'chatModelSelector')
    dd.classList.add('hidden');
}

function closeDd() { const dd = $('chatModelDropdown'); if (dd) dd.classList.add('hidden'); }

export async function selectProvider(id) {
  await switchProvider(id);
  closeDd();
}

export function updateModelTag(model) {
  updateSharedModelTag($, model, 'Unavailable');
}

// 鈹€鈹€ Session persistence 鈹€鈹€

function generateSessionId() {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
}

function extractTitleFromMessages(msgs) {
  if (!msgs || msgs.length === 0) return '';
  const first = msgs.find(m => m.role === 'user' && m.content);
  if (!first) return '';
  const text = first.content.trim().replace(/\s+/g, ' ');
  return text.length > 40 ? text.slice(0, 40) + '鈥? : text;
}

function saveCurrentSession() {
  if (messages.length === 0) return;
  const title = extractTitleFromMessages(messages);
  apiFetch('/agent/sessions/save', {
    method: 'POST',
    body: { id: sessionId, mode: chatMode, title, messages }
  }).then(resp => {
    if (!resp.ok) resp.text().then(t => console.warn('[chat] save session failed:', resp.status, t));
  }).catch(err => console.warn('[chat] save session error:', err));
}

// 鈹€鈹€ Helpers 鈹€鈹€

function escapeHtml(text) {
  const d = document.createElement('div');
  d.textContent = text;
  return d.innerHTML;
}

// 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲
//  Chat panel resize
// 鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲鈺愨晲

bindChatUiEvents();

const STORAGE_KEY = 'dna-chat-width';

export function initChatResize() {
  const handle = document.getElementById('chatResizeHandle');
  const panel = document.getElementById('chatPanel');
  if (!handle || !panel) return;

  const saved = localStorage.getItem(STORAGE_KEY);
  if (saved) panel.style.width = saved + 'px';

  let startX = 0, startW = 0;

  handle.addEventListener('mousedown', (e) => {
    e.preventDefault();
    startX = e.clientX;
    startW = panel.offsetWidth;
    panel.classList.add('resizing');
    handle.classList.add('active');
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  });

  function onMove(e) {
    const delta = startX - e.clientX;
    const min = parseInt(getComputedStyle(panel).minWidth) || 320;
    const max = window.innerWidth * 0.7;
    const newW = Math.min(max, Math.max(min, startW + delta));
    panel.style.width = newW + 'px';
  }

  function onUp() {
    panel.classList.remove('resizing');
    handle.classList.remove('active');
    document.removeEventListener('mousemove', onMove);
    document.removeEventListener('mouseup', onUp);
    localStorage.setItem(STORAGE_KEY, panel.offsetWidth);
  }
}
