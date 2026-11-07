// Prime Messenger Desktop (Rust / Tauri 2.0 Client)

const invoke = window.__TAURI__ ? window.__TAURI__.core.invoke : async (cmd, args) => {
  console.log(`[Mock Tauri Invoke] ${cmd}`, args);
  return getMockData(cmd, args);
};

// Global App State
const state = {
  chats: [],
  selectedChatId: null,
  messages: [],
  isRailMode: false,
  isDrawerOpen: true,
  sidebarWidth: 320,
  drawerWidth: 330,
  activeDrawerTab: 'media',
};

// DOM Elements
const el = {
  colSidebar: document.getElementById('col-sidebar'),
  splitterSidebar: document.getElementById('splitter-sidebar'),
  colChatView: document.getElementById('col-chatview'),
  splitterDrawer: document.getElementById('splitter-drawer'),
  colDrawer: document.getElementById('col-drawer'),
  chatsList: document.getElementById('chats-list'),
  messagesScroll: document.getElementById('messages-scroll'),
  messagesList: document.getElementById('messages-list'),
  msgInput: document.getElementById('msg-input'),
  btnSend: document.getElementById('btn-send'),
  btnToggleDrawer: document.getElementById('btn-toggle-drawer'),
  btnCloseDrawer: document.getElementById('btn-close-drawer'),
  chatHeaderName: document.getElementById('chat-header-name'),
  chatHeaderStatus: document.getElementById('chat-header-status'),
  chatHeaderInitials: document.getElementById('chat-header-initials'),
  drawerHeroName: document.getElementById('drawer-hero-name'),
  drawerHeroUsername: document.getElementById('drawer-hero-username'),
  drawerHeroInitials: document.getElementById('drawer-hero-initials'),
  pillTabs: document.querySelectorAll('.pill-tab'),
  tabPanes: document.querySelectorAll('.tab-pane'),
  activeChat: document.getElementById('active-chat'),
  emptyChatPlaceholder: document.getElementById('empty-chat-placeholder'),
};

// Initialize Application
async function init() {
  setupSplitter1();
  setupSplitter2();
  setupInputHandlers();
  setupDrawerTabs();

  try {
    const chats = await invoke('get_chats');
    state.chats = chats && chats.length ? chats : getFallbackChats();
    renderChats();

    if (state.chats.length > 0) {
      selectChat(state.chats[0].id);
    } else {
      showEmptyState();
    }
  } catch (err) {
    console.warn('Backend invoke error, using local fallback state:', err);
    state.chats = getFallbackChats();
    renderChats();
    selectChat(state.chats[0].id);
  }
}

// ----------------------------------------------------
// Splitter 1: Sidebar resizing & Rail mode snap trigger
// ----------------------------------------------------
function setupSplitter1() {
  let isDragging = false;
  let startX = 0;
  let startWidth = 320;

  el.splitterSidebar.addEventListener('mousedown', (e) => {
    isDragging = true;
    startX = e.clientX;
    startWidth = el.colSidebar.getBoundingClientRect().width;
    el.splitterSidebar.classList.add('dragging');
    document.body.style.cursor = 'col-resize';
    e.preventDefault();
  });

  window.addEventListener('mousemove', (e) => {
    if (!isDragging) return;
    const delta = e.clientX - startX;
    let newWidth = startWidth + delta;

    // Trigger: Snap to compact avatar rail mode when dragged towards left edge (< 180px)
    if (!state.isRailMode && newWidth < 180) {
      setRailMode(true);
      return;
    }

    // Trigger: Snap to expanded normal mode when dragging right from rail mode (> 100px or delta > 15)
    if (state.isRailMode && (newWidth > 100 || delta > 15)) {
      setRailMode(false);
      newWidth = 320;
    }

    if (!state.isRailMode) {
      newWidth = Math.max(220, Math.min(400, newWidth));
      state.sidebarWidth = newWidth;
      el.colSidebar.style.width = `${newWidth}px`;
    }
  });

  window.addEventListener('mouseup', () => {
    if (isDragging) {
      isDragging = false;
      el.splitterSidebar.classList.remove('dragging');
      document.body.style.cursor = '';
    }
  });
}

function setRailMode(rail) {
  state.isRailMode = rail;
  if (rail) {
    el.colSidebar.classList.remove('normal-mode');
    el.colSidebar.classList.add('rail-mode');
    el.colSidebar.style.width = '72px';
  } else {
    el.colSidebar.classList.remove('rail-mode');
    el.colSidebar.classList.add('normal-mode');
    el.colSidebar.style.width = `${state.sidebarWidth || 320}px`;
  }
}

