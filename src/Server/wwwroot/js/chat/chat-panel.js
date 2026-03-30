/**
 * 聊天面板模块
 * - 管理聊天面板 UI 状态（打开/关闭/新对话）
 * - 发送消息与接收 SSE 流式响应
 * - 渲染用户/助手消息、工具调用状态
 */

import { $, api, apiFetch } from '../utils.js';
import { getProviderList, getActiveProviderId, switchProvider, openLlmSettings } from './llm-settings.js';

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

window.addEventListener('beforeunload', () => { saveCurrentSession(); });

export function toggleChat() {
  // 右侧 AI 面板改为常驻：不再支持开关。
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

  container.innerHTML = '<div class="session-list-loading">加载中...</div>';

  try {
    const data = await api('/agent/sessions');
    const resp = { ok: true };
    if (!resp.ok) {
      container.innerHTML = '<div class="chat-welcome"><p>无法加载历史会话</p></div>';
      return;
    }
    const sessions = data.sessions || [];

    let html = `<div class="session-list-header">
      <span class="session-list-title">历史会话</span>
      <button class="session-list-back-btn" onclick="showSessionList()" title="返回当前对话">✕</button>
    </div>`;

    if (sessions.length === 0) {
      html += '<div class="session-list-empty">暂无历史会话</div>';
    } else {
      html += '<div class="session-list-items">';
      for (const s of sessions) {
        const isActive = s.id === sessionId;
        const modeLabel = { agent: 'Agent', chat: 'Chat', plan: 'Plan' }[(s.mode || '').toLowerCase()] || s.mode || '';
        const time = s.updatedAt ? new Date(s.updatedAt).toLocaleString() : '';
        const title = s.title || '无标题';
        const msgCount = s.messageCount || 0;
        html += `<div class="chat-session-item${isActive ? ' active' : ''}" onclick="loadSession('${escapeHtml(s.id)}')">
          <div class="chat-session-title">${escapeHtml(title)}</div>
          <div class="chat-session-meta">${modeLabel}${msgCount > 0 ? ' · ' + msgCount + ' 条消息' : ''}${time ? ' · ' + time : ''}</div>
          ${isActive ? '<div class="chat-session-badge">当前</div>' : ''}
        </div>`;
      }
      html += '</div>';
    }

    container.innerHTML = html;
  } catch (err) {
    console.warn('[chat] showSessionList failed:', err);
    container.innerHTML = '<div class="chat-welcome"><p>加载历史会话失败</p></div>';
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
      group.innerHTML = `<div class="chat-tool-summary" onclick="this.parentElement.classList.toggle('expanded')"><span class="tool-group-icon">⚡</span><span class="tool-summary-text">使用了 ${toolCount} 个工具</span></div>`;
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
      <div class="chat-welcome-icon">🤖</div>
      <p><strong>Project DNA AI 助手</strong></p>
      <p>我可以帮你理解项目结构、阅读和编写代码。</p>
      <p style="color:#64748b;font-size:12px;margin-top:8px;">Shift+Enter 换行 · Enter 发送</p>
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
    resume: text === '继续' && lastRunInterrupted,
    displayText: text === '继续' && lastRunInterrupted ? '继续（断点续跑）' : text
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
    text: `请调用 begin_task("${safeModuleName}") 并进入该模块继续执行。`,
    displayText: `进入模块 ${moduleName}`,
    actionId
  });
}

export function askClarifyingFromKnowledgeCard(encodedQuestion, btn) {
  const question = decodeCardText(encodedQuestion);
  if (!question) return;

  const actionId = registerActionButton(btn);
  enqueueMessage({
    text: question,
    displayText: `澄清：${shortenText(question, 24)}`,
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
    text: `请调用 validate_dependency(callerModule="${safeCaller}", calleeModule="${safeCallee}")，并说明是否允许访问、边界级别和后续建议。`,
    displayText: `校验依赖 ${caller} → ${callee}`,
    actionId
  });
}

