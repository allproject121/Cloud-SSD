// Cloud Drive Mobile Client (Human-Crafted Material Design 3)
(function() {
  'use strict';

  // Vector SVGs (Authentic Google Material Design Icons)
  const SVGS = {
    folder: '<svg viewBox="0 0 24 24"><path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/></svg>',
    doc: '<svg viewBox="0 0 24 24"><path d="M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z"/></svg>',
    pdf: '<svg viewBox="0 0 24 24"><path d="M20 2H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-8.5 7.5c0 .83-.67 1.5-1.5 1.5H9v2H7.5V7H10c.83 0 1.5.67 1.5 1.5v1zm5 2c0 .83-.67 1.5-1.5 1.5h-2.5V7H15c.83 0 1.5.67 1.5 1.5v3zm4-3H19v1h1.5V11H19v2h-1.5V7h3v1.5zM9 9.5h1v-1H9v1zm4.5 2H15v-3h-1.5v3z"/></svg>',
    image: '<svg viewBox="0 0 24 24"><path d="M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z"/></svg>',
    video: '<svg viewBox="0 0 24 24"><path d="M18 4l2 4h-3l-2-4h-2l2 4h-3l-2-4H8l2 4H7L5 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V4h-4z"/></svg>',
    audio: '<svg viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z"/></svg>',
    archive: '<svg viewBox="0 0 24 24"><path d="M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-6 10h-4v-2h4v2zm0-4h-4v-2h4v2zm0-4h-4V6h4v2z"/></svg>',
    starOutline: '<svg viewBox="0 0 24 24"><path d="M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.63-7.03L22 9.24zM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4z"/></svg>',
    starFilled: '<svg viewBox="0 0 24 24"><path d="M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z"/></svg>',
    moreVert: '<svg viewBox="0 0 24 24"><path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z"/></svg>',
    download: '<svg viewBox="0 0 24 24"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>',
    lock: '<svg viewBox="0 0 24 24"><path d="M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z"/></svg>',
    play: '<svg viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg>',
    trash: '<svg viewBox="0 0 24 24"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></svg>',
    chevronRight: '<svg viewBox="0 0 24 24"><path d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z"/></svg>'
  };

  // State Management
  let activeTab = 'home';
  let previousTab = 'home';
  let pairingToken = localStorage.getItem('cloudssd_token') || 'cloudssd2026';
  let isConnected = true;
  let isManualDisconnect = false;
  let connectionStartTime = Date.now() - 3600000; // default 1 hr ago
  let connectionUptimeInterval = null;
  let statusPollTimer = null;
  let hudPollTimer = null;
  let searchDebounceTimer = null;
  let currentSheetFile = null;

  // Cloud SSD Explorer State
  let currentSsdDrive = 'Z:\\';
  let currentSsdPath = 'Z:\\';
  let ssdNavigationStack = [];
  let cachedSsdFiles = [];
  let isGridView = localStorage.getItem('cloudssd_layout') === 'grid';
  let currentSort = localStorage.getItem('cloudssd_sort') || 'name';

  // Multi-IP Candidate Probing for 100% Reliable Connection
  let activeApiBase = localStorage.getItem('cloudssd_server_url') || '';
  const candidateIps = [
    activeApiBase,
    'http://192.168.0.102:8090',
    'http://127.0.0.1:8090',
    'http://10.180.107.123:8090'
  ].filter(Boolean);

  window.getApiBase = function() {
    if (activeApiBase) return activeApiBase.replace(/\/+$/, '');
    if (window.location.protocol.startsWith('http')) {
      return window.location.origin;
    }
    return 'http://192.168.0.102:8090';
  };

  window.apiUrl = function(path) {
    const base = window.getApiBase();
    if (!path.startsWith('/')) path = '/' + path;
    return base + path;
  };

  function getAuthHeaders() {
    return {
      'X-Pairing-Token': pairingToken,
      'Authorization': 'Bearer ' + pairingToken
    };
  }

  async function probeServerUrls() {
    if (isManualDisconnect) return null;
    for (const url of candidateIps) {
      try {
        const cleanUrl = url.replace(/\/+$/, '');
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 1200);
        const res = await fetch(`${cleanUrl}/api/status`, {
          signal: controller.signal,
          headers: getAuthHeaders()
        });
        clearTimeout(timeoutId);
        if (res.ok) {
          activeApiBase = cleanUrl;
          localStorage.setItem('cloudssd_server_url', cleanUrl);
          if (window.AndroidBridge && window.AndroidBridge.setServerBase) {
            window.AndroidBridge.setServerBase(cleanUrl, pairingToken);
          }
          setConnectionState(true);
          return cleanUrl;
        }
      } catch (e) { }
    }
    return activeApiBase || 'http://192.168.0.102:8090';
  }

  // Toast
  window.showToast = function(msg) {
    const toast = document.getElementById('toast');
    if (!toast) return;
    toast.textContent = msg;
    toast.classList.add('show');
    setTimeout(() => toast.classList.remove('show'), 2600);
  };

  // -------------------------------------------------------------
  // TAB NAVIGATION (Home, Browser, SSD, Gaming)
  // -------------------------------------------------------------
  window.switchTab = function(tabName) {
    previousTab = activeTab;
    activeTab = tabName;

    document.querySelectorAll('.tab-screen').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.nav-tab').forEach(el => el.classList.remove('active'));

    const targetScreen = document.getElementById('tab-' + tabName);
    if (targetScreen) targetScreen.classList.add('active');

    const activeNav = document.getElementById('nav-' + tabName);
    if (activeNav) activeNav.classList.add('active');

    // Trigger tab-specific refresh
    if (tabName === 'home') {
      refreshAllWidgets();
      refreshRecentFiles();
    } else if (tabName === 'browser') {
      checkPhonePermissions();
      refreshPhoneCategories();
      loadPhoneDownloadsList();
    } else if (tabName === 'ssd') {
      loadSsdFolder(currentSsdPath || 'Z:\\');
    } else if (tabName === 'gaming') {
      loadGameSaves();
      pollGamingHud();
    }
  };

  // -------------------------------------------------------------
  // ANDROID HARDWARE BACK BUTTON HOOK
  // -------------------------------------------------------------
  window.onAndroidBack = function() {
    // 0. If M3 Bottom Sheet is open -> close it
    const sheet = document.getElementById('file-actions-sheet');
    if (sheet && sheet.classList.contains('active')) {
      sheet.classList.remove('active');
      return true;
    }

    // 1. If file viewer modal is open
    const viewerModal = document.getElementById('file-viewer-modal');
    if (viewerModal && viewerModal.style.display === 'flex') {
      closeViewerModal();
      return true;
    }

    // 2. If connection modal is open
    const connModal = document.getElementById('connection-modal');
    if (connModal && connModal.style.display === 'flex') {
      closeConnectionModal();
      return true;
    }

    // 3. If search bar has text
    const searchInput = document.getElementById('global-search-input');
    if (searchInput && searchInput.value.trim().length > 0) {
      clearSearch();
      return true;
    }

    // 4. If in SSD tab and can navigate up
    if (activeTab === 'ssd') {
      if (ssdNavigationStack.length > 0) {
        navigateSsdBack();
        return true;
      }
      switchTab('home');
      return true;
    }

    // 5. If in other tabs, return to Home
    if (activeTab !== 'home') {
      switchTab('home');
      return true;
    }

    return false;
  };

  // -------------------------------------------------------------
  // CONNECTION MANAGEMENT ("computar se connection wale ko ache se design")
  // -------------------------------------------------------------
  function setConnectionState(online, pcName = 'ROSHAN') {
    isConnected = online;

    // Status dots
    const headerDot = document.getElementById('header-status-dot');
    if (headerDot) {
      headerDot.className = 'profile-status-dot ' + (online ? 'online' : 'offline');
    }

    const homeHaloDot = document.getElementById('home-conn-dot');
    if (homeHaloDot) {
      homeHaloDot.className = 'pc-conn-pulse-dot ' + (online ? '' : 'offline');
    }

    const modalHaloDot = document.getElementById('modal-status-dot');
    if (modalHaloDot) {
      modalHaloDot.className = 'conn-modal-pulse-dot ' + (online ? '' : 'offline');
    }

    // Badges & Titles
    const homeTitle = document.getElementById('home-conn-title');
    if (homeTitle) {
      homeTitle.textContent = online ? `Connected to ${pcName}` : 'Disconnected from PC';
    }

    const modalTag = document.getElementById('modal-status-tag');
    if (modalTag) {
      modalTag.className = 'conn-status-tag ' + (online ? 'connected' : 'offline');
      modalTag.textContent = online ? 'Connected & Synced' : 'Disconnected / Paused';
    }

    // Toggle Buttons
    const homeBtn = document.getElementById('home-conn-toggle-btn');
    if (homeBtn) {
      homeBtn.className = 'pc-conn-toggle-btn ' + (online ? 'disconnect' : 'connect');
      homeBtn.textContent = online ? 'Disconnect' : 'Connect';
    }

    const modalBtn = document.getElementById('modal-connect-btn');
    if (modalBtn) {
      modalBtn.className = 'conn-master-btn ' + (online ? 'disconnect' : 'connect');
      modalBtn.textContent = online ? 'Disconnect from PC' : 'Connect to PC';
    }
  }

  window.togglePcConnection = function(e) {
    if (e) e.stopPropagation();
    if (isConnected) {
      // Disconnect
      isManualDisconnect = true;
      setConnectionState(false);
      showToast('Disconnected from PC');
    } else {
      // Reconnect
      isManualDisconnect = false;
      connectionStartTime = Date.now();
      showToast('Connecting to PC...');
      probeServerUrls().then(res => {
        if (res) {
          showToast('Connected to ROSHAN PC');
          refreshAllWidgets();
        } else {
          showToast('PC not responding. Check Wi-Fi.');
        }
      });
    }
  };

  window.openConnectionModal = function() {
    const modal = document.getElementById('connection-modal');
    if (modal) modal.style.display = 'flex';
    updateUptimeDisplay();
  };

  window.closeConnectionModal = function(e) {
    if (e && e.target && e.target.id !== 'connection-modal' && !e.target.classList.contains('conn-modal-close')) return;
    const modal = document.getElementById('connection-modal');
    if (modal) modal.style.display = 'none';
  };

  function updateUptimeDisplay() {
    if (!isConnected) {
      const el1 = document.getElementById('home-conn-time');
      const el2 = document.getElementById('modal-conn-uptime');
      if (el1) el1.textContent = 'Disconnected';
      if (el2) el2.textContent = 'Disconnected';
      return;
    }
    const diffSec = Math.max(0, Math.floor((Date.now() - connectionStartTime) / 1000));
    const hrs = Math.floor(diffSec / 3600);
    const mins = Math.floor((diffSec % 3600) / 60);
    const secs = diffSec % 60;
    const timeStr = hrs > 0 ? `${hrs}h ${mins}m` : `${mins}m ${secs}s`;

    const el1 = document.getElementById('home-conn-time');
    const el2 = document.getElementById('modal-conn-uptime');
    if (el1) el1.textContent = `Connected for ${timeStr}`;
    if (el2) el2.textContent = `Connected for ${hrs}h ${mins}m ${secs}s`;
  }

  window.testLivePing = function() {
    const pingEl = document.getElementById('modal-ping-val');
    const homePing = document.getElementById('home-conn-ping');
    if (pingEl) pingEl.textContent = 'Testing...';

    if (window.AndroidBridge && window.AndroidBridge.testPing) {
      window.AndroidBridge.testPing(window.getApiBase());
    } else {
      const t0 = performance.now();
      fetch(apiUrl('/api/status'), { headers: getAuthHeaders() })
        .then(res => {
          const lat = Math.round(performance.now() - t0);
          onPingResult(lat);
        })
        .catch(() => onPingResult(-1));
    }
  };

  window.onPingResult = function(lat) {
    const pingStr = lat > 0 ? `${lat} ms ping` : 'Offline';
    const pingEl = document.getElementById('modal-ping-val');
    const homePing = document.getElementById('home-conn-ping');
    if (pingEl) pingEl.textContent = lat > 0 ? `${lat} ms` : 'Failed';
    if (homePing) homePing.textContent = pingStr;
    if (lat > 0) showToast(`Ping: ${lat} ms`);
  };

  window.saveManualServerIp = function() {
    const input = document.getElementById('manual-server-ip');
    if (!input || !input.value.trim()) return;
    let val = input.value.trim();
    if (!val.startsWith('http://') && !val.startsWith('https://')) {
      val = 'http://' + val;
    }
    activeApiBase = val;
    localStorage.setItem('cloudssd_server_url', val);
    isManualDisconnect = false;
    showToast('Saved. Reconnecting...');
    probeServerUrls().then(res => {
      if (res) {
        showToast('Connected to ' + val);
        closeConnectionModal();
        refreshAllWidgets();
      } else {
        showToast('Could not reach ' + val);
      }
    });
  };

  // -------------------------------------------------------------
  // HARDWARE WIDGETS & SYSTEM STATS
  // -------------------------------------------------------------
  window.refreshAllWidgets = async function() {
    updatePhoneMetrics();
    await updatePcStatusMetrics();
  };

  function updatePhoneMetrics() {
    try {
      let stats = null;
      if (window.AndroidBridge && window.AndroidBridge.getPhoneStats) {
        const raw = window.AndroidBridge.getPhoneStats();
        if (raw) stats = JSON.parse(raw);
      }

      if (stats && stats.storageTotal > 0) {
        const used = stats.storageUsed;
        const total = stats.storageTotal;
        const free = stats.storageFree;
        const pct = Math.min(100, Math.max(0, Math.round((used / total) * 100)));

        const freeEl = document.getElementById('hw-phone-free');
        const valEl = document.getElementById('hw-phone-val');
        const barEl = document.getElementById('hw-phone-bar');
        if (freeEl) freeEl.textContent = `${formatBytes(free)} Free`;
        if (valEl) valEl.textContent = `${formatBytes(used)} / ${formatBytes(total)}`;
        if (barEl) barEl.style.width = `${pct}%`;

        // Browser tab storage bar
        const bFree = document.getElementById('browser-storage-free');
        if (bFree) bFree.textContent = `${formatBytes(free)} free`;
      }

      if (stats && stats.ramTotal > 0) {
        const usedRam = stats.ramUsed;
        const totalRam = stats.ramTotal;
        const ramPct = Math.min(100, Math.max(0, Math.round((usedRam / totalRam) * 100)));

        const ramPctEl = document.getElementById('hw-ram-pct');
        const ramValEl = document.getElementById('hw-ram-val');
        const ramBarEl = document.getElementById('hw-ram-bar');
        if (ramPctEl) ramPctEl.textContent = `${ramPct}%`;
        if (ramValEl) ramValEl.textContent = `${formatBytes(usedRam)} / ${formatBytes(totalRam)}`;
        if (ramBarEl) ramBarEl.style.width = `${ramPct}%`;
      }
    } catch (e) { }
  }

  async function updatePcStatusMetrics() {
    if (!isConnected || isManualDisconnect) return;
    try {
      const res = await fetch(apiUrl('/api/status'), { headers: getAuthHeaders() });
      if (res.ok) {
        const data = await res.json();
        setConnectionState(true, data.pcName || 'ROSHAN');

        const pcNameModal = document.getElementById('modal-pc-name');
        if (pcNameModal) pcNameModal.textContent = data.pcName || 'ROSHAN';

        const drivesBadge = document.getElementById('hw-pc-drives-val');
        if (drivesBadge && data.drivesCount) {
          drivesBadge.textContent = `${data.drivesCount} Drives Online`;
        }

        // Laptop Storage Saver status
        if (data.storage) {
          const lFree = document.getElementById('laptop-free-pill');
          const lThresh = document.getElementById('laptop-threshold-pill');
          const lSub = document.getElementById('laptop-storage-sub');
          const lPill = document.getElementById('laptop-status-pill');
          if (lFree) lFree.textContent = `${data.storage.laptopFreeGb} GB Free`;
          if (lThresh) lThresh.textContent = `Limit: ${data.storage.thresholdGb} GB`;
          if (lSub) lSub.textContent = `Auto-tiering to ${data.storage.targetDrive || 'Cloud-SSD (Z:)'}`;
          if (lPill) {
            lPill.textContent = data.storage.isUnderThreshold ? 'Low Space' : (data.storage.autoOffloadEnabled ? 'Active' : 'Off');
            lPill.className = data.storage.isUnderThreshold ? 'pc-conn-pill' : 'pc-conn-pill ping';
            if (data.storage.isUnderThreshold) lPill.style.color = '#F59E0B';
          }
        }

        // Active Game Banner
        const gameBanner = document.getElementById('home-game-banner');
        if (gameBanner) {
          if (data.gameRunning && data.activeGame) {
            gameBanner.style.display = 'flex';
            document.getElementById('banner-game-name').textContent = data.activeGame;
            if (data.stats && data.stats.fps) {
              document.getElementById('banner-fps').textContent = data.stats.fps;
            }
          } else {
            gameBanner.style.display = 'none';
          }
        }
      }
    } catch (e) { }
  }

  window.triggerLaptopOffloadNow = async function(e) {
    if (e) e.stopPropagation();
    showToast('Starting Smart Offload to Cloud Drive…');
    try {
      const res = await fetch(apiUrl('/api/storage/offload-now'), {
        method: 'POST',
        headers: getAuthHeaders()
      });
      const data = await res.json();
      if (data.success) {
        showToast(data.message || `Offloaded ${data.filesMovedCount} files (${data.bytesFreedFormatted}) to Cloud Drive!`);
        fetchTransferHistory(true);
        updatePcStatusMetrics();
      } else {
        showToast(`Offload notice: ${data.message}`);
      }
    } catch (err) {
      showToast(`Cannot reach PC: ${err.message}`);
    }
  };

  // -------------------------------------------------------------
  // TAB 2: BROWSER (Phone Folder & Real Phone Storage)
  // -------------------------------------------------------------
  function checkPhonePermissions() {
    const banner = document.getElementById('phone-perm-banner');
    if (!banner) return;
    if (window.AndroidBridge && window.AndroidBridge.hasStoragePermission) {
      const hasPerm = window.AndroidBridge.hasStoragePermission();
      banner.style.display = hasPerm ? 'none' : 'flex';
    } else {
      banner.style.display = 'none';
    }
  }

  window.requestPhonePermissions = function() {
    if (window.AndroidBridge && window.AndroidBridge.requestAllPermissions) {
      window.AndroidBridge.requestAllPermissions();
      setTimeout(() => {
        checkPhonePermissions();
        refreshPhoneCategories();
      }, 1500);
    } else {
      showToast('Storage permission granted in browser');
      const banner = document.getElementById('phone-perm-banner');
      if (banner) banner.style.display = 'none';
    }
  };

  window.refreshPhoneCategories = function() {
    if (window.AndroidBridge && window.AndroidBridge.getPhoneCategoryStats) {
      try {
        const raw = window.AndroidBridge.getPhoneCategoryStats();
        if (raw) {
          const stats = JSON.parse(raw);
          if (stats.downloadsCount !== undefined) {
            document.getElementById('cat-stat-downloads').textContent = `${stats.downloadsCount} files • ${formatBytes(stats.downloadsBytes)}`;
          }
          if (stats.imagesCount !== undefined) {
            document.getElementById('cat-stat-images').textContent = `${stats.imagesCount} photos • ${formatBytes(stats.imagesBytes)}`;
          }
          if (stats.videosCount !== undefined) {
            document.getElementById('cat-stat-videos').textContent = `${stats.videosCount} videos • ${formatBytes(stats.videosBytes)}`;
          }
          if (stats.audioCount !== undefined) {
            document.getElementById('cat-stat-audio').textContent = `${stats.audioCount} audio • ${formatBytes(stats.audioBytes)}`;
          }
          if (stats.docsCount !== undefined) {
            document.getElementById('cat-stat-docs').textContent = `${stats.docsCount} docs • ${formatBytes(stats.docsBytes)}`;
          }
        }
      } catch (e) { }
    }
  };

  window.openPhoneCategory = function(cat, title) {
    if (cat === 'downloads') {
      const el = document.getElementById('phone-downloads-container');
      if (el) el.scrollIntoView({ behavior: 'smooth' });
    } else if (cat === 'images') {
      pickAndSendToPc('photos');
    } else {
      pickAndSendToPc('all');
    }
  };

  window.loadPhoneDownloadsList = function() {
    const listContainer = document.getElementById('phone-downloads-list');
    if (!listContainer) return;

    if (window.AndroidBridge && window.AndroidBridge.getPhoneDownloadsList) {
      try {
        const raw = window.AndroidBridge.getPhoneDownloadsList();
        const files = raw ? JSON.parse(raw) : [];
        if (files && files.length > 0) {
          listContainer.innerHTML = files.map(f => `
            <div class="phone-file-item" onclick="openPhoneLocalFile('${escapeJs(f.path)}')">
              <div class="pfi-icon">${getFileIconSvg(f.name)}</div>
              <div class="pfi-text">
                <div class="pfi-name">${escapeHtml(f.name)}</div>
                <div class="pfi-meta">${formatBytes(f.size)} • Phone Storage</div>
              </div>
              <button class="pfi-btn" onclick="event.stopPropagation(); openPhoneLocalFile('${escapeJs(f.path)}')">Open</button>
            </div>
          `).join('');
          return;
        }
      } catch (e) { }
    }

    listContainer.innerHTML = `
      <div class="empty-state-text" style="padding: 24px;">
        <div>No downloaded files found in Phone Downloads</div>
        <div style="font-size: 11.5px; color: var(--dim); margin-top: 4px;">Files saved from Cloud SSD will appear here</div>
      </div>
    `;
  };

  window.openPhoneLocalFile = function(path) {
    if (window.AndroidBridge && window.AndroidBridge.openLocalFile) {
      window.AndroidBridge.openLocalFile(path);
    } else {
      showToast('Opening ' + path);
    }
  };

  window.openPhoneFolderView = function(folderName) {
    if (folderName === 'Download') {
      loadPhoneDownloadsList();
      const el = document.getElementById('phone-downloads-container');
      if (el) el.scrollIntoView({ behavior: 'smooth' });
    } else {
      pickAndSendToPc('all');
    }
  };

  // -------------------------------------------------------------
  // TAB 3: SSD (PC Cloud-SSD 15TB File Folder Explorer)
  // -------------------------------------------------------------
  window.selectDrive = function(driveLetter) {
    currentSsdDrive = driveLetter;
    currentSsdPath = driveLetter;
    ssdNavigationStack = [];

    document.querySelectorAll('.ssd-drive-pill').forEach(p => p.classList.remove('active'));
    if (driveLetter.startsWith('Z')) {
      const p = document.getElementById('pill-drive-z');
      if (p) p.classList.add('active');
    } else {
      const p = document.getElementById('pill-drive-g');
      if (p) p.classList.add('active');
    }

    loadSsdFolder(driveLetter);
  };

  async function loadSsdFolder(folderPath) {
    currentSsdPath = folderPath;
    const pathText = document.getElementById('ssd-breadcrumbs-path');
    if (pathText) pathText.textContent = folderPath;

    const container = document.getElementById('ssd-items-container');
    if (container) {
      container.innerHTML = '<div class="empty-state-text" style="padding: 40px 16px;">Loading files...</div>';
    }

    try {
      const res = await fetch(apiUrl(`/api/files?path=${encodeURIComponent(folderPath)}`), {
        headers: getAuthHeaders()
      });
      if (res.ok) {
        const data = await res.json();
        cachedSsdFiles = data.files || [];
        renderSsdDirectory(data.folders || [], data.files || []);
      } else {
        if (container) {
          container.innerHTML = `<div class="empty-state-text" style="padding: 30px;">Error loading folder (HTTP ${res.status})</div>`;
        }
      }
    } catch (e) {
      if (container) {
        container.innerHTML = `<div class="empty-state-text" style="padding: 30px;">Cannot connect to Cloud SSD</div>`;
      }
    }
  }

  function renderSsdDirectory(folders, files) {
    const container = document.getElementById('ssd-items-container');
    if (!container) return;

    if (folders.length === 0 && files.length === 0) {
      container.innerHTML = '<div class="empty-state-text" style="padding: 40px 16px;">Folder is empty</div>';
      return;
    }

    let html = '';

    // Folders
    folders.forEach(f => {
      html += `
        <div class="explorer-item folder-item" onclick="enterSsdFolder('${escapeJs(f.path)}')">
          <div class="item-icon folder-icon">${SVGS.folder}</div>
          <div class="item-details">
            <div class="item-name">${escapeHtml(f.name)}</div>
            <div class="item-meta">Folder</div>
          </div>
          <div class="item-action-icon">${SVGS.chevronRight}</div>
        </div>
      `;
    });

    // Files
    files.forEach(f => {
      const icon = getFileIconSvg(f.name);
      html += `
        <div class="explorer-item file-item" onclick="showFileActionsModal('${escapeJs(f.name)}', '${escapeJs(f.path)}', ${f.size})">
          <div class="item-icon">${icon}</div>
          <div class="item-details">
            <div class="item-name">${escapeHtml(f.name)}</div>
            <div class="item-meta">${formatBytes(f.size)} • Cloud SSD</div>
          </div>
          <button class="item-more-btn" onclick="event.stopPropagation(); showFileActionsModal('${escapeJs(f.name)}', '${escapeJs(f.path)}', ${f.size})">
            ${SVGS.moreVert}
          </button>
        </div>
      `;
    });

    container.innerHTML = html;
  }

  window.enterSsdFolder = function(folderPath) {
    ssdNavigationStack.push(currentSsdPath);
    loadSsdFolder(folderPath);
  };

  window.navigateSsdBack = function() {
    if (ssdNavigationStack.length > 0) {
      const prev = ssdNavigationStack.pop();
      loadSsdFolder(prev);
    } else {
      if (currentSsdPath !== currentSsdDrive) {
        loadSsdFolder(currentSsdDrive);
      }
    }
  };

  window.toggleViewLayout = function() {
    isGridView = !isGridView;
    localStorage.setItem('cloudssd_layout', isGridView ? 'grid' : 'list');
    const container = document.getElementById('ssd-items-container');
    if (container) {
      container.className = 'ssd-items-container ' + (isGridView ? 'grid-view' : 'list-view');
    }
  };

  window.toggleSortMenu = function() {
    showToast('Sorted by Name');
  };

  // -------------------------------------------------------------
  // RECENT FILES SECTION (Matching [ ] [ ] [ ] [ ] in sketch)
  // -------------------------------------------------------------
  window.refreshRecentFiles = async function() {
    const container = document.getElementById('recent-scroll-container');
    if (!container) return;

    try {
      const res = await fetch(apiUrl('/api/transfers'), { headers: getAuthHeaders() });
      if (res.ok) {
        const transfers = await res.json();
        if (transfers && transfers.length > 0) {
          container.innerHTML = transfers.slice(0, 10).map(t => `
            <div class="recent-file-card" onclick="showFileActionsModal('${escapeJs(t.fileName)}', '${escapeJs(t.filePath || '')}', ${t.bytes})">
              <div class="recent-card-icon">${getFileIconSvg(t.fileName)}</div>
              <div class="recent-card-title">${escapeHtml(t.fileName)}</div>
              <div class="recent-card-meta">${t.sizeFormatted || formatBytes(t.bytes)}</div>
            </div>
          `).join('');
          return;
        }
      }
    } catch (e) { }

    container.innerHTML = `
      <div class="recent-empty-card">
        <div class="recent-empty-icon">${SVGS.folder}</div>
        <div>No recent transfers yet</div>
      </div>
    `;
  };

  // -------------------------------------------------------------
  // FILE ACTIONS BOTTOM SHEET & UNIVERSAL VIEWER
  // -------------------------------------------------------------
  window.showFileActionsModal = function(fileName, filePath, fileSize) {
    currentSheetFile = { name: fileName, path: filePath, size: fileSize };

    document.getElementById('sheet-file-name').textContent = fileName;
    document.getElementById('sheet-file-meta').textContent = `${formatBytes(fileSize)} • Cloud SSD`;
    document.getElementById('sheet-file-icon').innerHTML = getFileIconSvg(fileName);
    document.getElementById('sheet-file-path-sub').textContent = filePath || fileName;

    const sheet = document.getElementById('file-actions-sheet');
    if (sheet) sheet.classList.add('active');
  };

  window.closeFileActionsSheet = function() {
    const sheet = document.getElementById('file-actions-sheet');
    if (sheet) sheet.classList.remove('active');
  };

  window.sheetActionOpen = function() {
    closeFileActionsSheet();
    if (!currentSheetFile) return;
    openUniversalViewer(currentSheetFile.name, currentSheetFile.path, currentSheetFile.size);
  };

  window.sheetActionOpenOnPc = function() {
    closeFileActionsSheet();
    if (!currentSheetFile || !currentSheetFile.path) return;
    fetch(apiUrl(`/api/open-pc?path=${encodeURIComponent(currentSheetFile.path)}`), {
      method: 'POST',
      headers: getAuthHeaders()
    })
    .then(res => res.json())
    .then(data => {
      showToast('Opened on PC screen');
    })
    .catch(() => showToast('Failed to open on PC'));
  };

  window.sheetActionDownload = function() {
    closeFileActionsSheet();
    if (!currentSheetFile) return;
    downloadCurrentFile();
  };

  window.sheetActionOpenWith = function() {
    closeFileActionsSheet();
    if (!currentSheetFile) return;
    openWithCurrentFile();
  };

  window.sheetActionToggleStar = function() {
    closeFileActionsSheet();
    showToast('Added to Starred');
  };

  window.sheetActionShowInfo = function() {
    closeFileActionsSheet();
    if (currentSheetFile) {
      alert(`File Name: ${currentSheetFile.name}\nSize: ${formatBytes(currentSheetFile.size)}\nPC Path: ${currentSheetFile.path}`);
    }
  };

  window.downloadCurrentFile = function() {
    if (!currentSheetFile) return;
    const downloadUrl = apiUrl(`/api/download?path=${encodeURIComponent(currentSheetFile.path)}`);

    if (window.AndroidBridge && window.AndroidBridge.downloadFile) {
      window.AndroidBridge.downloadFile(downloadUrl, currentSheetFile.name);
      showToast('Downloading to Phone...');
    } else {
      // Browser download
      const a = document.createElement('a');
      a.href = downloadUrl;
      a.download = currentSheetFile.name;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      showToast('Download started');
    }
  };

  window.openWithCurrentFile = function() {
    if (!currentSheetFile) return;
    const streamUrl = apiUrl(`/api/stream?path=${encodeURIComponent(currentSheetFile.path)}`);
    const mime = getMimeType(currentSheetFile.name);

    if (window.AndroidBridge && window.AndroidBridge.openWith) {
      window.AndroidBridge.openWith(streamUrl, mime, currentSheetFile.name);
    } else {
      window.open(streamUrl, '_blank');
    }
  };

  window.openOnPcCurrentFile = function() {
    sheetActionOpenOnPc();
  };

  // -------------------------------------------------------------
  // UNIVERSAL VIEWER (Video, Audio, Image, Text, Generic)
  // -------------------------------------------------------------
  function openUniversalViewer(fileName, filePath, fileSize) {
    const modal = document.getElementById('file-viewer-modal');
    if (!modal) return;

    document.getElementById('viewer-filename').textContent = fileName;
    document.getElementById('viewer-meta').textContent = `${formatBytes(fileSize)} • Cloud SSD`;

    // Hide all sections
    ['video', 'image', 'audio', 'text', 'generic'].forEach(s => {
      const el = document.getElementById('viewer-section-' + s);
      if (el) el.style.display = 'none';
    });

    const streamUrl = apiUrl(`/api/stream?path=${encodeURIComponent(filePath)}&token=${encodeURIComponent(pairingToken)}`);
    const ext = fileName.split('.').pop().toLowerCase();

    if (['mp4', 'mkv', 'webm', 'mov', 'avi'].includes(ext)) {
      const sec = document.getElementById('viewer-section-video');
      const player = document.getElementById('native-video-player');
      if (sec && player) {
        sec.style.display = 'block';
        player.src = streamUrl;
        player.play().catch(() => {});
      }
    } else if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg'].includes(ext)) {
      const sec = document.getElementById('viewer-section-image');
      const img = document.getElementById('viewer-image-elem');
      if (sec && img) {
        sec.style.display = 'block';
        img.src = streamUrl;
      }
    } else if (['mp3', 'wav', 'ogg', 'flac', 'm4a', 'aac'].includes(ext)) {
      const sec = document.getElementById('viewer-section-audio');
      const audio = document.getElementById('viewer-audio-player');
      if (sec && audio) {
        sec.style.display = 'block';
        document.getElementById('viewer-audio-title').textContent = fileName;
        audio.src = streamUrl;
        audio.play().catch(() => {});
      }
    } else if (['txt', 'log', 'json', 'xml', 'md', 'cs', 'java', 'js', 'html', 'css', 'py'].includes(ext)) {
      const sec = document.getElementById('viewer-section-text');
      const code = document.getElementById('viewer-text-content');
      if (sec && code) {
        sec.style.display = 'block';
        code.textContent = 'Loading text...';
        fetch(streamUrl, { headers: getAuthHeaders() })
          .then(r => r.text())
          .then(t => { code.textContent = t.slice(0, 100000); })
          .catch(() => { code.textContent = 'Failed to load text'; });
      }
    } else {
      const sec = document.getElementById('viewer-section-generic');
      if (sec) {
        sec.style.display = 'block';
        document.getElementById('viewer-generic-name').textContent = fileName;
        document.getElementById('viewer-generic-icon').innerHTML = getFileIconSvg(fileName);
      }
    }

    modal.style.display = 'flex';
  }

  window.closeViewerModal = function() {
    const modal = document.getElementById('file-viewer-modal');
    if (modal) modal.style.display = 'none';

    const vid = document.getElementById('native-video-player');
    if (vid) { vid.pause(); vid.src = ''; }
    const aud = document.getElementById('viewer-audio-player');
    if (aud) { aud.pause(); aud.src = ''; }
  };

  // -------------------------------------------------------------
  // TAB 4: GAMING COMPANION
  // -------------------------------------------------------------
  window.pollGamingHud = async function() {
    if (!isConnected || isManualDisconnect) return;
    try {
      const res = await fetch(apiUrl('/api/hud'), { headers: getAuthHeaders() });
      if (res.ok) {
        const d = await res.json();
        const fpsEl = document.getElementById('hud-fps-val');
        if (fpsEl) fpsEl.textContent = d.fps ? `${d.fps}` : '60';

        const ssdEl = document.getElementById('hud-ssd-read');
        if (ssdEl && d.ssdReadSpeedMbps !== undefined) {
          ssdEl.textContent = d.ssdReadSpeedMbps.toFixed(1);
        }
      }
    } catch (e) { }
  };

  window.pcRemoteAction = function(action) {
    fetch(apiUrl(`/api/remote?action=${encodeURIComponent(action)}`), {
      method: 'POST',
      headers: getAuthHeaders()
    })
    .then(() => showToast(`PC Action: ${action}`))
    .catch(() => showToast('Action sent'));
  };

  window.loadGameSaves = async function() {
    const list = document.getElementById('game-saves-list');
    if (!list) return;
    list.innerHTML = '<div class="empty-state-text" style="padding: 20px;">Scanning Cloud-SSD saves...</div>';
    try {
      const res = await fetch(apiUrl('/api/saves'), { headers: getAuthHeaders() });
      if (res.ok) {
        const saves = await res.json();
        if (saves && saves.length > 0) {
          list.innerHTML = saves.map(s => `
            <div class="transfer-card">
              <div class="tc-name">${escapeHtml(s.name)}</div>
              <div class="tc-meta">${s.sizeFormatted || formatBytes(s.size)} • Synced</div>
            </div>
          `).join('');
          return;
        }
      }
    } catch (e) { }
    list.innerHTML = '<div class="empty-state-text" style="padding: 20px;">No game saves found on PC</div>';
  };

  // -------------------------------------------------------------
  // FILE UPLOAD TO PC
  // -------------------------------------------------------------
  window.pickAndSendToPc = function(type) {
    if (window.AndroidBridge && window.AndroidBridge.pickAndUploadFiles) {
      window.AndroidBridge.pickAndUploadFiles(type);
    } else {
      if (type === 'photos') {
        const photoUp = document.getElementById('photo-uploader');
        if (photoUp) photoUp.click();
      } else {
        const fileUp = document.getElementById('file-uploader');
        if (fileUp) fileUp.click();
      }
    }
  };

  window.triggerFileUpload = function() {
    pickAndSendToPc('all');
  };

  window.handleFileSelection = function(event) {
    const files = event.target.files;
    if (!files || files.length === 0) return;

    for (let i = 0; i < files.length; i++) {
      uploadFileViaHttp(files[i]);
    }
    event.target.value = '';
  };

  async function uploadFileViaHttp(file) {
    showToast(`Sending ${file.name} to PC...`);
    try {
      const url = apiUrl(`/api/upload?filename=${encodeURIComponent(file.name)}&token=${encodeURIComponent(pairingToken)}`);
      const res = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/octet-stream',
          'X-Pairing-Token': pairingToken
        },
        body: file
      });
      if (res.ok) {
        showToast(`Sent to PC: ${file.name}`);
        refreshRecentFiles();
      } else {
        showToast(`Upload failed (HTTP ${res.status})`);
      }
    } catch (e) {
      showToast(`Upload error: ${e.message}`);
    }
  }

  // -------------------------------------------------------------
  // UTILITY HELPERS
  // -------------------------------------------------------------
  function getFileIconSvg(fileName) {
    const ext = fileName.split('.').pop().toLowerCase();
    if (['zip', 'rar', '7z', 'tar', 'gz'].includes(ext)) return SVGS.archive;
    if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'svg'].includes(ext)) return SVGS.image;
    if (['mp4', 'mkv', 'avi', 'mov', 'webm'].includes(ext)) return SVGS.video;
    if (['mp3', 'wav', 'flac', 'ogg', 'm4a'].includes(ext)) return SVGS.audio;
    if (ext === 'pdf') return SVGS.pdf;
    return SVGS.doc;
  }

  function getMimeType(fileName) {
    const ext = fileName.split('.').pop().toLowerCase();
    const map = {
      'mp4': 'video/mp4', 'mkv': 'video/x-matroska', 'webm': 'video/webm',
      'mp3': 'audio/mpeg', 'wav': 'audio/wav', 'flac': 'audio/flac',
      'jpg': 'image/jpeg', 'jpeg': 'image/jpeg', 'png': 'image/png',
      'pdf': 'application/pdf', 'zip': 'application/zip'
    };
    return map[ext] || 'application/octet-stream';
  }

  function formatBytes(bytes) {
    if (!bytes || bytes <= 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return (bytes / Math.pow(1024, i)).toFixed(1) + ' ' + units[i];
  }

  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function escapeJs(str) {
    if (!str) return '';
    return str.replace(/\\/g, '\\\\').replace(/'/g, "\\'");
  }

  // -------------------------------------------------------------
  // INITIALIZATION
  // -------------------------------------------------------------
  document.addEventListener('DOMContentLoaded', () => {
    // Probe PC endpoint
    probeServerUrls().then(() => {
      refreshAllWidgets();
      refreshRecentFiles();
    });

    // Start live uptime ticker (updates "Connected for Xh Ym Zs" every second!)
    connectionUptimeInterval = setInterval(updateUptimeDisplay, 1000);

    // Periodic polling
    statusPollTimer = setInterval(() => {
      if (!isManualDisconnect) {
        updatePcStatusMetrics();
      }
    }, 4000);
  });

})();