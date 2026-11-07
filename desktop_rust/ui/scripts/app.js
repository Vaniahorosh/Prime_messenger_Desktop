// Prime Messenger Desktop (Native Rust + Tauri 2.0 Client)

const invoke = window.__TAURI__ ? window.__TAURI__.core.invoke : async (cmd, args) => {
  console.log(`[Mock Tauri Invoke] ${cmd}`, args);
  return getMockData(cmd, args);
};

const listen = window.__TAURI__ ? window.__TAURI__.event.listen : (ev, cb) => {
  console.log(`[Mock Tauri Listen] ${ev}`);
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
  replyingTo: null,
  editingMsg: null,
  contextTargetMsgId: null,
  userSettings: null,
  currentLightboxSrc: null,
  
  // Voice Recording
  mediaRecorder: null,
  audioChunks: [],
  voiceTimerInterval: null,
  voiceSeconds: 0,

  // Browser State
  browserTabs: [{ id: 1, title: 'DuckDuckGo', url: 'https://duckduckgo.com' }],
  activeBrowserTabId: 1,

  // Photo Editor State
  editorImage: null,
  editorRotation: 0,
  editorFlipH: false,
  editorFlipV: false,
  editorBrightness: 0,
  editorContrast: 0,
  editorSaturation: 0,
  editorBrushColor: '#4D9FFF',
  editorBrushSize: 4,
  editorStrokes: [],
  currentStroke: [],
  isPainting: false,

  // PIN Lock State
  enteredPin: '',
  isLocked: false,
};

// DOM Elements Cache
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
  btnAttach: document.getElementById('btn-attach'),
  fileInputHidden: document.getElementById('file-input-hidden'),
  btnEmoji: document.getElementById('btn-emoji'),
  btnVoice: document.getElementById('btn-voice'),
  voiceRecordBar: document.getElementById('voice-record-bar'),
  voiceRecordTimer: document.getElementById('voice-record-timer'),
  btnCancelVoice: document.getElementById('btn-cancel-voice'),
  btnSendVoice: document.getElementById('btn-send-voice'),
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
  inputModifierBar: document.getElementById('input-modifier-bar'),
  modifierTitle: document.getElementById('modifier-title'),
  modifierText: document.getElementById('modifier-text'),
  btnCancelModifier: document.getElementById('btn-cancel-modifier'),
  contextMenu: document.getElementById('context-menu'),
  modalNetwork: document.getElementById('modal-network'),
  modalSettings: document.getElementById('modal-settings'),
  modalLightbox: document.getElementById('modal-lightbox'),
  lightboxImg: document.getElementById('lightbox-img'),
  btnLightboxEdit: document.getElementById('btn-lightbox-edit'),
  btnScanNetwork: document.getElementById('btn-scan-network'),
  btnRailScan: document.getElementById('btn-rail-scan'),
  btnEmptyScan: document.getElementById('btn-empty-scan'),
  btnBrowser: document.getElementById('btn-browser'),
  btnRailBrowser: document.getElementById('btn-rail-browser'),
  btnSettings: document.getElementById('btn-settings'),
  btnRailSettings: document.getElementById('btn-rail-settings'),
  btnCloseScanModal: document.getElementById('btn-close-scan-modal'),
  btnCloseSettingsModal: document.getElementById('btn-close-settings-modal'),
  btnCloseLightbox: document.getElementById('btn-close-lightbox'),
  btnRefreshScan: document.getElementById('btn-refresh-scan'),
  scanResultsList: document.getElementById('scan-results-list'),
  btnSaveSettings: document.getElementById('btn-save-settings'),
  toastContainer: document.getElementById('toast-container'),
  screenWatermark: document.getElementById('screen-watermark'),
  lockScreen: document.getElementById('lock-screen'),
  pinDots: document.querySelectorAll('#pin-dots .dot'),
  modalBrowser: document.getElementById('modal-browser'),
  browserFrame: document.getElementById('browser-frame'),
  browserUrlInput: document.getElementById('browser-url-input'),
  modalPhotoEditor: document.getElementById('modal-photo-editor'),
  photoCanvas: document.getElementById('photo-canvas'),
  modalEmojiPicker: document.getElementById('modal-emoji-picker'),
};

// ----------------------------------------------------
// App Initialization
// ----------------------------------------------------
async function init() {
  setupSplitter1();
  setupSplitter2();
  setupInputHandlers();
  setupDrawerTabs();
  setupModals();
  setupContextMenu();
  setupRealtimeEvents();
  setupVoiceRecording();
  setupBrowser();
  setupPhotoEditor();
  setupEmojiPicker();
  setupPinLock();

  // Load User Settings
  try {
    state.userSettings = await invoke('get_settings');
    applyUserSettings(state.userSettings);

    // If PIN is enabled, lock app
    if (state.userSettings.pin_code && state.userSettings.pin_code.trim().length === 4) {
      lockApp();
    }
  } catch (err) {
    console.warn('Could not load settings:', err);
  }

  // Load Chats
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
    console.warn('Backend invoke error, using fallback state:', err);
    state.chats = getFallbackChats();
    renderChats();
    selectChat(state.chats[0].id);
  }
}

// ----------------------------------------------------
// Real-time Event Listeners
// ----------------------------------------------------
function setupRealtimeEvents() {
  listen('new-message', (event) => {
    const msg = event.payload;
    if (msg.chat_id === state.selectedChatId) {
      state.messages.push(msg);
      renderMessages();
    } else {
      // Toast notification for incoming messages from other chats
      const chat = state.chats.find((c) => c.id === msg.chat_id);
      showToastNotification(chat ? chat.display_name : 'Новое сообщение', msg.text || 'Медиафайл', msg.chat_id);
    }

    const chat = state.chats.find((c) => c.id === msg.chat_id);
    if (chat) {
      chat.last_message = msg.text || (msg.is_photo ? '📷 Фотография' : (msg.is_voice ? '🎤 Голосовое сообщение' : 'Сообщение'));
      chat.last_message_time = msg.formatted_time;
      renderChats();
    }
  });

  listen('message-edited', (event) => {
    const { id, text } = event.payload;
    const msg = state.messages.find((m) => m.id === id);
    if (msg) {
      msg.text = text;
      msg.is_edited = true;
      renderMessages();
    }
  });

  listen('message-deleted', (event) => {
    const { id } = event.payload;
    state.messages = state.messages.filter((m) => m.id !== id);
    renderMessages();
  });

  listen('reaction-updated', (event) => {
    const { msg_id, reaction, author } = event.payload;
    const msg = state.messages.find((m) => m.id === msg_id);
    if (msg) {
      if (!msg.reactions) msg.reactions = [];
      for (const g of msg.reactions) {
        const idx = g.users.indexOf(author);
        if (idx !== -1) {
          g.users.splice(idx, 1);
          g.count--;
        }
      }
      msg.reactions = msg.reactions.filter((g) => g.count > 0);
      if (reaction) {
        const exist = msg.reactions.find((g) => g.emoji === reaction);
        if (exist) {
          exist.users.push(author);
          exist.count++;
          if (author === 'Me') exist.is_selected_by_me = true;
        } else {
          msg.reactions.push({
            emoji: reaction,
            count: 1,
            is_selected_by_me: author === 'Me',
            users: [author],
          });
        }
      }
      renderMessages();
    }
  });

  listen('peer-status', (event) => {
    const { chat_id, is_online, status_text } = event.payload;
    const chat = state.chats.find((c) => c.id === chat_id);
    if (chat) {
      chat.is_online = is_online;
      chat.status_text = status_text;
      renderChats();
      if (state.selectedChatId === chat_id) {
        el.chatHeaderStatus.textContent = status_text;
      }
    }
  });
}