export function runGovernanceCheckFromKnowledgeCard(btn) {
  if (chatMode !== 'agent') {
    switchChatMode('agent');
  }

  const actionId = registerActionButton(btn);
  enqueueMessage({
    text: '请调用 evolve() 执行一次治理预检，并输出高风险模块和可执行的渐进式重构步骤。',
    displayText: '执行治理预检',
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
    btn.dataset.originalLabel = (btn.textContent || '').trim() || '执行';
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
  const baseLabel = btn.dataset.originalLabel || (btn.textContent || '').trim() || '执行';
  btn.classList.remove('busy', 'queued', 'running', 'done', 'paused', 'failed');
  btn.dataset.actionStatus = status;

  if (status === 'queued') {
    btn.classList.add('queued');
    btn.disabled = true;
    btn.textContent = '⏳ 已入队';
    btn.title = '动作已加入消息队列';
    return;
  }
  if (status === 'running') {
    btn.classList.add('running');
    btn.disabled = true;
    btn.textContent = '⚙ 执行中';
    btn.title = '动作正在执行';
    return;
  }
  if (status === 'done') {
    btn.classList.add('done');
    btn.disabled = false;
    btn.textContent = '✓ 已执行';
    btn.title = '动作已执行，可再次点击重跑';
    return;
  }
  if (status === 'paused') {
    btn.classList.add('paused');
    btn.disabled = false;
    btn.textContent = '↻ 已中断';
    btn.title = '执行中断，可点击重试';
    return;
  }
  if (status === 'failed') {
    btn.classList.add('failed');
    btn.disabled = false;
    btn.textContent = '⚠ 失败';
    btn.title = '动作执行失败，可点击重试';
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
        appendUserMessage(item.displayText || item.text || '继续');
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
      <span class="queue-text">${escapeHtml((text.displayText || text.text || '').length > 40 ? (text.displayText || text.text || '').slice(0, 40) + '…' : (text.displayText || text.text || ''))}</span>
      <button class="queue-edit" onclick="editQueueItem(${i})" title="编辑">✎</button>
      <button class="queue-remove" onclick="removeQueueItem(${i})" title="删除">✕</button>
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
          if (data && data.length > 0) attachments.push(`@${ref} (模块记忆):\n${data[0].content.slice(0, 5000)}`);
        }
      }
    } catch {}
  }

  return attachments.length > 0
    ? text + '\n\n--- 引用的上下文 ---\n' + attachments.join('\n\n')
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

// ── Message rendering ──

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
  edit_file: '📝', write_file: '📄', read_file: '📖', list_files: '📁',
  grep: '🔍', search_files: '🔍', find_files: '📁', run_command: '▶', restore_checkpoint: '↩',
  begin_task: '🎯', get_topology: '🗺', validate_dependency: '🔗',
  query_retrieval: '🧭',
  query_knowledge_graph: '🧠',
  create_plan: '📋', update_plan: '📋'
};
let toolCardMap = {};

const TOOL_VERBS = {
  grep: '已搜索',
  search_files: '已搜索',
  read_file: '已读取',
  list_files: '已列出',
  find_files: '已查找',
  write_file: '已写入',
  edit_file: '已编辑',
  run_command: '已执行',
  restore_checkpoint: '已恢复',
  begin_task: '已开始任务',
  get_module_context: '已获取上下文',
  validate_dependency: '已校验依赖',
  set_current_module: '已切换模块',
  get_current_module: '已检查模块',
  get_call_stack: '已检查栈',
  get_topology: '已获取拓扑',
  get_topology_summary: '已获取拓扑摘要',
  query_retrieval: '已查询',
  query_knowledge_graph: '已查询知识图谱',
  get_execution_plan: '已生成计划',
  suspend_and_push: '已挂起',
  complete_and_pop: '已完成',
  update_task_status: '已更新状态',
  remember: '已记录记忆',
  recall: '已检索记忆',
  verify_memory: '已验证记忆',
  get_feature_knowledge: '已获取特性知识',
  write_history: '已记录历史',
  write_lesson: '已记录教训',
  register_module: '已注册模块',
  auto_register_modules: '已自动注册模块',
  upsert_discipline: '已更新部门',
  relocate_module: '已迁移模块',
  remove_orphan: '已移除孤儿',
  plan_task: '已创建计划',
  activate_plan: '已激活计划',
  next_step: '已获取下一步',
  complete_step: '已完成步骤',
  create_plan: '已创建计划',
  update_plan: '已更新计划'
};