// ----------------------------------------------------
// Splitter 2: Interlocutor Drawer snap-to-close trigger
// ----------------------------------------------------
function setupSplitter2() {
  let isDragging = false;
  let startX = 0;
  let startWidth = 330;

  el.splitterDrawer.addEventListener('mousedown', (e) => {
    isDragging = true;
    startX = e.clientX;
    startWidth = el.colDrawer.getBoundingClientRect().width;
    el.splitterDrawer.classList.add('dragging');
    document.body.style.cursor = 'col-resize';
    e.preventDefault();
  });

  window.addEventListener('mousemove', (e) => {
    if (!isDragging) return;
    const delta = startX - e.clientX; // Dragging right decreases drawer width
    let newWidth = startWidth + delta;

    // Trigger: Snap closed if dragged closer to the screen edge (< 235px)
    if (newWidth < 235) {
      setDrawerOpen(false);
      return;
    }

    newWidth = Math.min(440, newWidth);
    state.drawerWidth = newWidth;
    el.colDrawer.style.width = `${newWidth}px`;
  });

  window.addEventListener('mouseup', () => {
    if (isDragging) {
      isDragging = false;
      el.splitterDrawer.classList.remove('dragging');
      document.body.style.cursor = '';
    }
  });

  el.btnToggleDrawer.addEventListener('click', () => {
    setDrawerOpen(!state.isDrawerOpen);
  });

  el.btnCloseDrawer.addEventListener('click', () => {
    setDrawerOpen(false);
  });
}

function setDrawerOpen(open) {
  state.isDrawerOpen = open;
  if (open) {
    el.colDrawer.classList.remove('closed');
    el.colDrawer.classList.add('open');
    el.colDrawer.style.width = `${state.drawerWidth || 330}px`;
    el.splitterDrawer.style.display = 'block';
  } else {
    el.colDrawer.classList.remove('open');
    el.colDrawer.classList.add('closed');
    el.colDrawer.style.width = '0px';
    el.splitterDrawer.style.display = 'none';
  }
}

// ----------------------------------------------------
// Drawer Segmented Tabs (Media / Files / Audio)
// ----------------------------------------------------
function setupDrawerTabs() {
  el.pillTabs.forEach((tab) => {
    tab.addEventListener('click', () => {
      const target = tab.dataset.tab;
      state.activeDrawerTab = target;

      el.pillTabs.forEach((t) => t.classList.remove('active'));
      tab.classList.add('active');

      el.tabPanes.forEach((p) => p.classList.remove('active'));
      const activePane = document.getElementById(`tab-content-${target}`);
      if (activePane) activePane.classList.add('active');
    });
  });
}

// ----------------------------------------------------
// Chats Rendering & Selection
// ----------------------------------------------------
function renderChats() {
  el.chatsList.innerHTML = '';
  state.chats.forEach((chat) => {
    const item = document.createElement('div');
    item.className = `chat-item ${chat.id === state.selectedChatId ? 'active' : ''}`;
    item.onclick = () => selectChat(chat.id);

    item.innerHTML = `
      <div class="avatar-circle">
        <span>${chat.initials || 'PR'}</span>
      </div>
      <div class="chat-item-info normal-only">
        <div class="chat-item-top">
          <span class="chat-item-name">${chat.display_name}</span>
          <span class="chat-item-time">${chat.last_message_time || ''}</span>
        </div>
        <div class="chat-item-msg">${chat.last_message || 'Нет сообщений'}</div>
      </div>
    `;
    el.chatsList.appendChild(item);
  });
}

async function selectChat(chatId) {
  state.selectedChatId = chatId;
  const chat = state.chats.find((c) => c.id === chatId);
  if (!chat) return;

  renderChats();

  el.chatHeaderName.textContent = chat.display_name;
  el.chatHeaderStatus.textContent = chat.is_online ? 'в сети' : 'был(а) недавно';
  el.chatHeaderInitials.textContent = chat.initials;

  el.drawerHeroName.textContent = chat.display_name;
  el.drawerHeroUsername.textContent = `@${chat.username}`;
  el.drawerHeroInitials.textContent = chat.initials;

  try {
    const msgs = await invoke('get_messages', { chatId });
    state.messages = msgs || [];
  } catch {
    state.messages = getFallbackMessages(chatId);
  }

  renderMessages();
}