// ----------------------------------------------------
// Splitter 1 & 2 (Sidebar & Drawer)
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

    if (!state.isRailMode && newWidth < 180) {
      setRailMode(true);
      return;
    }

    if (state.isRailMode && (newWidth > 100 || delta > 15)) {
      setRailMode(false);
      newWidth = 320;
    }

    if (!state.isRailMode) {
      newWidth = Math.max(220, Math.min(420, newWidth));
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
    const delta = startX - e.clientX;
    let newWidth = startWidth + delta;

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

  el.btnToggleDrawer.addEventListener('click', () => setDrawerOpen(!state.isDrawerOpen));
  el.btnCloseDrawer.addEventListener('click', () => setDrawerOpen(false));
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

    const isBt = chat.device_type === 'bluetooth' || chat.id.startsWith('peer_');
    const badge = isBt ? '<span class="peer-badge-bt">BT</span>' : '';

    item.innerHTML = `
      <div class="avatar-circle">
        <span>${chat.initials || 'PR'}</span>
      </div>
      <div class="chat-item-info normal-only">
        <div class="chat-item-top">
          <span class="chat-item-name">${escapeHtml(chat.display_name)} ${badge}</span>
          <span class="chat-item-time">${chat.last_message_time || ''}</span>
        </div>
        <div class="chat-item-msg">${escapeHtml(chat.last_message || 'Нет сообщений')}</div>
      </div>
    `;
    el.chatsList.appendChild(item);
  });
}

async function selectChat(chatId) {
  state.selectedChatId = chatId;
  const chat = state.chats.find((c) => c.id === chatId);
  if (!chat) return;

  el.activeChat.style.display = 'flex';
  el.emptyChatPlaceholder.classList.add('hidden');
  renderChats();

  el.chatHeaderName.textContent = chat.display_name;
  el.chatHeaderStatus.textContent = chat.status_text || (chat.is_online ? 'в сети' : 'не в сети');
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
// Messages Rendering & Circular Video Notes & Waveforms
// ----------------------------------------------------
function renderMessages() {
  el.messagesList.innerHTML = '';

  state.messages.forEach((msg) => {
    const row = document.createElement('div');
    row.className = `msg-row ${msg.is_from_me ? 'outgoing' : 'incoming'}`;
    row.dataset.msgId = msg.id;

    row.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      openContextMenu(e.clientX, e.clientY, msg);
    });

    let replyHtml = '';
    if (msg.reply_to_text) {
      replyHtml = `
        <div class="reply-quote-box">
          <span class="reply-quote-sender">${escapeHtml(msg.reply_to_sender || 'Собеседник')}</span>
          <span class="reply-quote-text">${escapeHtml(msg.reply_to_text)}</span>
        </div>
      `;
    }

    let previewHtml = '';
    if (msg.link_preview) {
      previewHtml = `
        <a class="link-preview-card" href="${escapeHtml(msg.link_preview.url)}" target="_blank">
          <div class="link-preview-title">${escapeHtml(msg.link_preview.title)}</div>
          <div class="link-preview-desc">${escapeHtml(msg.link_preview.description || '')}</div>
          <div class="link-preview-domain">${escapeHtml(msg.link_preview.domain)}</div>
        </a>
      `;
    }

    let reactionsHtml = '';
    if (msg.reactions && msg.reactions.length > 0) {
      reactionsHtml = '<div class="reaction-chips">';
      for (const rx of msg.reactions) {
        reactionsHtml += `
          <div class="reaction-chip ${rx.is_selected_by_me ? 'active' : ''}" onclick="toggleReaction('${msg.id}', '${rx.emoji}')">
            <span>${rx.emoji}</span>
            <span>${rx.count}</span>
          </div>
        `;
      }
      reactionsHtml += '</div>';
    }

    // Multimedia Handlers: Photo / Voice Waveform / Video Note
    let mediaHtml = '';
    if (msg.is_photo && msg.file_path) {
      mediaHtml = `<img class="chat-photo" src="asset://${msg.file_path}" onclick="openLightbox('${msg.file_path}')" style="max-width:280px; border-radius:10px; cursor:pointer; margin-bottom:6px;">`;
    } else if (msg.is_voice && msg.file_path) {
      mediaHtml = renderVoiceWaveformHtml(msg);
    } else if (msg.is_video && msg.file_path) {
      mediaHtml = `
        <div class="video-note-bubble" onclick="toggleVideoPlayback(this)">
          <video class="video-note-circle" src="asset://${msg.file_path}" playsinline loop autoplay muted></video>
          <span class="video-note-speed" onclick="event.stopPropagation(); cycleVideoSpeed(this)">1.0x</span>
        </div>
      `;
    }

    row.innerHTML = `
      <div class="msg-bubble">
        ${replyHtml}
        ${mediaHtml}
        ${msg.text ? `<div class="msg-text">${escapeHtml(msg.text)}</div>` : ''}
        ${previewHtml}
        <div class="msg-footer">
          ${msg.is_edited ? '<span class="msg-edited">изм.</span>' : ''}
          <span class="msg-time">${msg.formatted_time}</span>
          ${msg.is_from_me ? '<span class="msg-check">✓✓</span>' : ''}
        </div>
        ${reactionsHtml}
      </div>
    `;
    el.messagesList.appendChild(row);
  });

  requestAnimationFrame(() => {
    el.messagesScroll.scrollTop = el.messagesScroll.scrollHeight;
  });
}