function appendToolCall(name, args, description, toolCallId) {
  sealCurrentBubble();
  const block = getOrCreateAssistantBlock();
  const cardId = 'tc_' + Date.now() + '_' + Math.random().toString(36).slice(2,5);
  const icon = TOOL_ICONS[name] || '🔧';
  const desc = formatToolTitle(name, args, description);

  const card = document.createElement('div');
  card.className = 'tool-card running';
  card.id = cardId;
  card.innerHTML = `
    <div class="tool-card-header" onclick="this.parentElement.classList.toggle('expanded')">
      <span class="tool-card-icon">${icon}</span>
      <span class="tool-card-title">${escapeHtml(desc)}</span>
      <div class="tool-card-spinner"><div class="spinner"></div></div>
      <span class="tool-card-arrow">▸</span>
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
  if (spinner) spinner.innerHTML = '<span class="tool-card-check">✓</span>';

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
    if (!text) return '<div class="tool-search-info">统一检索完成。</div>';
    return `<div class="tool-search-info">统一检索结果解析失败：${escapeHtml(text.slice(0, 180))}</div>`;
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
    : '<div class="tool-search-info">当前未返回模块定位结果。</div>';

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
    ? `<div class="kg-order">${executionOrder.map((m, idx) => `${idx > 0 ? '<span class="kg-order-sep">→</span>' : ''}<span>${escapeHtml(m)}</span>`).join('')}</div>`
    : '<div class="tool-search-info">暂无执行顺序。</div>';

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
    <div class="kg-label">开发计划</div>
    ${orderHtml}
    ${checklistHtml ? `<div class="kg-label" style="margin-top:8px;">执行清单</div>${checklistHtml}` : ''}
    ${risksHtml ? `<div class="kg-label" style="margin-top:8px;">风险</div>${risksHtml}` : ''}
    ${rollbackHtml ? `<div class="kg-label" style="margin-top:8px;">回滚计划</div>${rollbackHtml}` : ''}
    ${assumptionsHtml ? `<div class="kg-label" style="margin-top:8px;">假设</div>${assumptionsHtml}` : ''}
  </div>`;
}

function renderRetrievalAnswerSection(answer) {
  const answerText = String(answer.answer || '').trim();
  const confidence = clampConfidence(answer.confidence);
  const confidencePct = Math.round(confidence * 100);
  const unknowns = normalizeStringArray(answer.unknowns).slice(0, 4);
  const assumptions = normalizeStringArray(answer.assumptions).slice(0, 4);

  return `<div class="kg-section">
    <div class="kg-label">技术答复</div>
    ${answerText ? `<div class="kg-summary">${escapeHtml(answerText)}</div>` : '<div class="tool-search-info">暂无答复文本。</div>'}
    <div class="kg-tags" style="margin-top:6px;">
      <span class="kg-tag">answer confidence: ${confidencePct}%</span>
    </div>
    ${assumptions.length > 0 ? `<div class="kg-label" style="margin-top:8px;">答复假设</div><ul class="kg-list">${assumptions.map(v => `<li>${escapeHtml(v)}</li>`).join('')}</ul>` : ''}
    ${unknowns.length > 0 ? `<div class="kg-label" style="margin-top:8px;">未确定项</div><ul class="kg-list">${unknowns.map(v => `<li>${escapeHtml(v)}</li>`).join('')}</ul>` : ''}
  </div>`;
}

