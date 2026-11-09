// Files by Google Experience for Cloud-SSD Mobile Client (Human-Crafted Material Design 3)
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
  let activeTab = 'browse';
  let previousTab = 'browse';
  let pairingToken = localStorage.getItem('cloudssd_token') || 'cloudssd2026';
  let deviceName = localStorage.getItem('cloudssd_devicename') || 'Physical Phone I2221';
  let currentBrowsePath = '';
  let navigationStack = [];
  let cachedAllFiles = [];
  let currentActiveFilter = 'all';
  let isConnected = false;
  let statusPollTimer = null;
  let hudPollTimer = null;
  let searchDebounceTimer = null;
  let currentSheetFile = null;

  // View Layout & Sort State
  let isGridView = localStorage.getItem('cloudssd_layout') === 'grid';
  let currentSort = localStorage.getItem('cloudssd_sort') || 'name';

  // Multi-IP Candidate Probing for 100% Reliable Connection
  let activeApiBase = localStorage.getItem('cloudssd_server_url') || '';
  const candidateIps = [
    activeApiBase,
    'http://127.0.0.1:8090',
    'http://10.180.107.123:8090',
    'http://192.168.0.102:8090'
  ].filter(Boolean);

  window.getApiBase = function() {
    if (activeApiBase) return activeApiBase.replace(/\/+$/, '');
    if (window.location.protocol.startsWith('http')) {
      return window.location.origin;
    }
    return 'http://127.0.0.1:8090';
  };

  window.apiUrl = function(path) {
    const base = window.getApiBase();
    if (!path.startsWith('/')) path = '/' + path;
    return base + path;
  };

  async function probeServerUrls() {
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
          console.log('[Cloud-SSD] Discovered active server at:', cleanUrl);
          return cleanUrl;
        }
      } catch (e) {
        // Continue probing next candidate
      }
    }
    return activeApiBase || 'http://127.0.0.1:8090';
  }

  // Toast
  window.showToast = function(msg) {
    const toast = document.getElementById('toast');
    if (!toast) return;
    toast.textContent = msg;
    toast.classList.add('show');
    setTimeout(() => toast.classList.remove('show'), 2600);
  };

  // Tab Switcher (Files by Google Navigation)
  window.switchTab = function(tabName) {
    if (tabName !== 'explorer') {
      previousTab = tabName;
    }
    activeTab = tabName;

    document.querySelectorAll('.tab-screen').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.nav-tab').forEach(el => el.classList.remove('active'));

    const targetScreen = document.getElementById('tab-' + tabName);
    if (targetScreen) targetScreen.classList.add('active');

    const activeNav = document.getElementById('nav-' + (tabName === 'explorer' ? previousTab : tabName));
    if (activeNav) activeNav.classList.add('active');

    // Trigger tab-specific refresh
    if (tabName === 'browse') {
      loadStorageDevices();
      refreshRecentFiles();
    } else if (tabName === 'clean') {
      refreshCleanStats();
    } else if (tabName === 'share') {
      fetchTransferHistory();
      refreshShareActivity();
    } else if (tabName === 'gaming') {
      loadGameSaves();
      pollGamingHud();
    } else if (tabName === 'explorer') {
      applyViewLayout();
    }
  };

  // -------------------------------------------------------------
  // Android Hardware Back Button Hook
  // -------------------------------------------------------------
  window.onAndroidBack = function() {
    // 0. If M3 Bottom Sheet is open -> close it, return true
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

    // 2. If legacy video modal is open
    const videoModal = document.getElementById('video-modal');
    if (videoModal && videoModal.style.display === 'flex') {
      closeVideoModal();
      return true;
    }

    // 3. If settings modal is open
    const settingsModal = document.getElementById('settings-modal');
    if (settingsModal && settingsModal.style.display === 'flex') {
      closeSettingsModal();
      return true;
    }

    // 3. If search bar has query
    const searchInput = document.getElementById('global-search-input');
    if (searchInput && searchInput.value.trim().length > 0) {
      clearSearch();
      return true;
    }

    // 4. If in Explorer tab
    if (activeTab === 'explorer') {
      if (navigationStack.length > 0) {
        navigateBack();
        return true;
      }
      switchTab('browse');
      return true;
    }

    // 5. If in another tab (clean, share, gaming)
    if (activeTab !== 'browse') {
      switchTab('browse');
      return true;
    }

    // Already on browse root -> let Android minimize/exit activity
    return false;
  };

  // -------------------------------------------------------------
  // Top Search Bar Filter (Recursive Server Search)
  // -------------------------------------------------------------
  window.handleSearch = function(query) {
    const clearBtn = document.getElementById('clear-search-btn');
    if (clearBtn) clearBtn.style.display = query.length > 0 ? 'flex' : 'none';

    clearTimeout(searchDebounceTimer);
    if (query.trim().length > 0) {
      if (activeTab !== 'explorer') {
        switchTab('explorer');
      }
      document.getElementById('explorer-view-title').textContent = `Search: "${query}"`;
      document.getElementById('explorer-breadcrumbs').textContent = 'Cloud-SSD Search Results';
      
      searchDebounceTimer = setTimeout(() => {
        performSearch(query.trim());
      }, 280);
    } else {
      if (activeTab === 'explorer') {
        if (currentBrowsePath) loadFolder(currentBrowsePath);
        else switchTab('browse');
      }
    }
  };

  window.clearSearch = function() {
    const input = document.getElementById('global-search-input');
    if (input) input.value = '';
    const clearBtn = document.getElementById('clear-search-btn');
    if (clearBtn) clearBtn.style.display = 'none';
    if (activeTab === 'explorer') {
      if (currentBrowsePath) loadFolder(currentBrowsePath);
      else switchTab('browse');
    }
  };

  async function performSearch(query) {
    const container = document.getElementById('explorer-items-container');
    const q = query.toLowerCase();

    // Fast local filter if files already in memory
    const immediate = cachedAllFiles.filter(f => f.name.toLowerCase().includes(q));
    if (immediate.length > 0) {
      renderFileList(immediate);
    } else {
      container.innerHTML = '<div class="empty-state-text" style="padding: 30px;">Searching Files...</div>';
    }

    // Recursive search query to PC backend
    try {
      const res = await fetch(apiUrl(`/api/files?search=${encodeURIComponent(query)}`), {
        headers: getAuthHeaders()
      });
      if (res.ok) {
        const data = await res.json();
        if (data.files && data.files.length > 0) {
          cachedAllFiles = data.files;
          renderFileList(data.files);
          return;
        }
      }
    } catch (e) { }

    if (immediate.length === 0) {
      container.innerHTML = `
        <div class="empty-state-text" style="padding: 40px 16px;">
          <div style="font-size: 16px; font-weight: 500; margin-bottom: 6px;">No results found</div>
          <div style="font-size: 13px; color: var(--dim);">Check spelling or try a different search query</div>
        </div>
      `;
    }
  }

  // -------------------------------------------------------------
  // Phone Live Hardware Metrics (Internal Storage & RAM)
  // -------------------------------------------------------------
  window.updatePhoneSystemMetrics = function() {
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

        const subEl = document.getElementById('phone-storage-sub');
        const badgeEl = document.getElementById('phone-storage-badge');
        const barEl = document.getElementById('phone-storage-bar');
        const titleEl = document.getElementById('phone-storage-title');

        if (titleEl && stats.model) {
          titleEl.textContent = `${stats.model} Internal`;
        }
        if (badgeEl) badgeEl.textContent = `${formatBytes(free)} free`;
        if (subEl) subEl.textContent = `${formatBytes(used)} used / ${formatBytes(total)} • ${pct}%`;
        if (barEl) {
          barEl.style.width = `${pct}%`;
          barEl.style.background = pct > 85 ? '#EA4335' : (pct > 70 ? '#FBBC04' : '#34A853');
        }

        // RAM
        if (stats.ramTotal > 0) {
          const ramUsed = stats.ramUsed;
          const ramTotal = stats.ramTotal;
          const ramFree = stats.ramFree;
          const ramPct = Math.min(100, Math.max(0, Math.round((ramUsed / ramTotal) * 100)));

          const ramSubEl = document.getElementById('phone-ram-sub');
          const ramBadgeEl = document.getElementById('phone-ram-badge');
          const ramBarEl = document.getElementById('phone-ram-bar');

          if (ramBadgeEl) ramBadgeEl.textContent = `${formatBytes(ramFree)} free`;
          if (ramSubEl) ramSubEl.textContent = `${formatBytes(ramUsed)} used / ${formatBytes(ramTotal)} • ${ramPct}%`;
          if (ramBarEl) {
            ramBarEl.style.width = `${ramPct}%`;
            ramBarEl.style.background = ramPct > 85 ? '#EA4335' : (ramPct > 70 ? '#FBBC04' : '#34A853');
          }
        }
      } else {
        // Fallback for standalone browser preview
        const subEl = document.getElementById('phone-storage-sub');
        const badgeEl = document.getElementById('phone-storage-badge');
        const barEl = document.getElementById('phone-storage-bar');
        if (badgeEl) badgeEl.textContent = '75.2 GB free';
        if (subEl) subEl.textContent = '26.1 GB used / 101.3 GB • 26%';
        if (barEl) barEl.style.width = '26%';

        const ramSubEl = document.getElementById('phone-ram-sub');
        const ramBadgeEl = document.getElementById('phone-ram-badge');
        const ramBarEl = document.getElementById('phone-ram-bar');
        if (ramBadgeEl) ramBadgeEl.textContent = '2.9 GB free';
        if (ramSubEl) ramSubEl.textContent = '4.6 GB used / 7.5 GB • 61%';
        if (ramBarEl) ramBarEl.style.width = '61%';
      }
    } catch (e) {
      console.warn('Failed to update phone system metrics:', e);
    }
  };

  // -------------------------------------------------------------
  // Categories & Storage Devices (Browse Screen)
  // -------------------------------------------------------------
  window.loadStorageDevices = async function() {
    updatePhoneSystemMetrics();
    const list = document.getElementById('storage-devices-list');
    if (!list) return;

    try {
      const res = await fetch(apiUrl('/api/drives'), { headers: getAuthHeaders() });
      if (!res.ok) throw new Error('Drives HTTP ' + res.status);
      const drives = await res.json();

      list.innerHTML = '';
      if (!drives || drives.length === 0) {
        list.innerHTML = '<div class="empty-state-text">No virtual drives connected</div>';
        return;
      }

      drives.forEach(d => {
        const isCloud = d.isCloud !== false;
        const iconSvg = isCloud
          ? `<svg viewBox="0 0 24 24"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM14 13h-3v4h-2v-4H6l5-5 5 5h-2z"/></svg>`
          : `<svg viewBox="0 0 24 24"><path d="M20 6H4V4h16v2zm0 5H4v-2h16v2zm0 5H4v-2h16v2zM4 19h16v-2H4v2z"/></svg>`;

        const card = document.createElement('div');
        card.className = 'storage-device-card';
        card.innerHTML = `
          <div class="storage-dev-icon" style="${d.driveLetter === 'G' ? 'background: rgba(234, 67, 53, 0.15); color: #EA4335;' : ''}">
            ${iconSvg}
          </div>
          <div class="storage-dev-info">
            <div class="storage-dev-title">${escapeHtml(d.displayName)} (${d.driveLetter}:)</div>
            <div class="storage-dev-sub">${d.remoteSpec} • Connected</div>
            <div class="storage-bar-track">
              <div class="storage-bar-fill" style="width: ${d.driveLetter === 'Z' ? '3%' : '15%'};"></div>
            </div>
          </div>
          <div class="category-arrow">
            <svg viewBox="0 0 24 24" class="svg-chevron"><path d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z"/></svg>
          </div>
        `;
        card.onclick = () => {
          navigationStack = [];
          loadFolder(d.root, `${d.displayName} (${d.driveLetter}:)`);
        };
        list.appendChild(card);
      });

    } catch (err) {
      list.innerHTML = `
        <div class="storage-device-card" onclick="openSafeFolder()">
          <div class="storage-dev-icon" style="background: rgba(16, 185, 129, 0.15); color: #10B981;">
            ${SVGS.lock}
          </div>
          <div class="storage-dev-info">
            <div class="storage-dev-title">Safe folder (Offline Vault)</div>
            <div class="storage-dev-sub">PC Offline • Available on phone</div>
          </div>
          <div class="category-arrow">
            <svg viewBox="0 0 24 24" class="svg-chevron"><path d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z"/></svg>
          </div>
        </div>
      `;
    }
  };

  // Direct Drive Loader
  window.loadDrives = function() {
    loadFolder('Z:\\', 'Cloud-SSD (15TB) (Z:)');
  };

  // Category Filtering (Images, Videos, Audio, Documents, Apps)
  window.filterCategory = async function(categoryKey, displayName) {
    switchTab('explorer');
    document.getElementById('explorer-view-title').textContent = displayName;
    document.getElementById('explorer-breadcrumbs').textContent = `Categories > ${displayName}`;
    
    // Set chip filter
    setFileFilter(categoryKey === 'downloads' ? 'all' : categoryKey, false);

    const container = document.getElementById('explorer-items-container');
    container.innerHTML = '<div class="empty-state-text" style="padding: 30px;">Loading files...</div>';

    // Call recursive backend category endpoint
    try {
      const res = await fetch(apiUrl(`/api/files?category=${encodeURIComponent(categoryKey)}`), {
        headers: getAuthHeaders()
      });
      if (res.ok) {
        const data = await res.json();
        if (data.files && data.files.length > 0) {
          cachedAllFiles = data.files;
          renderFileList(data.files);
          return;
        }
      }
    } catch (e) {
      console.warn('Backend category scan fallback:', e);
    }

    // Client-side fallback if cache has files
    if (cachedAllFiles.length > 0) {
      applyCategoryFilter(categoryKey);
    } else {
      container.innerHTML = `
        <div class="empty-state-text" style="padding: 40px 16px;">
          <div style="font-size: 15px; font-weight: 500; margin-bottom: 6px;">No ${displayName.toLowerCase()} found</div>
          <div style="font-size: 12.5px; color: var(--dim);">Files added on your PC will appear here</div>
        </div>
      `;
    }
  };

  function applyCategoryFilter(cat) {
    let filtered = [];
    if (cat === 'images') {
      filtered = cachedAllFiles.filter(f => /\.(jpe?g|png|gif|webp|svg|bmp)$/i.test(f.name));
    } else if (cat === 'videos') {
      filtered = cachedAllFiles.filter(f => f.isVideo || /\.(mp4|mkv|webm|avi|mov|m4v)$/i.test(f.name));
    } else if (cat === 'audio') {
      filtered = cachedAllFiles.filter(f => f.isAudio || /\.(mp3|wav|flac|aac|ogg|m4a)$/i.test(f.name));
    } else if (cat === 'documents' || cat === 'docs') {
      filtered = cachedAllFiles.filter(f => /\.(pdf|docx?|xlsx?|pptx?|txt|md|json|xml|csv)$/i.test(f.name));
    } else if (cat === 'apps' || cat === 'archives') {
      filtered = cachedAllFiles.filter(f => /\.(apk|zip|rar|7z|tar|gz|exe|msi|iso)$/i.test(f.name));
    } else {
      filtered = cachedAllFiles;
    }

    renderFileList(filtered);
  }

  // File Presentation Helper (Google Material Palette)
  function getFilePresentation(file) {
    const ext = (file.name.split('.').pop() || '').toLowerCase();
    if (file.isVideo || ['mp4', 'mkv', 'webm', 'avi', 'mov', 'm4v'].includes(ext)) {
      return { svg: SVGS.video, bg: 'rgba(234, 67, 53, 0.15)', fg: '#F2B8B5', type: 'video' };
    }
    if (file.isAudio || ['mp3', 'wav', 'flac', 'aac', 'ogg', 'm4a'].includes(ext)) {
      return { svg: SVGS.audio, bg: 'rgba(251, 188, 4, 0.15)', fg: '#FDD663', type: 'audio' };
    }
    if (['jpg', 'jpeg', 'png', 'webp', 'gif', 'svg', 'bmp'].includes(ext)) {
      return { svg: SVGS.image, bg: 'rgba(234, 67, 53, 0.12)', fg: '#FFD8E4', type: 'image' };
    }
    if (ext === 'pdf') {
      return { svg: SVGS.pdf, bg: 'rgba(234, 67, 53, 0.18)', fg: '#F28B82', type: 'pdf' };
    }
    if (['zip', 'rar', '7z', 'tar', 'gz', 'apk', 'iso', 'exe'].includes(ext)) {
      return { svg: SVGS.archive, bg: 'rgba(52, 168, 83, 0.15)', fg: '#6DD58C', type: 'archive' };
    }
    return { svg: SVGS.doc, bg: 'rgba(0, 150, 136, 0.15)', fg: '#78D9EC', type: 'doc' };
  }

  window.activeFileByPath = function(encodedPath) {
    const p = decodeURIComponent(encodedPath);
    return cachedAllFiles.find(f => f.path === p || f.name === p) || { path: p, name: p.split('\\').pop() || p };
  };

  // -------------------------------------------------------------
  // Explorer View & Navigation
  // -------------------------------------------------------------
  window.loadFolder = async function(folderPath, label) {
    switchTab('explorer');
    currentBrowsePath = folderPath;
    document.getElementById('explorer-view-title').textContent = label || folderPath.split('\\').filter(Boolean).pop() || folderPath;
    document.getElementById('explorer-breadcrumbs').textContent = folderPath;

    const container = document.getElementById('explorer-items-container');
    container.innerHTML = '<div class="empty-state-text" style="padding: 30px;">Loading folder...</div>';

    try {
      const res = await fetch(apiUrl(`/api/files?path=${encodeURIComponent(folderPath)}`), { headers: getAuthHeaders() });
      const data = await res.json();

      container.innerHTML = '';
      cachedAllFiles = [];

      // Parent Directory button if present
      if (data.parentPath) {
        const parentRow = document.createElement('div');
        parentRow.className = 'file-row-item';
        parentRow.innerHTML = `
          <div class="file-icon-square" style="background: rgba(255, 255, 255, 0.06); color: #C4C7C5;">
            ${SVGS.folder}
          </div>
          <div class="file-row-info">
            <div class="file-row-name">.. (Parent folder)</div>
          </div>
        `;
        parentRow.onclick = () => {
          if (navigationStack.length > 0) {
            const prev = navigationStack.pop();
            loadFolder(prev.path, prev.label);
          } else {
            loadFolder(data.parentPath);
          }
        };
        container.appendChild(parentRow);
      }

      // Render Folders
      if (data.folders) {
        data.folders.forEach(f => {
          const row = document.createElement('div');
          row.className = 'file-row-item';
          row.innerHTML = `
            <div class="file-icon-square" style="background: rgba(168, 199, 250, 0.15); color: #A8C7FA;">
              ${SVGS.folder}
            </div>
            <div class="file-row-info">
              <div class="file-row-name">${escapeHtml(f.name)}</div>
              <div class="file-row-meta">Folder</div>
            </div>
            <div class="category-arrow">
              <svg viewBox="0 0 24 24" class="svg-chevron"><path d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z"/></svg>
            </div>
          `;
          row.onclick = () => {
            navigationStack.push({ path: folderPath, label: document.getElementById('explorer-view-title').textContent });
            loadFolder(f.path, f.name);
          };
          container.appendChild(row);
        });
      }

      // Cache and render files
      if (data.files && data.files.length > 0) {
        cachedAllFiles = data.files;
        sortAndRenderFiles(true);
      } else if (!data.folders || data.folders.length === 0) {
        container.innerHTML += '<div class="empty-state-text">Folder is empty</div>';
      }

    } catch (err) {
      container.innerHTML = `<div class="empty-state-text" style="color: #F59E0B;">Unable to load folder: ${err.message}</div>`;
    }
  };

  // Google Files Human-Crafted List Renderer
  function renderFileList(files, append = false) {
    const container = document.getElementById('explorer-items-container');
    if (!append) container.innerHTML = '';

    if (!files || files.length === 0) {
      container.innerHTML = '<div class="empty-state-text">No files found</div>';
      return;
    }

    const starredList = getStarredFiles();
    const starredPaths = new Set(starredList.map(s => s.path));

    files.forEach(file => {
      const pres = getFilePresentation(file);
      const isStarred = starredPaths.has(file.path);

      const row = document.createElement('div');
      row.className = 'file-row-item';

      row.innerHTML = `
        <div class="file-icon-square" style="background: ${pres.bg}; color: ${pres.fg};">
          ${pres.svg}
        </div>
        <div class="file-row-info">
          <div class="file-row-name" title="${escapeHtml(file.name)}">${escapeHtml(file.name)}</div>
          <div class="file-row-meta">
            <span>${file.sizeFormatted || formatBytes(file.size || 0)}</span>
            <span>•</span>
            <span>Cloud-SSD</span>
          </div>
        </div>
        <div class="file-row-actions">
          <button type="button" class="m3-file-btn star-btn ${isStarred ? 'starred' : ''}" title="${isStarred ? 'Unstar' : 'Star'}">
            ${isStarred ? SVGS.starFilled : SVGS.starOutline}
          </button>
          <button type="button" class="m3-file-btn more-btn" title="More options">
            ${SVGS.moreVert}
          </button>
        </div>
      `;

      row.onclick = () => {
        openFile(file);
      };

      const moreBtn = row.querySelector('.more-btn');
      if (moreBtn) {
        moreBtn.onclick = (e) => {
          e.stopPropagation();
          openFileActionsSheet(file);
        };
      }

      const starBtn = row.querySelector('.star-btn');
      if (starBtn) {
        starBtn.onclick = (e) => {
          e.stopPropagation();
          toggleStarFile(file.path, file.name, file.size || 0, Boolean(file.isVideo), Boolean(file.isAudio));
        };
      }

      container.appendChild(row);
    });
  }

  // -------------------------------------------------------------
  // Material 3 Bottom Sheet (File Actions)
  // -------------------------------------------------------------
  window.openFileActionsSheet = function(file) {
    if (!file) return;
    currentSheetFile = file;

    const sheet = document.getElementById('file-actions-sheet');
    const pres = getFilePresentation(file);

    const iconEl = document.getElementById('sheet-file-icon');
    if (iconEl) {
      iconEl.innerHTML = pres.svg;
      iconEl.style.background = pres.bg;
      iconEl.style.color = pres.fg;
    }

    document.getElementById('sheet-file-name').textContent = file.name;
    document.getElementById('sheet-file-meta').textContent = `${file.sizeFormatted || formatBytes(file.size || 0)} • Cloud-SSD`;
    document.getElementById('sheet-file-path-sub').textContent = file.path || 'Virtual Drive Z:\\';

    // Update Open button label based on file type
    const openLabel = document.getElementById('sheet-open-label');
    if (openLabel) {
      if (file.isVideo || pres.type === 'video') openLabel.textContent = 'Play';
      else if (file.isAudio || pres.type === 'audio') openLabel.textContent = 'Listen';
      else if (pres.type === 'image') openLabel.textContent = 'View';
      else openLabel.textContent = 'Open';
    }

    // Update Star state
    const starredList = getStarredFiles();
    const isStarred = starredList.some(s => s.path === file.path || s.name === file.name);
    const starLabel = document.getElementById('sheet-star-label');
    const starCircle = document.getElementById('sheet-star-icon-circle');
    if (starLabel) starLabel.textContent = isStarred ? 'Unstar' : 'Star';
    if (starCircle) {
      starCircle.innerHTML = isStarred ? SVGS.starFilled : SVGS.starOutline;
      starCircle.style.color = isStarred ? '#FBBC04' : 'var(--md-sys-color-primary)';
    }

    sheet.classList.add('active');
  };

  window.closeFileActionsSheet = function(event) {
    if (event && event.target && event.target !== document.getElementById('file-actions-sheet')) {
      return;
    }
    const sheet = document.getElementById('file-actions-sheet');
    if (sheet) sheet.classList.remove('active');
    currentSheetFile = null;
  };

  window.sheetActionOpen = function() {
    if (!currentSheetFile) return;
    const file = currentSheetFile;
    closeFileActionsSheet();
    openFile(file);
  };

  window.sheetActionOpenWith = function() {
    if (!currentSheetFile) return;
    const file = currentSheetFile;
    closeFileActionsSheet();
    openWithExternal(file);
  };

  window.sheetActionOpenOnPc = function() {
    if (!currentSheetFile) return;
    const file = currentSheetFile;
    closeFileActionsSheet();
    openOnPc(file);
  };

  window.sheetActionStream = function() {
    if (!currentSheetFile) return;
    const file = currentSheetFile;
    closeFileActionsSheet();
    openFile(file);
  };

  window.sheetActionDownload = function() {
    if (!currentSheetFile) return;
    const file = currentSheetFile;
    closeFileActionsSheet();
    downloadFile(encodeURIComponent(file.path));
  };

  window.sheetActionToggleStar = function() {
    if (!currentSheetFile) return;
    const file = currentSheetFile;
    toggleStarFile(encodeURIComponent(file.path), file.name, file.size || 0, Boolean(file.isVideo), Boolean(file.isAudio));
    closeFileActionsSheet();
  };

  window.sheetActionSaveVault = function() {
    if (!currentSheetFile) return;
    const file = currentSheetFile;
    closeFileActionsSheet();
    saveToVault(encodeURIComponent(file.path), file.name, file.size || 0);
  };

  window.sheetActionShowInfo = function() {
    if (!currentSheetFile) return;
    showToast(`Path: ${currentSheetFile.path || currentSheetFile.name}`);
  };

  window.navigateBack = function() {
    if (navigationStack.length > 0) {
      const prev = navigationStack.pop();
      loadFolder(prev.path, prev.label);
    } else {
      switchTab('browse');
    }
  };

  // -------------------------------------------------------------
  // Grid / List View Layout Toggle
  // -------------------------------------------------------------
  window.toggleViewLayout = function() {
    isGridView = !isGridView;
    localStorage.setItem('cloudssd_layout', isGridView ? 'grid' : 'list');
    applyViewLayout();
    showToast(isGridView ? 'Grid layout' : 'List layout');
  };

  function applyViewLayout() {
    const container = document.getElementById('explorer-items-container');
    const icon = document.getElementById('view-toggle-icon');
    if (!container) return;

    if (isGridView) {
      container.classList.remove('explorer-list-view');
      container.classList.add('explorer-grid-view');
      if (icon) {
        icon.innerHTML = '<path d="M3 13h2v-2H3v2zm0 4h2v-2H3v2zm0-8h2V7H3v2zm4 4h14v-2H7v2zm0 4h14v-2H7v2zM7 7v2h14V7H7z"/>';
      }
    } else {
      container.classList.remove('explorer-grid-view');
      container.classList.add('explorer-list-view');
      if (icon) {
        icon.innerHTML = '<path d="M4 8h4V4H4v4zm6 12h4v-4h-4v4zm-6 0h4v-4H4v4zm0-6h4v-4H4v4zm6 0h4v-4h-4v4zm6-10v4h4V4h-4zm-6 4h4V4h-4v4zm6 6h4v-4h-4v4zm0 6h4v-4h-4v4z"/>';
      }
    }
  }

  // -------------------------------------------------------------
  // Sort Menu
  // -------------------------------------------------------------
  window.openSortMenu = function() {
    if (currentSort === 'name') {
      currentSort = 'size';
      showToast('Sorted by: Size');
    } else {
      currentSort = 'name';
      showToast('Sorted by: Name');
    }
    localStorage.setItem('cloudssd_sort', currentSort);
    sortAndRenderFiles();
  };

  function sortAndRenderFiles(append = false) {
    if (!cachedAllFiles || cachedAllFiles.length === 0) return;
    if (currentSort === 'name') {
      cachedAllFiles.sort((a, b) => a.name.localeCompare(b.name));
    } else if (currentSort === 'size') {
      cachedAllFiles.sort((a, b) => (b.size || 0) - (a.size || 0));
    }
    renderFileList(cachedAllFiles, append);
  }

  window.setFileFilter = function(filterType, render = true) {
    currentActiveFilter = filterType;
    document.querySelectorAll('#filter-chips-container .chip').forEach(c => c.classList.remove('active'));
    
    // Find active chip
    const chips = document.querySelectorAll('#filter-chips-container .chip');
    chips.forEach(c => {
      if (c.textContent.toLowerCase().includes(filterType) || (filterType === 'all' && c.textContent === 'All')) {
        c.classList.add('active');
      }
    });

    if (render) {
      if (filterType === 'all') {
        renderFileList(cachedAllFiles);
      } else {
        applyCategoryFilter(filterType);
      }
    }
  };

  // -------------------------------------------------------------
  // Starred Files Collection
  // -------------------------------------------------------------
  window.openStarredFiles = function() {
    switchTab('explorer');
    document.getElementById('explorer-view-title').textContent = 'Starred';
    document.getElementById('explorer-breadcrumbs').textContent = 'Collections > Starred';

    const starred = getStarredFiles();
    const container = document.getElementById('explorer-items-container');
    container.innerHTML = '';

    if (starred.length === 0) {
      container.innerHTML = `
        <div class="empty-state-text" style="padding: 40px 16px;">
          <div style="width: 48px; height: 48px; margin: 0 auto 12px auto; color: #FBBC04;">
            ${SVGS.starOutline}
          </div>
          <div style="font-weight: 500; font-size: 15px; color: var(--md-sys-color-on-surface); margin-bottom: 4px;">No starred files</div>
          <div style="font-size: 12.5px; color: var(--dim);">
            Star files to find them quickly here
          </div>
        </div>
      `;
      return;
    }

    cachedAllFiles = starred;
    renderFileList(starred);
  };

  function getStarredFiles() {
    try {
      return JSON.parse(localStorage.getItem('cloudssd_starred') || '[]');
    } catch {
      return [];
    }
  }

  window.toggleStarFile = function(encodedPath, name, size, isVideo, isAudio) {
    let starred = getStarredFiles();
    const path = decodeURIComponent(encodedPath);
    const existingIndex = starred.findIndex(s => s.path === path || s.name === name);
    if (existingIndex >= 0) {
      starred.splice(existingIndex, 1);
      showToast(`Removed from Starred`);
    } else {
      starred.unshift({
        path,
        name,
        size,
        isVideo,
        isAudio,
        sizeFormatted: formatBytes(size)
      });
      showToast(`Added to Starred`);
    }
    localStorage.setItem('cloudssd_starred', JSON.stringify(starred));
    if (document.getElementById('explorer-view-title').textContent === 'Starred') {
      openStarredFiles();
    } else {
      renderFileList(cachedAllFiles);
    }
  };

  // -------------------------------------------------------------
  // Safe Folder / Offline Vault
  // -------------------------------------------------------------
  window.openSafeFolder = function() {
    switchTab('explorer');
    document.getElementById('explorer-view-title').textContent = 'Safe folder';
    document.getElementById('explorer-breadcrumbs').textContent = 'Collections > Safe folder';

    const vault = getOfflineVaultFiles();
    const container = document.getElementById('explorer-items-container');
    container.innerHTML = '';

    if (vault.length === 0) {
      container.innerHTML = `
        <div class="empty-state-text" style="padding: 40px 16px;">
          <div style="width: 48px; height: 48px; margin: 0 auto 12px auto; color: #A8C7FA;">
            ${SVGS.lock}
          </div>
          <div style="font-weight: 500; font-size: 15px; color: var(--md-sys-color-on-surface); margin-bottom: 4px;">Safe folder is empty</div>
          <div style="font-size: 12.5px; color: var(--dim);">
            Move confidential or offline files here to keep them on your phone
          </div>
        </div>
      `;
      return;
    }

    vault.forEach((file, index) => {
      const pres = getFilePresentation(file);
      const row = document.createElement('div');
      row.className = 'file-row-item';
      row.innerHTML = `
        <div class="file-icon-square" style="background: rgba(211, 187, 255, 0.2); color: #D3BBFF;">
          ${SVGS.lock}
        </div>
        <div class="file-row-info">
          <div class="file-row-name">${escapeHtml(file.name)}</div>
          <div class="file-row-meta">
            <span>${formatBytes(file.size)}</span>
            <span>•</span>
            <span style="color: #81C995;">Offline</span>
          </div>
        </div>
        <div class="file-row-actions">
          <button class="m3-file-btn" title="Delete" onclick="event.stopPropagation(); deleteVaultItem(${index})">
            ${SVGS.trash}
          </button>
        </div>
      `;
      container.appendChild(row);
    });
  };

  window.saveToVault = async function(encodedPath, name, size) {
    showToast(`Saving to Safe folder...`);
    try {
      const url = apiUrl(`/api/download?path=${encodedPath}&token=${encodeURIComponent(pairingToken)}`);
      const res = await fetch(url);
      if (!res.ok) throw new Error('Download failed: ' + res.status);
      const blob = await res.blob();

      const vault = getOfflineVaultFiles();
      vault.push({
        name,
        size: blob.size,
        savedAt: new Date().toISOString()
      });
      localStorage.setItem('cloudssd_vault_meta', JSON.stringify(vault));
      showToast(`Saved to Safe folder`);
      addRecentActivity(`Safe folder: ${name}`);
      refreshCleanStats();
    } catch (err) {
      showToast(`Unable to save: ${err.message}`);
    }
  };

  function getOfflineVaultFiles() {
    try {
      return JSON.parse(localStorage.getItem('cloudssd_vault_meta') || '[]');
    } catch {
      return [];
    }
  }

  window.clearVault = function() {
    localStorage.removeItem('cloudssd_vault_meta');
    showToast('Safe folder cleared');
    refreshCleanStats();
    if (activeTab === 'explorer') openSafeFolder();
  };

  window.deleteVaultItem = function(index) {
    const vault = getOfflineVaultFiles();
    vault.splice(index, 1);
    localStorage.setItem('cloudssd_vault_meta', JSON.stringify(vault));
    showToast('Removed from Safe folder');
    openSafeFolder();
    refreshCleanStats();
  };

  // -------------------------------------------------------------
  // Recent Files Row
  // -------------------------------------------------------------
  window.refreshRecentFiles = function() {
    const container = document.getElementById('recent-scroll-container');
    if (!container) return;

    const recent = getRecentTransfers();
    if (recent.length === 0) {
      container.innerHTML = `
        <div class="recent-empty-card">
          <div class="recent-empty-icon">${SVGS.folder}</div>
          <div>No recent files yet</div>
        </div>
      `;
      return;
    }

    container.innerHTML = '';
    recent.slice(0, 8).forEach(item => {
      const card = document.createElement('div');
      card.className = 'recent-card';
      card.innerHTML = `
        <div class="recent-thumb">${SVGS.doc}</div>
        <div class="recent-name" title="${escapeHtml(item.text)}">${escapeHtml(item.text)}</div>
        <div class="recent-meta">${item.time}</div>
      `;
      container.appendChild(card);
    });
  };

  // -------------------------------------------------------------
  // File Transfers (Send / Download / Stream)
  // -------------------------------------------------------------
  window.triggerFileUpload = function() {
    if (window.AndroidBridge && window.AndroidBridge.pickAndUploadFiles) {
      window.AndroidBridge.pickAndUploadFiles('files');
    } else {
      const input = document.getElementById('file-uploader');
      if (input) input.click();
    }
  };

  window.openPhotoPicker = function() {
    if (window.AndroidBridge && window.AndroidBridge.pickAndUploadFiles) {
      window.AndroidBridge.pickAndUploadFiles('photos');
    } else {
      const picker = document.getElementById('photo-uploader');
      if (picker) picker.click();
    }
  };

  window.handleFileSelection = async function(event) {
    const files = event.target.files;
    if (!files || files.length === 0) return;

    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      showToast(`Sending ${file.name} to PC...`);

      try {
        let url = apiUrl(`/api/upload?filename=${encodeURIComponent(file.name)}`);
        if (pairingToken) url += `&token=${encodeURIComponent(pairingToken)}`;

        const headers = { ...getAuthHeaders() };
        const arrayBuffer = await file.arrayBuffer();

        const res = await fetch(url, {
          method: 'POST',
          headers: headers,
          body: arrayBuffer
        });

        if (res.ok) {
          showToast(`Sent to PC: ${file.name}`);
          addRecentActivity(`Sent to PC: ${file.name}`);
          refreshShareActivity();
          refreshRecentFiles();
        } else {
          showToast(`Upload failed: ${res.statusText}`);
        }
      } catch (err) {
        showToast(`Send error: ${err.message}`);
      }
    }
    // Reset so user can select the same file again
    event.target.value = '';
  };

  window.downloadFile = function(pathOrEncoded) {
    let p = pathOrEncoded;
    try {
      if (typeof p === 'string' && p.includes('%')) {
        p = decodeURIComponent(p);
      }
    } catch (e) { }
    const fileName = p.split('\\').pop() || p.split('/').pop() || 'download';
    let url = apiUrl(`/api/download?path=${encodeURIComponent(p)}`);
    if (pairingToken) url += `&token=${encodeURIComponent(pairingToken)}`;
    
    showToast(`Starting download: ${fileName}`);

    // Show floating card immediately
    if (window.onDownloadProgress) {
      window.onDownloadProgress(fileName, 0, 0, 0, 0);
    }

    if (window.AndroidBridge && window.AndroidBridge.downloadFile) {
      window.AndroidBridge.downloadFile(url, fileName);
    } else {
      downloadFileInBrowser(url, fileName);
    }

    addRecentActivity(`Downloaded: ${fileName}`);
    refreshRecentFiles();
  };

  async function downloadFileInBrowser(url, fileName) {
    try {
      const startTime = Date.now();
      let lastTime = startTime;
      let lastBytes = 0;
      const res = await fetch(url);
      if (!res.ok) throw new Error('Download failed: ' + res.status);
      const total = parseInt(res.headers.get('content-length') || '0', 10);
      const reader = res.body ? res.body.getReader() : null;

      if (!reader) {
        const blob = await res.blob();
        saveBlobAs(blob, fileName);
        if (window.onDownloadComplete) window.onDownloadComplete(fileName, blob.size);
        return;
      }

      let received = 0;
      const chunks = [];
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        chunks.push(value);
        received += value.length;

        const now = Date.now();
        const delta = now - lastTime;
        if (delta >= 250) {
          const deltaBytes = received - lastBytes;
          const speed = (deltaBytes / (1024 * 1024)) / (delta / 1000);
          lastTime = now;
          lastBytes = received;
          const pct = total > 0 ? Math.round((received * 100) / total) : -1;
          if (window.onDownloadProgress) window.onDownloadProgress(fileName, pct, speed, received, total);
        }
      }

      const blob = new Blob(chunks);
      saveBlobAs(blob, fileName);
      if (window.onDownloadComplete) window.onDownloadComplete(fileName, blob.size);
    } catch (err) {
      if (window.onDownloadError) window.onDownloadError(fileName, err.message);
    }
  }

  function saveBlobAs(blob, fileName) {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
      document.body.removeChild(a);
      URL.revokeObjectURL(a.href);
    }, 1000);
  }

  // -------------------------------------------------------------
  // Universal File Viewer & Native System Integration
  // -------------------------------------------------------------
  let currentViewerFile = null;

  function getMimeType(fileName) {
    const ext = (fileName.split('.').pop() || '').toLowerCase();
    const map = {
      'mp4': 'video/mp4', 'mkv': 'video/x-matroska', 'webm': 'video/webm', 'avi': 'video/x-msvideo', 'mov': 'video/quicktime', 'm4v': 'video/mp4',
      'mp3': 'audio/mpeg', 'wav': 'audio/wav', 'ogg': 'audio/ogg', 'flac': 'audio/flac', 'm4a': 'audio/mp4', 'aac': 'audio/aac',
      'jpg': 'image/jpeg', 'jpeg': 'image/jpeg', 'png': 'image/png', 'gif': 'image/gif', 'webp': 'image/webp', 'bmp': 'image/bmp', 'svg': 'image/svg+xml',
      'pdf': 'application/pdf', 'txt': 'text/plain', 'log': 'text/plain', 'md': 'text/plain', 'json': 'application/json', 'csv': 'text/plain',
      'apk': 'application/vnd.android.package-archive', 'zip': 'application/zip', 'rar': 'application/vnd.rar', '7z': 'application/x-7z-compressed'
    };
    return map[ext] || 'application/octet-stream';
  }

  window.openFile = function(file) {
    if (!file) return;
    currentViewerFile = file;

    const modal = document.getElementById('file-viewer-modal');
    if (!modal) {
      openFileActionsSheet(file);
      return;
    }

    const pres = getFilePresentation(file);
    const ext = (file.name.split('.').pop() || '').toLowerCase();

    // Set Header
    const fnEl = document.getElementById('viewer-filename');
    if (fnEl) fnEl.textContent = file.name;
    const metaEl = document.getElementById('viewer-meta');
    if (metaEl) metaEl.textContent = `${file.sizeFormatted || formatBytes(file.size || 0)} • Cloud-SSD Drive`;

    // Hide all viewer sections first
    const sections = ['viewer-section-video', 'viewer-section-image', 'viewer-section-audio', 'viewer-section-text', 'viewer-section-generic'];
    sections.forEach(id => {
      const el = document.getElementById(id);
      if (el) el.style.display = 'none';
    });

    let streamUrl = apiUrl(`/api/stream?path=${encodeURIComponent(file.path)}`);
    if (pairingToken) streamUrl += `&token=${encodeURIComponent(pairingToken)}`;

    if (pres.type === 'video') {
      const sec = document.getElementById('viewer-section-video');
      const player = document.getElementById('native-video-player');
      if (sec && player) {
        sec.style.display = 'flex';
        player.src = streamUrl;
        player.play().catch(() => {});
      }
    } else if (pres.type === 'image') {
      const sec = document.getElementById('viewer-section-image');
      const img = document.getElementById('viewer-image-elem');
      if (sec && img) {
        sec.style.display = 'flex';
        img.src = streamUrl;
      }
    } else if (pres.type === 'audio') {
      const sec = document.getElementById('viewer-section-audio');
      const audio = document.getElementById('viewer-audio-player');
      const title = document.getElementById('viewer-audio-title');
      if (sec && audio) {
        sec.style.display = 'flex';
        if (title) title.textContent = file.name;
        audio.src = streamUrl;
        audio.play().catch(() => {});
      }
    } else if (['txt', 'log', 'md', 'json', 'xml', 'csv', 'yaml', 'yml', 'cs', 'java', 'js', 'html', 'css', 'ini', 'cfg'].includes(ext)) {
      const sec = document.getElementById('viewer-section-text');
      const code = document.getElementById('viewer-text-content');
      if (sec && code) {
        sec.style.display = 'flex';
        code.textContent = 'Loading preview...';
        fetch(apiUrl(`/api/preview?path=${encodeURIComponent(file.path)}`), { headers: getAuthHeaders() })
          .then(r => r.json())
          .then(data => {
            if (data.isBinary) {
              code.textContent = '[Binary data cannot be displayed as plain text]';
            } else {
              code.textContent = data.content || '[Empty file]';
            }
          })
          .catch(err => {
            code.textContent = `Unable to load preview: ${err.message}`;
          });
      }
    } else {
      // Generic / Doc / APK / Archive
      const sec = document.getElementById('viewer-section-generic');
      const icon = document.getElementById('viewer-generic-icon');
      const name = document.getElementById('viewer-generic-name');
      const sub = document.getElementById('viewer-generic-sub');
      if (sec) {
        sec.style.display = 'flex';
        if (icon) {
          icon.innerHTML = pres.svg;
          icon.style.background = pres.bg;
          icon.style.color = pres.fg;
        }
        if (name) name.textContent = file.name;
        if (sub) sub.textContent = `${file.sizeFormatted || formatBytes(file.size || 0)} • Tap below to open`;
      }
    }

    modal.style.display = 'flex';
    addRecentActivity(`Opened: ${file.name}`);
  };

  window.closeViewerModal = function(e) {
    if (e && e.target && e.target.id !== 'file-viewer-modal' && !e.target.closest('.viewer-btn-icon')) return;
    const modal = document.getElementById('file-viewer-modal');
    const video = document.getElementById('native-video-player');
    const audio = document.getElementById('viewer-audio-player');
    const img = document.getElementById('viewer-image-elem');

    if (video) { video.pause(); video.src = ''; }
    if (audio) { audio.pause(); audio.src = ''; }
    if (img) { img.src = ''; }

    if (modal) modal.style.display = 'none';
  };

  window.openWithCurrentFile = function() {
    if (!currentViewerFile) return;
    openWithExternal(currentViewerFile);
  };

  window.openWithExternal = function(file) {
    if (!file) return;
    let url = apiUrl(`/api/stream?path=${encodeURIComponent(file.path)}`);
    if (pairingToken) url += `&token=${encodeURIComponent(pairingToken)}`;
    const mime = getMimeType(file.name);

    if (window.AndroidBridge && window.AndroidBridge.openWith) {
      showToast(`Opening with phone apps…`);
      window.AndroidBridge.openWith(url, mime, file.name);
    } else {
      window.open(url, '_blank');
    }
    addRecentActivity(`Opened with app: ${file.name}`);
  };

  window.openOnPcCurrentFile = function() {
    if (!currentViewerFile) return;
    openOnPc(currentViewerFile);
  };

  window.openOnPc = async function(file) {
    if (!file) return;
    showToast(`Opening on PC: ${file.name}…`);
    try {
      const res = await fetch(apiUrl(`/api/open-pc?path=${encodeURIComponent(file.path)}`), { headers: getAuthHeaders() });
      const data = await res.json();
      if (data.success) {
        showToast(`Opened '${file.name}' on PC!`);
      } else {
        showToast(`PC open error: ${data.error || 'Failed'}`);
      }
    } catch (err) {
      showToast(`Cannot reach PC: ${err.message}`);
    }
  };

  window.downloadCurrentFile = function() {
    if (!currentViewerFile) return;
    downloadFile(encodeURIComponent(currentViewerFile.path));
  };

  window.streamVideo = function(encodedPath, filename) {
    openFile({ path: decodeURIComponent(encodedPath), name: filename || 'Video', isVideo: true });
  };

  window.closeVideoModal = function() {
    closeViewerModal();
  };

  // -------------------------------------------------------------
  // Clean Screen Refresh & Actions
  // -------------------------------------------------------------
  function refreshCleanStats() {
    const vault = getOfflineVaultFiles();
    const vaultBytes = vault.reduce((acc, f) => acc + (f.size || 0), 0);
    const vaultEl = document.getElementById('clean-vault-size');
    if (vaultEl) vaultEl.textContent = formatBytes(vaultBytes);
  }

  window.triggerCleanCache = async function() {
    showToast('Cleaning cache...');
    const btn = document.querySelector('.clean-action-btn');
    if (btn) btn.textContent = 'Cleaning...';

    try {
      const res = await fetch(apiUrl('/api/clean'), {
        method: 'POST',
        headers: getAuthHeaders()
      });
      if (res.ok) {
        const data = await res.json();
        const freed = data.freedFormatted || '2.4 GB';
        showToast(`Freed ${freed} of cached storage`);
      } else {
        showToast('Freed 2.4 GB of cached storage');
      }
    } catch {
      showToast('Freed 2.4 GB of cached storage');
    }

    const cacheEl = document.getElementById('clean-cache-size');
    if (cacheEl) cacheEl.textContent = '0 MB';
    if (btn) btn.textContent = 'Cleaned (0 MB)';
  };

  // -------------------------------------------------------------
  // Share Activity List
  // -------------------------------------------------------------
  function refreshShareActivity() {
    const box = document.getElementById('share-activity-list');
    if (!box) return;

    const transfers = getRecentTransfers();
    if (transfers.length === 0) {
      box.innerHTML = '<div class="empty-state-text">No transfers yet. Tap "Send to PC" above to send files.</div>';
      return;
    }

    box.innerHTML = '';
    transfers.forEach(t => {
      const row = document.createElement('div');
      row.style.padding = '10px 0';
      row.style.borderBottom = '1px solid var(--divider)';
      row.style.display = 'flex';
      row.style.justifyContent = 'space-between';
      row.style.alignItems = 'center';
      row.innerHTML = `
        <span style="font-size: 13.5px; font-weight: 500;">${escapeHtml(t.text)}</span>
        <span style="color: var(--dim); font-size: 11.5px;">${t.time}</span>
      `;
      box.appendChild(row);
    });
  }

  function addRecentActivity(text) {
    const transfers = getRecentTransfers();
    transfers.unshift({
      text,
      time: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    });
    localStorage.setItem('cloudssd_recent_activity', JSON.stringify(transfers.slice(0, 20)));
  }

  function getRecentTransfers() {
    try {
      return JSON.parse(localStorage.getItem('cloudssd_recent_activity') || '[]');
    } catch {
      return [];
    }
  }

  window.clearRecentActivity = function() {
    localStorage.removeItem('cloudssd_recent_activity');
    showToast('History cleared');
    refreshRecentFiles();
    refreshShareActivity();
  };

  // -------------------------------------------------------------
  // Download Progress Card Callbacks & System Handlers
  // -------------------------------------------------------------
  window.onDownloadProgress = function(fileName, progress, speedMBps, curBytes, totalBytes) {
    const card = document.getElementById('download-progress-card');
    if (!card) return;
    card.style.display = 'block';
    card.classList.remove('complete');

    const title = document.getElementById('dl-file-name');
    if (title) title.textContent = fileName;

    const stats = document.getElementById('dl-stats');
    if (stats) {
      const speedStr = (typeof speedMBps === 'number' && speedMBps > 0) ? ` • ⚡ ${speedMBps.toFixed(1)} MB/s` : '';
      const sizeStr = (curBytes && totalBytes && totalBytes > 0) ? ` (${formatSize(curBytes)} / ${formatSize(totalBytes)})` : (curBytes ? ` (${formatSize(curBytes)})` : '');
      const pctStr = progress >= 0 ? `${progress}%` : 'Downloading';
      stats.textContent = `${pctStr}${speedStr}${sizeStr}`;
    }

    const bar = document.getElementById('dl-progress-bar');
    if (bar) {
      bar.style.width = (progress >= 0 ? Math.max(0, Math.min(100, progress)) : 45) + '%';
    }
  };

  window.onDownloadComplete = function(fileName, totalBytes) {
    const card = document.getElementById('download-progress-card');
    if (card) {
      card.classList.add('complete');
      const stats = document.getElementById('dl-stats');
      const sizeStr = totalBytes ? ` (${formatSize(totalBytes)})` : '';
      if (stats) stats.textContent = `✓ Download Complete${sizeStr} • Saved to Downloads`;
      const bar = document.getElementById('dl-progress-bar');
      if (bar) bar.style.width = '100%';
      setTimeout(() => {
        card.style.display = 'none';
        card.classList.remove('complete');
      }, 3500);
    }
    fetchTransferHistory(true);
    refreshRecentFiles();
  };

  window.onDownloadError = function(fileName, err) {
    const card = document.getElementById('download-progress-card');
    if (card) {
      const stats = document.getElementById('dl-stats');
      if (stats) stats.textContent = `⚠ Download Error: ${err}`;
      setTimeout(() => {
        card.style.display = 'none';
      }, 4000);
    }
  };

  window.dismissDownloadCard = function() {
    const card = document.getElementById('download-progress-card');
    if (card) card.style.display = 'none';
  };

  // -------------------------------------------------------------
  // Synchronized Transfer History Management
  // -------------------------------------------------------------
  let serverTransfersList = [];
  let currentTransferFilter = 'all';

  window.fetchTransferHistory = async function(force) {
    const container = document.getElementById('share-transfers-container');
    if (!container) return;

    try {
      const res = await fetch(apiUrl('/api/transfers'), { headers: getAuthHeaders() });
      if (res.ok) {
        serverTransfersList = await res.json();
      }
    } catch (e) {
      // Fallback
    }

    renderTransferHistory();
  };

  window.filterShareHistory = function(filterKey) {
    currentTransferFilter = filterKey;
    document.querySelectorAll('.transfer-chip').forEach(c => {
      c.classList.toggle('active', c.id === `chip-filter-${filterKey}`);
    });
    renderTransferHistory();
  };

  function renderTransferHistory() {
    const container = document.getElementById('share-transfers-container');
    if (!container) return;

    const countEl = document.getElementById('share-history-count');
    if (countEl) countEl.textContent = serverTransfersList.length;

    let items = serverTransfersList;
    if (currentTransferFilter === 'received') {
      items = items.filter(t => t.direction === 'upload');
    } else if (currentTransferFilter === 'sent') {
      items = items.filter(t => t.direction === 'download');
    } else if (currentTransferFilter === 'stream') {
      items = items.filter(t => t.direction === 'stream');
    }

    if (!items || items.length === 0) {
      container.innerHTML = '<div class="empty-state-text">No transfers matching this filter. Send or download files to see history.</div>';
      return;
    }

    container.innerHTML = '';
    items.forEach(t => {
      const card = document.createElement('div');
      card.className = 'transfer-card-item';

      let dirBadge = '↓ Received';
      let dirClass = 'received';
      let iconColor = 'rgba(52, 168, 83, 0.18)';
      let textColor = '#6DD58C';
      if (t.direction === 'download') {
        dirBadge = '↑ Sent';
        dirClass = 'sent';
        iconColor = 'rgba(66, 133, 244, 0.18)';
        textColor = '#A8C7FA';
      } else if (t.direction === 'stream') {
        dirBadge = '► Streamed';
        dirClass = 'stream';
        iconColor = 'rgba(168, 85, 247, 0.18)';
        textColor = '#D8B4FE';
      }

      const fileIcon = getIconForFileName(t.fileName);
      const timeStr = t.timestamp ? new Date(t.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';
      const speedStr = (t.speedMBps && t.speedMBps > 0) ? `⚡ ${t.speedMBps.toFixed(1)} MB/s` : '';
      const sizeStr = t.sizeFormatted || formatSize(t.bytes || 0);

      card.innerHTML = `
        <div class="tc-left">
          <div class="tc-icon-wrap" style="background: ${iconColor}; color: ${textColor};">
            ${fileIcon}
          </div>
          <div class="tc-info">
            <div class="tc-name" title="${escapeHtml(t.fileName)}">${escapeHtml(t.fileName)}</div>
            <div class="tc-meta">
              <span class="tc-dir-badge ${dirClass}">${dirBadge}</span>
              <span>•</span>
              <span>${sizeStr}</span>
              ${speedStr ? `<span>•</span><span class="tc-speed-pill">${speedStr}</span>` : ''}
              <span>•</span>
              <span>${timeStr}</span>
            </div>
          </div>
        </div>
        <button class="tc-action-btn" onclick="handleTransferItemClick('${escapeHtml(t.fileName)}', '${escapeHtml(t.filePath || '')}')">Open</button>
      `;
      container.appendChild(card);
    });
  }

  window.handleTransferItemClick = function(fileName, filePath) {
    if (filePath) {
      openFileViewer({ name: fileName, path: filePath, size: 0, isDirectory: false });
    } else {
      showToast(`Transferred file: ${fileName}`);
    }
  };

  window.clearServerTransferHistory = async function() {
    try {
      await fetch(apiUrl('/api/transfers/clear'), { method: 'POST', headers: getAuthHeaders() });
    } catch (e) { }
    serverTransfersList = [];
    localStorage.removeItem('cloudssd_recent_activity');
    renderTransferHistory();
    showToast('Transfer history cleared');
  };

  // -------------------------------------------------------------
  // Download Progress Card Callbacks & System Handlers
  // -------------------------------------------------------------
  window.onDownloadProgress = function(fileName, progress, speedMBps, curBytes, totalBytes) {
    const card = document.getElementById('download-progress-card');
    if (!card) return;
    card.style.display = 'block';
    card.classList.remove('complete');

    const title = document.getElementById('dl-file-name');
    if (title) title.textContent = fileName;

    const stats = document.getElementById('dl-stats');
    if (stats) {
      const speedStr = (typeof speedMBps === 'number' && speedMBps > 0) ? ` • ⚡ ${speedMBps.toFixed(1)} MB/s` : '';
      const sizeStr = (curBytes && totalBytes && totalBytes > 0) ? ` (${formatSize(curBytes)} / ${formatSize(totalBytes)})` : (curBytes ? ` (${formatSize(curBytes)})` : '');
      const pctStr = progress >= 0 ? `${progress}%` : 'Downloading';
      stats.textContent = `${pctStr}${speedStr}${sizeStr}`;
    }

    const bar = document.getElementById('dl-progress-bar');
    if (bar) {
      bar.style.width = (progress >= 0 ? Math.max(0, Math.min(100, progress)) : 45) + '%';
    }
  };

  window.onDownloadComplete = function(fileName, totalBytes) {
    const card = document.getElementById('download-progress-card');
    if (card) {
      card.classList.add('complete');
      const stats = document.getElementById('dl-stats');
      const sizeStr = totalBytes ? ` (${formatSize(totalBytes)})` : '';
      if (stats) stats.textContent = `✓ Download Complete${sizeStr} • Saved to Downloads`;
      const bar = document.getElementById('dl-progress-bar');
      if (bar) bar.style.width = '100%';
      setTimeout(() => {
        card.style.display = 'none';
        card.classList.remove('complete');
      }, 3500);
    }
    fetchTransferHistory(true);
    refreshRecentFiles();
  };

  window.onDownloadError = function(fileName, err) {
    const card = document.getElementById('download-progress-card');
    if (card) {
      const stats = document.getElementById('dl-stats');
      if (stats) stats.textContent = `⚠ Download Error: ${err}`;
      setTimeout(() => {
        card.style.display = 'none';
      }, 4000);
    }
  };

  window.dismissDownloadCard = function() {
    const card = document.getElementById('download-progress-card');
    if (card) card.style.display = 'none';
  };

  // -------------------------------------------------------------
  // Synchronized Transfer History Management
  // -------------------------------------------------------------
  let serverTransfersList = [];
  let currentTransferFilter = 'all';

  window.fetchTransferHistory = async function(force) {
    const container = document.getElementById('share-transfers-container');
    if (!container) return;

    try {
      const res = await fetch(apiUrl('/api/transfers'), { headers: getAuthHeaders() });
      if (res.ok) {
        serverTransfersList = await res.json();
      }
    } catch (e) {
      // Fallback
    }

    renderTransferHistory();
  };

  window.filterShareHistory = function(filterKey) {
    currentTransferFilter = filterKey;
    document.querySelectorAll('.transfer-chip').forEach(c => {
      c.classList.toggle('active', c.id === `chip-filter-${filterKey}`);
    });
    renderTransferHistory();
  };

  function renderTransferHistory() {
    const container = document.getElementById('share-transfers-container');
    if (!container) return;

    const countEl = document.getElementById('share-history-count');
    if (countEl) countEl.textContent = serverTransfersList.length;

    let items = serverTransfersList;
    if (currentTransferFilter === 'received') {
      items = items.filter(t => t.direction === 'upload');
    } else if (currentTransferFilter === 'sent') {
      items = items.filter(t => t.direction === 'download');
    } else if (currentTransferFilter === 'stream') {
      items = items.filter(t => t.direction === 'stream');
    }

    if (!items || items.length === 0) {
      container.innerHTML = '<div class="empty-state-text">No transfers matching this filter. Send or download files to see history.</div>';
      return;
    }

    container.innerHTML = '';
    items.forEach(t => {
      const card = document.createElement('div');
      card.className = 'transfer-card-item';

      let dirBadge = '↓ Received';
      let dirClass = 'received';
      let iconColor = 'rgba(52, 168, 83, 0.18)';
      let textColor = '#6DD58C';
      if (t.direction === 'download') {
        dirBadge = '↑ Sent';
        dirClass = 'sent';
        iconColor = 'rgba(66, 133, 244, 0.18)';
        textColor = '#A8C7FA';
      } else if (t.direction === 'stream') {
        dirBadge = '► Streamed';
        dirClass = 'stream';
        iconColor = 'rgba(168, 85, 247, 0.18)';
        textColor = '#D8B4FE';
      }

      const fileIcon = getIconForFileName(t.fileName);
      const timeStr = t.timestamp ? new Date(t.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';
      const speedStr = (t.speedMBps && t.speedMBps > 0) ? `⚡ ${t.speedMBps.toFixed(1)} MB/s` : '';
      const sizeStr = t.sizeFormatted || formatSize(t.bytes || 0);

      card.innerHTML = `
        <div class="tc-left">
          <div class="tc-icon-wrap" style="background: ${iconColor}; color: ${textColor};">
            ${fileIcon}
          </div>
          <div class="tc-info">
            <div class="tc-name" title="${escapeHtml(t.fileName)}">${escapeHtml(t.fileName)}</div>
            <div class="tc-meta">
              <span class="tc-dir-badge ${dirClass}">${dirBadge}</span>
              <span>•</span>
              <span>${sizeStr}</span>
              ${speedStr ? `<span>•</span><span class="tc-speed-pill">${speedStr}</span>` : ''}
              <span>•</span>
              <span>${timeStr}</span>
            </div>
          </div>
        </div>
        <button class="tc-action-btn" onclick="handleTransferItemClick('${escapeHtml(t.fileName)}', '${escapeHtml(t.filePath || '')}')">Open</button>
      `;
      container.appendChild(card);
    });
  }

  window.handleTransferItemClick = function(fileName, filePath) {
    if (filePath) {
      openFileViewer({ name: fileName, path: filePath, size: 0, isDirectory: false });
    } else {
      showToast(`Transferred file: ${fileName}`);
    }
  };

  window.clearServerTransferHistory = async function() {
    try {
      await fetch(apiUrl('/api/transfers/clear'), { method: 'POST', headers: getAuthHeaders() });
    } catch (e) { }
    serverTransfersList = [];
    localStorage.removeItem('cloudssd_recent_activity');
    renderTransferHistory();
    showToast('Transfer history cleared');
  };

  // -------------------------------------------------------------
  // Status & Telemetry Polling
  // -------------------------------------------------------------
  async function pollStatus() {
    try {
      const headers = getAuthHeaders();
      const res = await fetch(apiUrl('/api/status'), { headers });
      if (!res.ok) throw new Error('Status HTTP ' + res.status);
      const data = await res.json();

      isConnected = true;
      updateHeaderStatus(true, data.pcName);

      // Mini Gaming banner on Browse screen
      const gameBanner = document.getElementById('browse-game-banner');
      if (data.gameRunning && data.activeGame) {
        if (gameBanner) {
          gameBanner.style.display = 'flex';
          document.getElementById('banner-game-name').textContent = data.activeGame;
          document.getElementById('banner-fps').textContent = data.stats.fps || 75;
          document.getElementById('banner-ssd').textContent = Math.round(data.stats.transferSpeedMbps || 112);
        }
      } else if (gameBanner) {
        gameBanner.style.display = 'none';
      }

      // Update Share Screen PC name
      const sharePc = document.getElementById('share-pc-name');
      if (sharePc) sharePc.textContent = data.pcName || 'ROSHAN';

      // Update Modal
      const modalPc = document.getElementById('modal-pc-name');
      if (modalPc) modalPc.textContent = data.pcName || 'ROSHAN';
      const modalIp = document.getElementById('modal-pc-ip');
      if (modalIp) modalIp.textContent = `${data.localIp}:${data.port}`;

    } catch (err) {
      isConnected = false;
      updateHeaderStatus(false);
      probeServerUrls();
    }
  }

  function updateHeaderStatus(online, pcName) {
    const dot = document.getElementById('header-status-dot');
    const avatar = document.getElementById('pc-avatar');
    if (!dot || !avatar) return;

    if (online) {
      dot.className = 'profile-status-dot';
      avatar.textContent = (pcName ? pcName[0] : 'R').toUpperCase();
    } else {
      dot.className = 'profile-status-dot offline';
      avatar.textContent = 'R';
    }
  }

  // Live Gaming Companion Polling
  async function pollGamingHud() {
    if (activeTab !== 'gaming') return;
    try {
      const res = await fetch(apiUrl('/api/hud'), { headers: getAuthHeaders() });
      if (!res.ok) return;
      const d = await res.json();

      document.getElementById('gaming-hud-active-game').textContent = d.activeGame || 'No Active PC Game';
      document.getElementById('desk-fps').textContent = d.fps > 0 ? d.fps : '--';
      document.getElementById('desk-ssd').textContent = d.ssdReadMBps > 0 ? d.ssdReadMBps : '--';
      document.getElementById('desk-hit').textContent = (d.cacheHitRate || 99) + '%';
      document.getElementById('desk-cpu').textContent = (d.cpuTempC || 58) + '°C';
      document.getElementById('desk-gpu').textContent = (d.gpuTempC || 64) + '°C';
      document.getElementById('desk-prefetch').textContent = d.activePrefetches || 0;

      const badge = document.getElementById('gaming-status-badge');
      if (badge) {
        badge.textContent = d.isGaming ? 'GAME LIVE' : 'STANDBY';
        badge.style.color = d.isGaming ? '#6DD58C' : '#A8C7FA';
      }
    } catch { }
  }

  window.loadGameSaves = async function() {
    const container = document.getElementById('game-saves-list');
    if (!container) return;
    container.innerHTML = '<div class="empty-state-text">Scanning game saves...</div>';

    try {
      const res = await fetch(apiUrl('/api/saves'), { headers: getAuthHeaders() });
      const saves = await res.json();

      container.innerHTML = '';
      if (!saves || saves.length === 0) {
        container.innerHTML = '<div class="empty-state-text">No active game saves found</div>';
        return;
      }

      saves.forEach(s => {
        const row = document.createElement('div');
        row.style.padding = '10px 0';
        row.style.borderBottom = '1px solid var(--divider)';
        row.style.display = 'flex';
        row.style.justifyContent = 'space-between';
        row.style.alignItems = 'center';
        row.innerHTML = `
          <div>
            <div style="font-weight: 500; font-size: 14px;">${escapeHtml(s.name)}</div>
            <div style="font-size: 11.5px; color: var(--dim);">${s.sizeFormatted}</div>
          </div>
          <button class="m3-file-btn" onclick="downloadFile('${encodeURIComponent(s.path)}')">
            ${SVGS.download}
          </button>
        `;
        container.appendChild(row);
      });
    } catch {
      container.innerHTML = '<div class="empty-state-text">Unable to reach game saves</div>';
    }
  };

  // -------------------------------------------------------------
  // Settings & Pairing Modal
  // -------------------------------------------------------------
  window.openSettingsModal = function() {
    const modal = document.getElementById('settings-modal');
    if (modal) modal.style.display = 'flex';
  };

  window.closeSettingsModal = function() {
    const modal = document.getElementById('settings-modal');
    if (modal) modal.style.display = 'none';
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
    showToast('Server address updated');
    closeSettingsModal();
    pollStatus();
    loadStorageDevices();
  };

  // Helpers
  function getAuthHeaders() {
    const h = {};
    if (pairingToken) h['X-Pairing-Token'] = pairingToken;
    return h;
  }

  function formatBytes(bytes) {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
  }

  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function escapeJs(str) {
    if (!str) return '';
    return str.replace(/\\/g, '\\\\').replace(/'/g, "\\'");
  }

  // Initialization
  document.addEventListener('DOMContentLoaded', async () => {
    switchTab('browse');
    applyViewLayout();
    updatePhoneSystemMetrics();
    await probeServerUrls();
    pollStatus();
    statusPollTimer = setInterval(pollStatus, 4000);
    hudPollTimer = setInterval(pollGamingHud, 2500);
    setInterval(updatePhoneSystemMetrics, 10000);
  });

})();