function renderVoiceWaveformHtml(msg) {
  const bars = [12, 18, 24, 14, 20, 26, 16, 22, 18, 12, 24, 28, 22, 16, 14, 20, 26, 18, 14, 22];
  let barsHtml = '';
  bars.forEach((h) => {
    barsHtml += `<span class="waveform-bar" style="height:${h}px"></span>`;
  });

  return `
    <div class="waveform-wrap" data-file="${msg.file_path}">
      <button class="waveform-play-btn" onclick="toggleVoicePlay(this, '${msg.file_path}')">▶</button>
      <div class="waveform-bars">${barsHtml}</div>
      <span class="waveform-time">${msg.duration || '0:05'}</span>
      <audio class="voice-audio-element" src="asset://${msg.file_path}" preload="none"></audio>
    </div>
  `;
}

let activeAudio = null;
let activePlayBtn = null;

function toggleVoicePlay(btn, path) {
  const wrap = btn.closest('.waveform-wrap');
  const audio = wrap.querySelector('.voice-audio-element');

  if (activeAudio && activeAudio !== audio) {
    activeAudio.pause();
    activeAudio.currentTime = 0;
    if (activePlayBtn) activePlayBtn.textContent = '▶';
  }

  if (audio.paused) {
    audio.play();
    btn.textContent = '⏸';
    activeAudio = audio;
    activePlayBtn = btn;
    audio.onended = () => {
      btn.textContent = '▶';
    };
  } else {
    audio.pause();
    btn.textContent = '▶';
  }
}

function toggleVideoPlayback(wrap) {
  const video = wrap.querySelector('video');
  if (video.paused) {
    video.play();
  } else {
    video.pause();
  }
}

function cycleVideoSpeed(badge) {
  const video = badge.parentElement.querySelector('video');
  if (video.playbackRate === 1.0) {
    video.playbackRate = 1.5;
    badge.textContent = '1.5x';
  } else if (video.playbackRate === 1.5) {
    video.playbackRate = 2.0;
    badge.textContent = '2.0x';
  } else {
    video.playbackRate = 1.0;
    badge.textContent = '1.0x';
  }
}

// ----------------------------------------------------
// Voice Message Recording (Web Audio / MediaRecorder)
// ----------------------------------------------------
function setupVoiceRecording() {
  el.btnVoice.addEventListener('click', startVoiceRecording);
  el.btnCancelVoice.addEventListener('click', cancelVoiceRecording);
  el.btnSendVoice.addEventListener('click', finishAndSendVoice);
}

async function startVoiceRecording() {
  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    state.audioChunks = [];
    state.mediaRecorder = new MediaRecorder(stream);

    state.mediaRecorder.ondataavailable = (e) => {
      if (e.data.size > 0) state.audioChunks.push(e.data);
    };

    state.mediaRecorder.start();
    invoke('play_sound_effect', { effect: 'record_start', volume: 0.7 }).catch(() => {});

    state.voiceSeconds = 0;
    el.voiceRecordTimer.textContent = '0:00';
    el.voiceRecordBar.classList.remove('hidden');

    state.voiceTimerInterval = setInterval(() => {
      state.voiceSeconds++;
      const m = Math.floor(state.voiceSeconds / 60);
      const s = state.voiceSeconds % 60;
      el.voiceRecordTimer.textContent = `${m}:${s < 10 ? '0' : ''}${s}`;
    }, 1000);
  } catch (err) {
    console.warn('Microphone error:', err);
    alert('Не удалось получить доступ к микрофону: ' + err.message);
  }
}

function cancelVoiceRecording() {
  if (state.mediaRecorder && state.mediaRecorder.state !== 'inactive') {
    state.mediaRecorder.stop();
  }
  clearInterval(state.voiceTimerInterval);
  el.voiceRecordBar.classList.add('hidden');
  invoke('play_sound_effect', { effect: 'record_stop', volume: 0.7 }).catch(() => {});
}

async function finishAndSendVoice() {
  if (!state.mediaRecorder || state.mediaRecorder.state === 'inactive') return;

  const durationStr = el.voiceRecordTimer.textContent;
  clearInterval(state.voiceTimerInterval);
  el.voiceRecordBar.classList.add('hidden');
  invoke('play_sound_effect', { effect: 'record_stop', volume: 0.7 }).catch(() => {});

  state.mediaRecorder.onstop = async () => {
    const blob = new Blob(state.audioChunks, { type: 'audio/webm' });
    const reader = new FileReader();
    reader.onloadend = async () => {
      const base64Audio = reader.result;
      if (state.selectedChatId) {
        try {
          const msg = await invoke('send_voice_recording', {
            chatId: state.selectedChatId,
            base64Audio,
            duration: durationStr,
          });
          state.messages.push(msg);
          renderMessages();
        } catch (err) {
          console.warn('Send voice error:', err);
        }
      }
    };
    reader.readAsDataURL(blob);
  };

  state.mediaRecorder.stop();
}

// ----------------------------------------------------
// Message Input & Attachments
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
    if (state.selectedChatId) {
      invoke('send_typing', { chatId: state.selectedChatId }).catch(() => {});
    }
  });

  el.btnCancelModifier.addEventListener('click', cancelModifier);
  el.btnAttach.addEventListener('click', () => el.fileInputHidden.click());

  el.fileInputHidden.addEventListener('change', async (e) => {
    const file = e.target.files[0];
    if (!file || !state.selectedChatId) return;
    try {
      await invoke('send_file', {
        chatId: state.selectedChatId,
        filePath: file.path || file.name,
        caption: file.name,
        isVoice: false,
        duration: null,
      });
    } catch (err) {
      console.warn('Send file err:', err);
    }
  });
}

function cancelModifier() {
  state.replyingTo = null;
  state.editingMsg = null;
  el.inputModifierBar.classList.add('hidden');
}