function showEmptyState() {
  el.activeChat.style.display = 'none';
  el.emptyChatPlaceholder.classList.remove('hidden');
}

// ----------------------------------------------------
// Messages Rendering & Virtual Scroll
// ----------------------------------------------------
function renderMessages() {
  el.messagesList.innerHTML = '';

  state.messages.forEach((msg) => {
    const row = document.createElement('div');
    row.className = `msg-row ${msg.is_from_me ? 'outgoing' : 'incoming'}`;

    row.innerHTML = `
      <div class="msg-bubble">
        <div class="msg-text">${escapeHtml(msg.text)}</div>
        <div class="msg-footer">
          <span class="msg-time">${msg.formatted_time}</span>
          ${msg.is_from_me ? '<span class="msg-check">✓✓</span>' : ''}
        </div>
      </div>
    `;
    el.messagesList.appendChild(row);
  });

  // Smooth scroll to bottom spacer
  requestAnimationFrame(() => {
    el.messagesScroll.scrollTop = el.messagesScroll.scrollHeight;
  });
}

// ----------------------------------------------------
// Message Input & Sending
// ----------------------------------------------------
function setupInputHandlers() {
  el.btnSend.addEventListener('click', sendMessage);

  el.msgInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  });

  el.msgInput.addEventListener('input', () => {
    el.msgInput.style.height = 'auto';
    el.msgInput.style.height = `${Math.min(el.msgInput.scrollHeight, 100)}px`;
  });
}

async function sendMessage() {
  const text = el.msgInput.value.trim();
  if (!text || !state.selectedChatId) return;

  el.msgInput.value = '';
  el.msgInput.style.height = 'auto';

  try {
    const sentMsg = await invoke('send_message', {
      chatId: state.selectedChatId,
      text: text,
      replyToId: null,
      replyToSender: null,
      replyToText: null,
    });
    state.messages.push(sentMsg);
  } catch {
    // Local fallback message
    const now = new Date();
    const fallback = {
      id: String(Date.now()),
      chat_id: state.selectedChatId,
      sender: 'Me',
      text: text,
      timestamp: Date.now(),
      formatted_time: `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`,
      is_from_me: true,
      packet_type: 'Text',
      reactions: [],
      is_first_in_chat: false,
      is_last_in_chat: true,
    };
    state.messages.push(fallback);
  }

  renderMessages();
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// Mock & Fallback Data
function getFallbackChats() {
  return [
    {
      id: 'alice',
      display_name: 'Алиса Смирнова',
      username: 'alice_prime',
      initials: 'АС',
      last_message: 'Привет! Клиент на Rust работает отлично! 🚀',
      last_message_time: '12:45',
      unread_count: 0,
      is_online: true,
    },
    {
      id: 'ivan',
      display_name: 'Иван Петров',
      username: 'ivan_dev',
      initials: 'ИП',
      last_message: 'Отправил файлы проекта',
      last_message_time: '11:20',
      unread_count: 1,
      is_online: false,
    },
    {
      id: 'team',
      display_name: 'Команда Prime',
      username: 'prime_team',
      initials: 'КП',
      last_message: 'Релиз Prime v2.0 на Rust готов к тестированию',
      last_message_time: 'Вчера',
      unread_count: 0,
      is_online: true,
    }
  ];
}

function getFallbackMessages(chatId) {
  return [
    {
      id: '1',
      chat_id: chatId,
      sender: chatId,
      text: 'Привет! Добро пожаловать в новый Prime Desktop на языке Rust! 🎉',
      formatted_time: '12:40',
      is_from_me: false,
    },
    {
      id: '2',
      chat_id: chatId,
      sender: 'Me',
      text: 'Выглядит потрясающе: матовое стекло, островки, отзывчивые сплиттеры и мгновенная скорость отклика!',
      formatted_time: '12:42',
      is_from_me: true,
    },
    {
      id: '3',
      chat_id: chatId,
      sender: chatId,
      text: 'Да, вся сетевая часть, P2P сокеты и протокол пакетов теперь работают напрямую в нативном Rust! 🦀',
      formatted_time: '12:45',
      is_from_me: false,
    }
  ];
}

function getMockData(cmd, args) {
  if (cmd === 'get_chats') return getFallbackChats();
  if (cmd === 'get_messages') return getFallbackMessages(args.chatId);
  return null;
}

// Run application on load
window.addEventListener('DOMContentLoaded', init);