function renderKnowledgeGraphDetail(result) {
  const parsed = typeof result === 'string' ? tryParseJson(result) : result;
  if (!parsed || typeof parsed !== 'object') {
    const text = String(result || '').trim();
    if (!text) return '<div class="tool-search-info">知识图谱查询完成。</div>';
    return `<div class="tool-search-info">知识图谱结果解析失败：${escapeHtml(text.slice(0, 180))}</div>`;
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
    `<button class="kg-action-btn" onclick="beginTaskFromKnowledgeCard('${encodeURIComponent(moduleName)}', this)">进入 ${escapeHtml(moduleName)}</button>`
  )).join('');
  const dependencyQuickActions = (primaryModules.length > 0 && relatedModules.length > 0)
    ? relatedModules.slice(0, 2).map(moduleName => (
        `<button class="kg-action-btn secondary" onclick="queueDependencyValidationFromKnowledgeCard('${encodeURIComponent(primaryModules[0])}', '${encodeURIComponent(moduleName)}', this)">校验 ${escapeHtml(primaryModules[0])} → ${escapeHtml(moduleName)}</button>`
      )).join('')
    : '';
  const clarifyQuickActions = clarifyingQuestions.length > 0
    ? clarifyingQuestions.slice(0, 3).map(question => (
        `<button class="kg-action-btn secondary" onclick="askClarifyingFromKnowledgeCard('${encodeURIComponent(question)}', this)" title="${escapeHtml(question)}">${escapeHtml(shortenText(question, 26))}</button>`
      )).join('')
    : '';
  const hasGovernanceRisk = governanceHints.some(h =>
    Number(h?.errorCount || 0) >= 3 || Number(h?.warningCount || 0) >= 8
  );
  const governanceQuickAction = hasGovernanceRisk
    ? `<button class="kg-action-btn warn" onclick="runGovernanceCheckFromKnowledgeCard(this)">先执行治理预检</button>`
    : '';

  const primaryHtml = primaryModules.length > 0
    ? `<div class="kg-section"><div class="kg-label">候选模块</div><div class="kg-chip-row">${
        primaryModules.map(m => `<span class="kg-chip primary">${escapeHtml(m)}</span>`).join('')
      }</div></div>`
    : '';

  const relatedHtml = relatedModules.length > 0
    ? `<div class="kg-section"><div class="kg-label">关联模块</div><div class="kg-chip-row">${
        relatedModules.map(m => `<span class="kg-chip">${escapeHtml(m)}</span>`).join('')
      }</div></div>`
    : '';

  const orderHtml = executionOrder.length > 0
    ? `<div class="kg-section"><div class="kg-label">执行顺序</div><div class="kg-order">${
        executionOrder.map((m, idx) => `${idx > 0 ? '<span class="kg-order-sep">→</span>' : ''}<span>${escapeHtml(m)}</span>`).join('')
      }</div></div>`
    : '';

  const governanceHtml = governanceHints.length > 0
    ? `<div class="kg-section"><div class="kg-label">治理提示</div><div class="kg-govern-list">${
        governanceHints.map(h => renderGovernanceHint(h)).join('')
      }</div></div>`
    : '';

  const suggestedHtml = buildSuggestedActionList(
    suggestedActions,
    { primaryModules, relatedModules, executionOrder, clarifyingQuestions }
  );

  const clarifyHtml = parsed.needsClarification && clarifyingQuestions.length > 0
    ? `<div class="kg-section"><div class="kg-label">建议先澄清</div><ul class="kg-list">${
        clarifyingQuestions.map(q => `<li>${escapeHtml(q)}</li>`).join('')
      }</ul></div>`
    : '';
  const clarifyActionHtml = parsed.needsClarification && clarifyQuickActions
    ? `<div class="kg-section"><div class="kg-label">澄清快捷动作（点击直发）</div><div class="kg-action-row">${clarifyQuickActions}</div></div>`
    : '';
  const dependencyActionHtml = dependencyQuickActions
    ? `<div class="kg-section"><div class="kg-label">依赖校验快捷动作</div><div class="kg-action-row">${dependencyQuickActions}</div></div>`
    : '';
  const moduleActionHtml = moduleQuickActions
    ? `<div class="kg-section"><div class="kg-label">模块跳转</div><div class="kg-action-row">${moduleQuickActions}</div></div>`
    : '';
  const governanceActionHtml = governanceQuickAction
    ? `<div class="kg-section"><div class="kg-label">治理快捷动作</div><div class="kg-action-row">${governanceQuickAction}</div></div>`
    : '';

  return `<div class="tool-kg">
    <div class="kg-top-row">
      <div class="kg-tags">
        <span class="kg-tag">intent: ${escapeHtml(String(parsed.intent || 'mixed'))}</span>
        <span class="kg-tag">游戏角色: ${escapeHtml(formatGameRoleTag(parsed.role || 'coder'))}</span>
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
  return value.length <= limit ? value : value.slice(0, limit - 1) + '…';
}

function formatGameRoleTag(role) {
  const normalized = String(role || '').trim().toLowerCase();
  if (normalized === 'programmer' || normalized === 'coder') return '程序';
  if (normalized === 'planner' || normalized === 'designer' || normalized === 'design') return '策划';
  if (normalized === 'artist' || normalized === 'art') return '美术';
  return '程序';
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
  return `<div class="kg-section"><div class="kg-label">建议动作（可点击执行）</div><ul class="kg-list kg-action-list">${rows}</ul></div>`;
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
      .split(/[,，]/)
      .map(v => v.trim())
      .filter(Boolean)
      .slice(0, 3);
    moduleNames.forEach(moduleName => {
      buttons.push(`<button class="kg-action-btn secondary" onclick="beginTaskFromKnowledgeCard('${encodeURIComponent(moduleName)}', this)">进入 ${escapeHtml(moduleName)}</button>`);
    });
  } else if (/begin_task\s*\(\s*\)/i.test(text)) {
    buttons.push(`<button class="kg-action-btn secondary" onclick="runSuggestedActionFromKnowledgeCard('${encodeURIComponent('请调用 begin_task() 返回项目模块速查表。')}', '${encodeURIComponent('获取模块速查表')}', this)">获取模块速查表</button>`);
  }

  if (lower.includes('validate_dependency')) {
    if (primaryModules.length > 0 && relatedModules.length > 0) {
      relatedModules.slice(0, 2).forEach(moduleName => {
        buttons.push(`<button class="kg-action-btn secondary" onclick="queueDependencyValidationFromKnowledgeCard('${encodeURIComponent(primaryModules[0])}', '${encodeURIComponent(moduleName)}', this)">校验 ${escapeHtml(primaryModules[0])} → ${escapeHtml(moduleName)}</button>`);
      });
    } else {
      buttons.push(`<button class="kg-action-btn secondary" onclick="runSuggestedActionFromKnowledgeCard('${encodeURIComponent('请调用 validate_dependency(callerModule, calleeModule) 并说明依赖是否合法。')}', '${encodeURIComponent('执行依赖校验')}', this)">执行依赖校验</button>`);
    }
  }

  if (lower.includes('evolve()') || lower.includes('治理')) {
    buttons.push(`<button class="kg-action-btn warn" onclick="runGovernanceCheckFromKnowledgeCard(this)">执行治理预检</button>`);
  }

  if (lower.includes('澄清') && clarifyingQuestions.length > 0) {
    buttons.push(`<button class="kg-action-btn secondary" onclick="askClarifyingFromKnowledgeCard('${encodeURIComponent(clarifyingQuestions[0])}', this)">发送澄清问题</button>`);
  }

  if ((lower.includes('执行顺序') || lower.includes('按顺序')) && executionOrder.length > 0) {
    const firstModule = executionOrder[0];
    buttons.push(`<button class="kg-action-btn secondary" onclick="beginTaskFromKnowledgeCard('${encodeURIComponent(firstModule)}', this)">按顺序从 ${escapeHtml(firstModule)} 开始</button>`);
  }

  if (buttons.length === 0 && lower.includes('进入模块') && primaryModules.length > 0) {
    buttons.push(`<button class="kg-action-btn secondary" onclick="beginTaskFromKnowledgeCard('${encodeURIComponent(primaryModules[0])}', this)">进入 ${escapeHtml(primaryModules[0])}</button>`);
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
        ? '撤销本条改动'
        : escapeHtml(String(detail.undoReason || '该改动不支持单条 Undo'));
      const helper = detail.truncated
        ? '<span class="tool-diff-note">Diff 过长，已自动截断预览。</span>'
        : '';
      const undoHint = !undoAvailable
        ? `<span class="tool-diff-note warn">${escapeHtml(String(detail.undoReason || '该改动不支持单条 Undo'))}</span>`
        : '';
      return `<div class="tool-diff">
        <div class="tool-diff-path">${escapeHtml(path)}</div>
        ${renderDiff(detail.oldStr || '', detail.newStr || '')}
        <div class="tool-card-actions">
          <button class="tool-keep-btn" ${keepDisabled} onclick="keepEdit('${encodedEditId}', this)">Keep</button>
          <button class="tool-undo-btn" ${undoDisabled} title="${undoTitle}" onclick="undoEdit('${encodedEditId}', '${encodedPath}', this)">Undo</button>
          ${helper}
          ${undoHint}
        </div>
      </div>`;
      }

    case 'file_created':
      return `<div class="tool-file-info">
        <span class="tool-file-path">${escapeHtml(detail.path || '')}</span>
        <span class="tool-file-size">${detail.size || 0} 字符</span>
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
        <span class="tool-file-size">${detail.lines || 0} 行</span>
      </div>`;

    case 'search':
      return `<div class="tool-search-info">搜索 "${escapeHtml(detail.pattern || '')}" — ${detail.matchCount || 0} 条匹配</div>`;

    case 'find':
      return `<div class="tool-search-info">查找 "${escapeHtml(detail.pattern || '')}" — ${detail.fileCount || 0} 个文件</div>`;

    default: return '';
  }
}

function renderDiff(oldStr, newStr) {
  const oldLines = String(oldStr || '').split('\n');
  const newLines = String(newStr || '').split('\n');
  const ops = buildGreedyDiffOps(oldLines, newLines);
  const hasChanges = ops.some(op => op.type !== 'context');
  if (!hasChanges) {
    return '<div class="diff-view unified"><div class="diff-empty">无可视化差异（内容可能只变更了换行/空白）。</div></div>';
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
      html += '<div class="diff-gap">…</div>';
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
    html += '<div class="diff-gap">…（diff 过长，已截断）</div>';
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
    setDiffActionHint(actions, result.message || 'Keep 失败', 'warn');
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
  setDiffActionHint(actions, '已保留该改动', 'ok');
}

export async function undoEdit(encodedEditId, encodedPath, btn) {
  const editId = decodeCardText(encodedEditId);
  const path = decodeCardText(encodedPath) || '该文件';
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
    setDiffActionHint(actions, result.message || `撤销 ${path} 失败`, 'warn');
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
  setDiffActionHint(actions, result.message || `已撤销 ${path}`, 'ok');
}

async function postEditAction(url, editId) {
  try {
    const resp = await apiFetch(url, {
      method: 'POST',
      body: { editId }
    });
    const data = await resp.json().catch(() => ({}));
    if (!resp.ok) {
      return { success: false, message: data.message || `请求失败 (${resp.status})` };
    }
    if (data && typeof data.success === 'boolean') {
      return data;
    }
    return { success: true, message: '' };
  } catch (err) {
    return { success: false, message: '网络异常：' + (err?.message || String(err)) };
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
  div.innerHTML = `<span>已达工具调用上限（${limit} 轮）。</span><button class="chat-continue-btn" onclick="continueChatFromLimit()">继续</button>`;
  container.appendChild(div);
  scrollToBottom();
}

export function continueChatFromLimit() {
  const prompt = document.querySelector('.chat-continue-prompt');
  if (prompt) prompt.remove();
  enqueueMessage({ text: '继续', resume: true, displayText: '继续（断点续跑）' });
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

// ── SSE streaming ──

async function streamAssistantResponse(options = {}) {
  const resume = options.resume === true;
  let interruptedThisRun = false;
  isStreaming = true;
  updateSendButton();
  setStatus(resume ? '续跑中...' : '思考中...', 'thinking');

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
      appendError(err.error || `请求失败 (${resp.status})`);
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
          const reason = evt.reason || `已自动切换到 ${toMode} 模式`;
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
          appendError(evt.content || evt.message || '未知错误');
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
      appendError('连接中断: ' + err.message);
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
  setStatus('已停止', 'warn');
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

  let html = '';

  if (providers.length === 0) {
    html = '<div class="model-dd-empty">未配置模型 —— <a href="#" onclick="event.preventDefault(); openLlmSettings()">去设置</a></div>';
  } else {
    for (const p of providers) {
      const active = p.id === activeId ? ' active' : '';
      html += `<div class="model-dd-item${active}" onclick="selectProvider('${p.id}')">`
        + `<span class="model-dd-name">${p.model}</span>`
        + `<span class="model-dd-provider">${p.name}</span>`
        + `</div>`;
    }
  }

  html += '<div class="model-dd-divider"></div>';
  html += '<div class="model-dd-empty">工具轮次固定：200（Max）</div>';
  html += `<div class="model-dd-item settings" onclick="openLlmSettings(); closeDd()">⚙ 模型设置</div>`;

  dd.innerHTML = html;
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
  const tag = $('chatModelTag');
  if (tag) tag.textContent = model || '未配置';
}

// ── Session persistence ──

function generateSessionId() {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
}

function extractTitleFromMessages(msgs) {
  if (!msgs || msgs.length === 0) return '';
  const first = msgs.find(m => m.role === 'user' && m.content);
  if (!first) return '';
  const text = first.content.trim().replace(/\s+/g, ' ');
  return text.length > 40 ? text.slice(0, 40) + '…' : text;
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

// ── Helpers ──

function escapeHtml(text) {
  const d = document.createElement('div');
  d.textContent = text;
  return d.innerHTML;
}

// ══════════════════════════════════════════════
//  Chat panel resize
// ══════════════════════════════════════════════

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