async function sendMessage() {
  const text = el.msgInput.value.trim();
  if (!text || !state.selectedChatId) return;

  el.msgInput.value = '';
  el.msgInput.style.height = 'auto';

  if (state.editingMsg) {
    const editId = state.editingMsg.id;
    cancelModifier();
    try {
      await invoke('edit_message', {
        chatId: state.selectedChatId,
        msgId: editId,
        newText: text,
      });
      const m = state.messages.find((x) => x.id === editId);
      if (m) {
        m.text = text;
        m.is_edited = true;
        renderMessages();
      }
    } catch (err) {
      console.warn('Edit error:', err);
    }
    return;
  }

  const reply = state.replyingTo;
  cancelModifier();

  try {
    const sentMsg = await invoke('send_message', {
      chatId: state.selectedChatId,
      text: text,
      replyToId: reply ? reply.id : null,
      replyToSender: reply ? reply.sender : null,
      replyToText: reply ? reply.text : null,
    });
    state.messages.push(sentMsg);
    renderMessages();
  } catch (err) {
    console.warn('Send error:', err);
  }
}

// ----------------------------------------------------
// Context Menu & Reactions
// ----------------------------------------------------
function setupContextMenu() {
  window.addEventListener('click', () => el.contextMenu.classList.add('hidden'));

  document.querySelectorAll('.quick-rx-btn').forEach((btn) => {
    btn.addEventListener('click', () => {
      const emoji = btn.dataset.emoji;
      if (state.contextTargetMsgId && state.selectedChatId) {
        toggleReaction(state.contextTargetMsgId, emoji);
      }
      el.contextMenu.classList.add('hidden');
    });
  });

  document.getElementById('cm-reply').addEventListener('click', () => {
    const msg = state.messages.find((m) => m.id === state.contextTargetMsgId);
    if (!msg) return;
    state.replyingTo = { id: msg.id, sender: msg.sender, text: msg.text };
    state.editingMsg = null;
    el.modifierTitle.textContent = `Ответ пользователю ${msg.sender}`;
    el.modifierText.textContent = msg.text;
    el.inputModifierBar.classList.remove('hidden');
    el.msgInput.focus();
  });

  document.getElementById('cm-edit').addEventListener('click', () => {
    const msg = state.messages.find((m) => m.id === state.contextTargetMsgId);
    if (!msg || !msg.is_from_me) return;
    state.editingMsg = { id: msg.id, text: msg.text };
    state.replyingTo = null;
    el.modifierTitle.textContent = 'Редактирование сообщения';
    el.modifierText.textContent = msg.text;
    el.msgInput.value = msg.text;
    el.inputModifierBar.classList.remove('hidden');
    el.msgInput.focus();
  });

  document.getElementById('cm-copy').addEventListener('click', () => {
    const msg = state.messages.find((m) => m.id === state.contextTargetMsgId);
    if (msg) navigator.clipboard.writeText(msg.text);
  });

  document.getElementById('cm-delete-me').addEventListener('click', async () => {
    if (state.contextTargetMsgId && state.selectedChatId) {
      await invoke('delete_message', {
        chatId: state.selectedChatId,
        msgId: state.contextTargetMsgId,
        forEveryone: false,
      });
      state.messages = state.messages.filter((m) => m.id !== state.contextTargetMsgId);
      renderMessages();
    }
  });

  document.getElementById('cm-delete-all').addEventListener('click', async () => {
    if (state.contextTargetMsgId && state.selectedChatId) {
      await invoke('delete_message', {
        chatId: state.selectedChatId,
        msgId: state.contextTargetMsgId,
        forEveryone: true,
      });
      state.messages = state.messages.filter((m) => m.id !== state.contextTargetMsgId);
      renderMessages();
    }
  });
}

function openContextMenu(x, y, msg) {
  state.contextTargetMsgId = msg.id;
  const cmEdit = document.getElementById('cm-edit');
  const cmDelAll = document.getElementById('cm-delete-all');

  if (msg.is_from_me) {
    cmEdit.style.display = 'block';
    cmDelAll.style.display = 'block';
  } else {
    cmEdit.style.display = 'none';
    cmDelAll.style.display = 'none';
  }

  el.contextMenu.style.left = `${Math.min(x, window.innerWidth - 200)}px`;
  el.contextMenu.style.top = `${Math.min(y, window.innerHeight - 250)}px`;
  el.contextMenu.classList.remove('hidden');
}

async function toggleReaction(msgId, emoji) {
  if (!state.selectedChatId) return;
  try {
    await invoke('send_reaction', {
      chatId: state.selectedChatId,
      msgId: msgId,
      emoji: emoji,
    });
  } catch (err) {
    console.warn('Reaction error:', err);
  }
}

// ----------------------------------------------------
// In-App Web Browser ("Prime Browser")
// ----------------------------------------------------
function setupBrowser() {
  const openBrowser = () => el.modalBrowser.classList.remove('hidden');
  el.btnBrowser.addEventListener('click', openBrowser);
  el.btnRailBrowser.addEventListener('click', openBrowser);
  document.getElementById('btn-browser-close').addEventListener('click', () => el.modalBrowser.classList.add('hidden'));

  document.getElementById('btn-browser-reload').addEventListener('click', () => {
    el.browserFrame.src = el.browserFrame.src;
  });

  document.getElementById('btn-browser-home').addEventListener('click', () => {
    navigateTo('https://duckduckgo.com');
  });

  el.browserUrlInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      let url = el.browserUrlInput.value.trim();
      if (!url.startsWith('http://') && !url.startsWith('https://')) {
        if (url.includes('.') && !url.includes(' ')) {
          url = 'https://' + url;
        } else {
          url = `https://duckduckgo.com/?q=${encodeURIComponent(url)}`;
        }
      }
      navigateTo(url);
    }
  });

  document.getElementById('btn-browser-bookmark').addEventListener('click', async () => {
    const url = el.browserUrlInput.value.trim();
    if (url) {
      await invoke('add_bookmark', { url, title: url, icon: null });
      loadBrowserBookmarks();
    }
  });

  loadBrowserBookmarks();
}

function navigateTo(url) {
  el.browserFrame.src = url;
  el.browserUrlInput.value = url;
  invoke('add_browser_history', { url, title: url }).catch(() => {});
}

async function loadBrowserBookmarks() {
  const bar = document.getElementById('browser-bookmarks-bar');
  bar.innerHTML = '';
  try {
    const bookmarks = await invoke('get_bookmarks');
    if (bookmarks && bookmarks.length > 0) {
      bookmarks.forEach((b) => {
        const item = document.createElement('div');
        item.className = 'browser-bookmark-item';
        item.textContent = b.title;
        item.onclick = () => navigateTo(b.url);
        bar.appendChild(item);
      });
    } else {
      const defaultBm = document.createElement('div');
      defaultBm.className = 'browser-bookmark-item';
      defaultBm.textContent = '🔍 DuckDuckGo';
      defaultBm.onclick = () => navigateTo('https://duckduckgo.com');
      bar.appendChild(defaultBm);
    }
  } catch { }
}

// ----------------------------------------------------
// Photo Editor ("Фоторедактор Прайм")
// ----------------------------------------------------
function setupPhotoEditor() {
  el.btnLightboxEdit.addEventListener('click', () => {
    el.modalLightbox.classList.add('hidden');
    openPhotoEditor(state.currentLightboxSrc);
  });

  document.getElementById('btn-close-photo-editor').addEventListener('click', () => {
    el.modalPhotoEditor.classList.add('hidden');
  });

  // Tool bindings
  document.getElementById('tool-rotate-cw').addEventListener('click', () => {
    state.editorRotation = (state.editorRotation + 90) % 360;
    redrawCanvas();
  });

  document.getElementById('tool-flip-h').addEventListener('click', () => {
    state.editorFlipH = !state.editorFlipH;
    redrawCanvas();
  });

  document.getElementById('tool-flip-v').addEventListener('click', () => {
    state.editorFlipV = !state.editorFlipV;
    redrawCanvas();
  });

  document.querySelectorAll('.color-dot').forEach((dot) => {
    dot.addEventListener('click', () => {
      document.querySelectorAll('.color-dot').forEach((d) => d.classList.remove('active'));
      dot.classList.add('active');
      state.editorBrushColor = dot.dataset.color;
    });
  });

  document.getElementById('brush-size').addEventListener('input', (e) => {
    state.editorBrushSize = parseInt(e.target.value);
  });

  document.getElementById('tool-undo-stroke').addEventListener('click', () => {
    state.editorStrokes.pop();
    redrawCanvas();
  });

  document.getElementById('filter-brightness').addEventListener('input', (e) => {
    state.editorBrightness = parseInt(e.target.value);
    redrawCanvas();
  });

  document.getElementById('filter-contrast').addEventListener('input', (e) => {
    state.editorContrast = parseInt(e.target.value);
    redrawCanvas();
  });

  document.getElementById('filter-saturation').addEventListener('input', (e) => {
    state.editorSaturation = parseInt(e.target.value);
    redrawCanvas();
  });

  document.getElementById('tool-reset-filters').addEventListener('click', () => {
    state.editorBrightness = 0;
    state.editorContrast = 0;
    state.editorSaturation = 0;
    document.getElementById('filter-brightness').value = 0;
    document.getElementById('filter-contrast').value = 0;
    document.getElementById('filter-saturation').value = 0;
    redrawCanvas();
  });

  document.getElementById('btn-editor-apply-send').addEventListener('click', async () => {
    const dataUrl = el.photoCanvas.toDataURL('image/png');
    if (state.selectedChatId) {
      try {
        const msg = await invoke('send_edited_photo', {
          chatId: state.selectedChatId,
          base64Image: dataUrl,
          caption: 'Отредактированное фото',
        });
        state.messages.push(msg);
        renderMessages();
      } catch (err) {
        console.warn('Send edited photo error:', err);
      }
    }
    el.modalPhotoEditor.classList.add('hidden');
  });

  // Canvas drawing events
  el.photoCanvas.addEventListener('mousedown', (e) => {
    state.isPainting = true;
    const rect = el.photoCanvas.getBoundingClientRect();
    state.currentStroke = {
      color: state.editorBrushColor,
      size: state.editorBrushSize,
      points: [{ x: e.clientX - rect.left, y: e.clientY - rect.top }],
    };
  });

  el.photoCanvas.addEventListener('mousemove', (e) => {
    if (!state.isPainting) return;
    const rect = el.photoCanvas.getBoundingClientRect();
    state.currentStroke.points.push({ x: e.clientX - rect.left, y: e.clientY - rect.top });
    redrawCanvas();
  });

  window.addEventListener('mouseup', () => {
    if (state.isPainting) {
      state.isPainting = false;
      if (state.currentStroke && state.currentStroke.points.length > 1) {
        state.editorStrokes.push(state.currentStroke);
      }
      state.currentStroke = null;
    }
  });
}

function openPhotoEditor(imgSrc) {
  const img = new Image();
  img.crossOrigin = 'anonymous';
  img.onload = () => {
    state.editorImage = img;
    state.editorRotation = 0;
    state.editorFlipH = false;
    state.editorFlipV = false;
    state.editorBrightness = 0;
    state.editorContrast = 0;
    state.editorSaturation = 0;
    state.editorStrokes = [];
    el.modalPhotoEditor.classList.remove('hidden');
    redrawCanvas();
  };
  img.src = `asset://${imgSrc}`;
}

function redrawCanvas() {
  if (!state.editorImage) return;
  const ctx = el.photoCanvas.getContext('2d');
  const img = state.editorImage;

  const isRotated = state.editorRotation === 90 || state.editorRotation === 270;
  el.photoCanvas.width = isRotated ? img.height : img.width;
  el.photoCanvas.height = isRotated ? img.width : img.height;

  ctx.save();
  ctx.clearRect(0, 0, el.photoCanvas.width, el.photoCanvas.height);

  // Apply filters
  const b = 100 + state.editorBrightness;
  const c = 100 + state.editorContrast;
  const s = 100 + state.editorSaturation;
  ctx.filter = `brightness(${b}%) contrast(${c}%) saturate(${s}%)`;

  // Apply transforms
  ctx.translate(el.photoCanvas.width / 2, el.photoCanvas.height / 2);
  ctx.rotate((state.editorRotation * Math.PI) / 180);
  ctx.scale(state.editorFlipH ? -1 : 1, state.editorFlipV ? -1 : 1);
  ctx.drawImage(img, -img.width / 2, -img.height / 2);
  ctx.restore();

  // Draw strokes
  const allStrokes = [...state.editorStrokes];
  if (state.currentStroke) allStrokes.push(state.currentStroke);

  allStrokes.forEach((stroke) => {
    if (!stroke.points || stroke.points.length < 2) return;
    ctx.beginPath();
    ctx.strokeStyle = stroke.color;
    ctx.lineWidth = stroke.size;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.moveTo(stroke.points[0].x, stroke.points[0].y);
    for (let i = 1; i < stroke.points.length; i++) {
      ctx.lineTo(stroke.points[i].x, stroke.points[i].y);
    }
    ctx.stroke();
  });
}

// ----------------------------------------------------
// Categorized Emoji Picker
// ----------------------------------------------------
const EMOJI_CATEGORIES = {
  smileys: ['😀', '😃', '😄', '😁', '😆', '😅', '😂', '🤣', '😊', '😇', '🙂', '😉', '😌', '😍', '🥰', '😘', '😋', '😜', '🤪', '😎', '🤓', '🥳', '😏', '😒', '😞', '😔', '😟', '😕', '🙁', '😢', '😭', '😤', '😡', '🤬', '🤯', '😳', '😱', '😨', '🥶', '🥵', '😴', '🤤', '😷', '🤒', '🤕', '🤢', '🤮', '🤧'],
  gestures: ['👍', '👎', '👊', '✊', '🤛', '🤜', '👏', '🙌', '👐', '🤲', '🤝', '🙏', '✌️', '🤟', '🤘', '👌', '🤏', '👈', '👉', '👆', '👇', '✋', '🤚', '🖐', '🖖', '👋', '🤙', '💪', '🖕', '✍️', '🤳', '💅'],
  nature: ['🐶', '🐱', '🐭', '🐹', '🐰', '🦊', '🐻', '🐼', '🐨', '🐯', '🦁', '🐮', '🐷', '🐸', '🐵', '🐔', '🐧', '🐦', '🦅', '🦆', '🦉', '🦇', '🐺', '🐗', '🐴', '🦄', '🐝', '🐛', '🦋', '🐌', '🐞', '🐜', '🦟', '🐢', '🐍', '🐙', '🦑', '🦐', '🦀', '🐡', '🐠', '🐟', '🐬', '🐳', '🦈', '🐊', '🐅', '🐆'],
  food: ['🍏', '🍎', '🍐', '🍊', '🍋', '🍌', '🍉', '🍇', '🍓', '🫐', '🍈', '🍒', '🍑', '🥭', '🍍', '🥥', '🥝', '🍅', '🍆', '🥑', '🥦', '🌽', '🥕', '🧄', '🧅', '🥔', '🍠', '🥐', '🥯', '🍞', '🥖', '🥨', '🧀', '🥚', '🍳', '🥞', '🥓', '🥩', '🍗', '🍖', '🌭', '🍔', '🍟', '🍕', '🌮', '🌯', '🥗', '🍲'],
  activity: ['⚽️', '🏀', '🏈', '⚾️', '🥎', '🎾', '🏐', '🏉', '🥏', '🎱', '🏓', '🏸', '🏒', '🏑', '🥍', '🏏', '🥅', '⛳️', '🏹', '🎣', '🥊', '🥋', '🛹', '🛼', '🛷', '⛸', '🥌', '🎿', '⛷', '🏂', '🏋️‍♀️', '🤼‍♂️', '🤸‍♀️', '🚀', '✈️', '⛵️', '🚗', '🏎', '🏍', '🛵'],
  objects: ['💡', '🔦', '🏮', '🪔', '📱', '💻', '🖥', '🖨', '⌨️', '🖱', '🎙', '📻', '📺', '📷', '📹', '📼', '🔍', '🔬', '🔭', '📡', '⏰', '⏱', '🔋', '🔌', '💎', '🔑', '🗝', '📦', '🎁', '🎈', '🎉', '🎊'],
  symbols: ['❤️', '🧡', '💛', '💚', '💙', '💜', '🖤', '🤍', '🤎', '💔', '❣️', '💕', '💞', '💓', '💗', '💖', '💘', '💝', '💟', '☮️', '✝️', '☪️', '🕉', '✡️', '☸️', '☯️', '☦️', '🛐', '⛎', '♈️', '♉️', '♊️', '♋️', '♌️', '♍️', '♎️', '♏️', '♐️', '♑️', '♒️', '♓️', '🆔', '💯', '💢', '♨️', '🚷', '🚫', '⛔️'],
};

function setupEmojiPicker() {
  el.btnEmoji.addEventListener('click', (e) => {
    e.stopPropagation();
    el.modalEmojiPicker.classList.toggle('hidden');
    renderEmojiGrid('smileys');
  });

  window.addEventListener('click', (e) => {
    if (!el.modalEmojiPicker.contains(e.target) && e.target !== el.btnEmoji) {
      el.modalEmojiPicker.classList.add('hidden');
    }
  });

  document.querySelectorAll('.emoji-tab').forEach((tab) => {
    tab.addEventListener('click', () => {
      document.querySelectorAll('.emoji-tab').forEach((t) => t.classList.remove('active'));
      tab.classList.add('active');
      renderEmojiGrid(tab.dataset.cat);
    });
  });
}

function renderEmojiGrid(category) {
  const grid = document.getElementById('emoji-grid');
  grid.innerHTML = '';
  const list = EMOJI_CATEGORIES[category] || EMOJI_CATEGORIES.smileys;
  list.forEach((emoji) => {
    const item = document.createElement('span');
    item.className = 'emoji-item';
    item.textContent = emoji;
    item.onclick = () => {
      el.msgInput.value += emoji;
      el.msgInput.focus();
    };
    grid.appendChild(item);
  });
}

// ----------------------------------------------------
// Screen PIN Lock
// ----------------------------------------------------
function setupPinLock() {
  document.querySelectorAll('.pin-btn').forEach((btn) => {
    btn.addEventListener('click', () => {
      const key = btn.dataset.key;
      if (!key) return;
      if (key === 'back') {
        state.enteredPin = state.enteredPin.slice(0, -1);
      } else if (state.enteredPin.length < 4) {
        state.enteredPin += key;
      }
      updatePinDisplay();
      if (state.enteredPin.length === 4) {
        checkEnteredPin();
      }
    });
  });

  window.addEventListener('keydown', (e) => {
    if (!state.isLocked) return;
    if (e.key >= '0' && e.key <= '9') {
      if (state.enteredPin.length < 4) {
        state.enteredPin += e.key;
        updatePinDisplay();
        if (state.enteredPin.length === 4) checkEnteredPin();
      }
    } else if (e.key === 'Backspace') {
      state.enteredPin = state.enteredPin.slice(0, -1);
      updatePinDisplay();
    }
  });
}

function updatePinDisplay() {
  el.pinDots.forEach((dot, index) => {
    if (index < state.enteredPin.length) {
      dot.classList.add('filled');
    } else {
      dot.classList.remove('filled');
    }
  });
}

async function checkEnteredPin() {
  try {
    const valid = await invoke('verify_pin', { pin: state.enteredPin });
    if (valid) {
      unlockApp();
    } else {
      invoke('play_sound_effect', { effect: 'pop', volume: 0.9 }).catch(() => {});
      document.querySelector('.lock-card').classList.add('shake');
      setTimeout(() => {
        document.querySelector('.lock-card').classList.remove('shake');
        state.enteredPin = '';
        updatePinDisplay();
      }, 500);
    }
  } catch {
    unlockApp();
  }
}

function lockApp() {
  state.isLocked = true;
  state.enteredPin = '';
  updatePinDisplay();
  el.lockScreen.classList.remove('hidden');
}

function unlockApp() {
  state.isLocked = false;
  el.lockScreen.classList.add('hidden');
}

// ----------------------------------------------------
// Toast Notification Popup
// ----------------------------------------------------
function showToastNotification(title, text, chatId) {
  const toast = document.createElement('div');
  toast.className = 'toast-card';
  toast.innerHTML = `
    <div class="toast-avatar">${title.slice(0, 2).toUpperCase()}</div>
    <div class="toast-info">
      <div class="toast-title">${escapeHtml(title)}</div>
      <div class="toast-text">${escapeHtml(text)}</div>
    </div>
    <button class="toast-close">✕</button>
  `;

  toast.onclick = () => {
    if (chatId) selectChat(chatId);
    toast.remove();
  };

  toast.querySelector('.toast-close').onclick = (e) => {
    e.stopPropagation();
    toast.remove();
  };

  el.toastContainer.appendChild(toast);
  invoke('play_sound_effect', { effect: 'received', volume: 0.7 }).catch(() => {});

  setTimeout(() => {
    if (toast.parentElement) toast.remove();
  }, 4500);
}

// ----------------------------------------------------
// Modals & Settings Management
// ----------------------------------------------------
function setupModals() {
  // Network scan modal
  const openScan = () => {
    el.modalNetwork.classList.remove('hidden');
    refreshNetworkScan();
    refreshBluetoothScan();
  };
  el.btnScanNetwork.addEventListener('click', openScan);
  el.btnRailScan.addEventListener('click', openScan);
  el.btnEmptyScan.addEventListener('click', openScan);
  el.btnCloseScanModal.addEventListener('click', () => el.modalNetwork.classList.add('hidden'));
  el.btnRefreshScan.addEventListener('click', refreshNetworkScan);
  document.getElementById('btn-refresh-bt').addEventListener('click', refreshBluetoothScan);

  // Network tabs
  document.getElementById('tab-scan-lan').addEventListener('click', () => switchNetTab('lan'));
  document.getElementById('tab-scan-bt').addEventListener('click', () => switchNetTab('bt'));
  document.getElementById('tab-scan-manual').addEventListener('click', () => switchNetTab('manual'));

  document.getElementById('btn-connect-manual').addEventListener('click', () => {
    const ip = document.getElementById('manual-ip-input').value.trim();
    const port = parseInt(document.getElementById('manual-port-input').value) || 8989;
    if (ip) connectToPeer(ip, port);
  });

  // Settings modal
  const openSettings = () => {
    if (state.userSettings) {
      document.getElementById('setting-name').value = state.userSettings.my_display_name;
      document.getElementById('setting-username').value = state.userSettings.my_username;
      document.getElementById('setting-theme').value = state.userSettings.theme_mode;
      document.getElementById('setting-sound').checked = state.userSettings.sound_enabled;
      document.getElementById('setting-protection').checked = state.userSettings.screenshot_protection;
      document.getElementById('setting-watermark').checked = state.userSettings.screen_watermark;
      document.getElementById('setting-bubble-radius').value = state.userSettings.bubble_radius || 16;
      document.getElementById('setting-wallpaper-opacity').value = Math.round((state.userSettings.wallpaper_opacity || 0.15) * 100);
      document.getElementById('setting-pin').value = state.userSettings.pin_code || '';
    }
    el.modalSettings.classList.remove('hidden');
  };

  el.btnSettings.addEventListener('click', openSettings);
  el.btnRailSettings.addEventListener('click', openSettings);
  el.btnCloseSettingsModal.addEventListener('click', () => el.modalSettings.classList.add('hidden'));

  // Accent swatches
  document.querySelectorAll('.accent-swatch').forEach((swatch) => {
    swatch.addEventListener('click', () => {
      document.querySelectorAll('.accent-swatch').forEach((s) => s.classList.remove('active'));
      swatch.classList.add('active');
      applyAccentColor(swatch.dataset.color);
    });
  });

  document.getElementById('setting-accent-color').addEventListener('input', (e) => {
    applyAccentColor(e.target.value);
  });

  document.getElementById('btn-eyedropper').addEventListener('click', async () => {
    if (window.EyeDropper) {
      try {
        const eyeDropper = new EyeDropper();
        const res = await eyeDropper.open();
        applyAccentColor(res.sRGBHex);
        return;
      } catch { }
    }
    try {
      const color = await invoke('pick_screen_color');
      applyAccentColor(color);
    } catch (err) {
      console.warn('Eyedropper error:', err);
    }
  });

  el.btnSaveSettings.addEventListener('click', async () => {
    const pinVal = document.getElementById('setting-pin').value.trim();
    const s = {
      my_display_name: document.getElementById('setting-name').value.trim(),
      my_username: document.getElementById('setting-username').value.trim(),
      avatar_path: state.userSettings ? state.userSettings.avatar_path : null,
      theme_mode: document.getElementById('setting-theme').value,
      accent_color: document.documentElement.style.getPropertyValue('--accent') || '#2EA043',
      chat_wallpaper_path: null,
      wallpaper_opacity: parseInt(document.getElementById('setting-wallpaper-opacity').value) / 100,
      sound_enabled: document.getElementById('setting-sound').checked,
      sound_volume: 80,
      screenshot_protection: document.getElementById('setting-protection').checked,
      avatar_corner_radius: 14,
      bubble_radius: parseInt(document.getElementById('setting-bubble-radius').value) || 16,
      auto_download_media: true,
      e2ee_enabled: true,
      screen_watermark: document.getElementById('setting-watermark').checked,
      pin_code: pinVal.length === 4 ? pinVal : null,
      auto_lock_minutes: 0,
      enable_acrylic: true,
      enable_animations: true,
      use_internal_browser: true,
    };
    await invoke('save_settings', { settings: s });
    state.userSettings = s;
    applyUserSettings(s);
    el.modalSettings.classList.add('hidden');
  });

  // Lightbox
  el.btnCloseLightbox.addEventListener('click', () => el.modalLightbox.classList.add('hidden'));
}

function switchNetTab(tab) {
  document.querySelectorAll('.net-tab').forEach((t) => t.classList.remove('active'));
  document.querySelectorAll('.net-pane').forEach((p) => p.classList.remove('active'));
  document.getElementById(`tab-scan-${tab}`).classList.add('active');
  document.getElementById(`pane-scan-${tab}`).classList.add('active');
}

function applyAccentColor(hex) {
  document.documentElement.style.setProperty('--accent', hex);
  document.getElementById('setting-accent-color').value = hex;
}

function applyUserSettings(s) {
  if (!s) return;
  document.body.className = `theme-${s.theme_mode}`;
  document.getElementById('sidebar-user-name').textContent = s.my_display_name;

  if (s.accent_color) applyAccentColor(s.accent_color);

  if (s.bubble_radius) {
    document.documentElement.style.setProperty('--bubble-radius', `${s.bubble_radius}px`);
  }

  if (s.screenshot_protection !== undefined) {
    invoke('set_screenshot_protection', { enabled: s.screenshot_protection }).catch(() => {});
  }

  if (s.screen_watermark) {
    el.screenWatermark.textContent = `PRIME • ${s.my_username.toUpperCase()} • ENCRYPTED`;
    el.screenWatermark.classList.remove('hidden');
  } else {
    el.screenWatermark.classList.add('hidden');
  }
}

async function refreshNetworkScan() {
  el.scanResultsList.innerHTML = '<div class="empty-tab-hint">Сканирование сети Wi-Fi...</div>';
  try {
    const peers = await invoke('scan_network');
    if (!peers || peers.length === 0) {
      el.scanResultsList.innerHTML = '<div class="empty-tab-hint">Устройств Prime не найдено в сети Wi-Fi</div>';
      return;
    }
    el.scanResultsList.innerHTML = '';
    peers.forEach((p) => {
      const card = document.createElement('div');
      card.className = 'peer-card';
      card.innerHTML = `
        <div class="peer-meta">
          <div class="peer-name">${escapeHtml(p.name)} (@${escapeHtml(p.username)}) <span class="peer-badge-lan">LAN</span></div>
          <div class="peer-addr">${escapeHtml(p.ip)}:${p.port} (${escapeHtml(p.device_type)})</div>
        </div>
        <button class="primary-btn" onclick="connectToPeer('${p.ip}', ${p.port})">Подключить</button>
      `;
      el.scanResultsList.appendChild(card);
    });
  } catch (err) {
    el.scanResultsList.innerHTML = `<div class="empty-tab-hint">Ошибка сканирования: ${err}</div>`;
  }
}

async function refreshBluetoothScan() {
  const btList = document.getElementById('bt-results-list');
  btList.innerHTML = '<div class="empty-tab-hint">Поиск сопряжённых Bluetooth устройств...</div>';
  try {
    const devices = await invoke('scan_bluetooth_devices');
    if (!devices || devices.length === 0) {
      btList.innerHTML = '<div class="empty-tab-hint">Сопряжённых Bluetooth устройств не обнаружено</div>';
      return;
    }
    btList.innerHTML = '';
    devices.forEach((d) => {
      const card = document.createElement('div');
      card.className = 'peer-card';
      card.innerHTML = `
        <div class="peer-meta">
          <div class="peer-name">🔵 ${escapeHtml(d.name)} <span class="peer-badge-bt">Bluetooth</span></div>
          <div class="peer-addr">ID: ${escapeHtml(d.opaque_id)} (MAC: ${escapeHtml(d.mac)})</div>
        </div>
        <button class="primary-btn" onclick="connectBluetoothPeer('${d.opaque_id}', '${d.name}')">Открыть чат</button>
      `;
      btList.appendChild(card);
    });
  } catch (err) {
    btList.innerHTML = `<div class="empty-tab-hint">Ошибка Bluetooth: ${err}</div>`;
  }
}

async function connectBluetoothPeer(opaqueId, name) {
  el.modalNetwork.classList.add('hidden');
  let chat = state.chats.find((c) => c.id === opaqueId);
  if (!chat) {
    chat = {
      id: opaqueId,
      display_name: name,
      username: opaqueId,
      initials: name.slice(0, 2).toUpperCase(),
      last_message: 'Bluetooth соединение',
      last_message_time: 'Сейчас',
      unread_count: 0,
      is_online: true,
      status_text: 'Bluetooth сопряжено',
      is_muted: false,
      device_type: 'bluetooth',
    };
    state.chats.unshift(chat);
  }
  renderChats();
  selectChat(opaqueId);
}

async function connectToPeer(ip, port) {
  try {
    const peerId = await invoke('connect_peer', { peerIp: ip, port: port });
    el.modalNetwork.classList.add('hidden');
    const chats = await invoke('get_chats');
    state.chats = chats;
    renderChats();
    selectChat(peerId);
  } catch (err) {
    alert(`Ошибка подключения к ${ip}:${port}: ${err}`);
  }
}

function openLightbox(src) {
  state.currentLightboxSrc = src;
  el.lightboxImg.src = `asset://${src}`;
  el.modalLightbox.classList.remove('hidden');
}

// ----------------------------------------------------
// Fallbacks & Mock Data
// ----------------------------------------------------
function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function getFallbackChats() {
  return [
    {
      id: 'prime_phone',
      display_name: 'Prime Смартфон (Android)',
      username: 'android_phone',
      initials: 'АН',
      last_message: 'Готов к передаче P2P',
      last_message_time: '12:00',
      unread_count: 0,
      is_online: true,
      status_text: 'в сети',
      is_muted: false,
      device_type: 'android',
    },
  ];
}

function getFallbackMessages(chatId) {
  return [
    {
      id: '1',
      chat_id: chatId,
      sender: 'Собеседник',
      text: 'Добро пожаловать в нативный клиент Prime Desktop на Rust! Все функции C# перенесены: Bluetooth Classic, аудио-войсы, кружочки, фоторедактор, встроенный браузер и PIN-код.',
      timestamp: Date.now() - 60000,
      formatted_time: '12:00',
      is_from_me: false,
      packet_type: 'Text',
      reactions: [{ emoji: '🔥', count: 1, is_selected_by_me: true, users: ['Me'] }],
      is_first_in_chat: true,
      is_last_in_chat: true,
    },
  ];
}

window.addEventListener('DOMContentLoaded', init);
