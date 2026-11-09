// Cloud-SSD Mobile Client Application
(function() {
  'use strict';

  // State Management
  let deferredPrompt = null;
  let activeTab = 'home';
  let pairingToken = localStorage.getItem('cloudssd_token') || '';
  let deviceName = localStorage.getItem('cloudssd_devicename') || (navigator.userAgent.includes('Android') ? 'Android Device' : 'Mobile Client');
  let currentBrowsePath = '';
  let statusPollTimer = null;
  let hudPollTimer = null;
  let isConnected = false;

  // Base API URL resolver (works for hosted HTTP, local reverse, and offline APK)
  window.getApiBase = function() {
    const saved = localStorage.getItem('cloudssd_server_url');
    if (saved) return saved.replace(/\/+$/, '');
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

  // Check URL parameters for instant pairing via QR Code: ?token=XYZ&name=MyPhone
  const urlParams = new URLSearchParams(window.location.search);
  if (urlParams.has('token')) {
    pairingToken = urlParams.get('token');
    localStorage.setItem('cloudssd_token', pairingToken);
    if (urlParams.has('name')) {
      deviceName = urlParams.get('name');
      localStorage.setItem('cloudssd_devicename', deviceName);
    }
  }

  // Register PWA Service Worker
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('./sw.js').catch(err => {
      console.warn('Service Worker registration skipped:', err);
    });
  }

  // PWA Install Prompt Capture
  window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault();
    deferredPrompt = e;
    const installBtn = document.getElementById('pwa-install-btn');
    if (installBtn) installBtn.style.display = 'block';
  });

  // Tab Navigation
  window.switchTab = function(tabName, extra) {
    activeTab = tabName;
    document.querySelectorAll('.tab-screen').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));

    const targetScreen = document.getElementById('tab-' + tabName);
    if (targetScreen) targetScreen.classList.add('active');

    // Update bottom nav active state
    const navItems = document.querySelectorAll('.nav-item');
    const tabIndex = ['home', 'files', 'gaming', 'vault', 'settings'].indexOf(tabName);
    if (tabIndex >= 0 && navItems[tabIndex]) navItems[tabIndex].classList.add('active');

    if (tabName === 'files') {
      if (extra === 'drives') loadDrives();
      else if (!currentBrowsePath) loadDrives();
      else loadFolder(currentBrowsePath);
    } else if (tabName === 'gaming') {
      loadGameSaves();
    } else if (tabName === 'vault') {
      renderVaultFiles();
    } else if (tabName === 'settings') {
      updateSettingsDisplay();
    }
  };

  // Toast Notification
  window.showToast = function(msg) {
    const toast = document.getElementById('toast');
    if (!toast) return;
    toast.textContent = msg;
    toast.classList.add('show');
    setTimeout(() => toast.classList.remove('show'), 2800);
  };

  // PWA Install Action
  window.installPwa = function() {
    if (deferredPrompt) {
      deferredPrompt.prompt();
      deferredPrompt.userChoice.then(() => { deferredPrompt = null; });
    } else {
      showToast('Chrome menu (⋮) par tap karke "Install App" select karein.');
    }
  };

  // Status & Health Polling
  async function pollStatus() {
    try {
      const headers = {};
      if (pairingToken) headers['X-Pairing-Token'] = pairingToken;

      const res = await fetch(apiUrl('/api/status'), { headers });
      if (!res.ok) throw new Error('Status HTTP ' + res.status);

      const data = await res.json();
      isConnected = true;
      updateConnectionPill(true, data.pcName);

      // Update Home Storage & PC name
      document.getElementById('pc-name-display').textContent = data.pcName || 'PC';
      document.getElementById('stat-drives-count').textContent = (data.drivesCount || 0) + ' Drives Mounted';
      document.getElementById('stat-prefetch-count').textContent = data.stats.activePrefetches > 0
        ? `Prefetching (${data.stats.activePrefetches})` : 'SSD Cache Ready';

      if (data.stats.transferSpeedMbps > 0) {
        document.getElementById('transfer-speed-summary').textContent = data.stats.transferSpeedMbps + ' MB/s';
      } else {
        document.getElementById('transfer-speed-summary').textContent = 'Local Wi-Fi 0 Data';
      }

      document.getElementById('storage-summary').textContent = data.isPaired ? 'Paired & Connected' : 'Requires Pairing';

      // Update Home HUD Card if game running
      const homeHud = document.getElementById('home-hud-card');
      if (data.gameRunning && data.activeGame) {
        homeHud.style.display = 'block';
        document.getElementById('hud-active-game').textContent = data.activeGame;
        document.getElementById('hud-fps').textContent = data.stats.fps || 75;
        document.getElementById('hud-ssd').textContent = Math.round(data.stats.transferSpeedMbps || 112);
        document.getElementById('hud-hit').textContent = (data.stats.cacheHitRate || 99) + '%';
      } else {
        homeHud.style.display = 'none';
      }

      // Settings screen details
      document.getElementById('settings-pc-name').textContent = data.pcName || '--';
      document.getElementById('settings-local-ip').textContent = data.localIp ? `${data.localIp}:${data.port}` : '--';
      document.getElementById('settings-pair-status').textContent = data.isPaired ? 'Paired & Trusted ✓' : 'Pairing Required';

    } catch (err) {
      isConnected = false;
      updateConnectionPill(false);
    }
  }

  function updateConnectionPill(online, pcName) {
    const pill = document.getElementById('connection-pill');
    const text = document.getElementById('connection-text');
    if (!pill || !text) return;

    if (online) {
      pill.className = 'status-pill online';
      text.textContent = pcName ? `PC: ${pcName}` : 'Local Connected';
    } else {
      pill.className = 'status-pill offline';
      text.textContent = 'Offline Vault Mode';
    }
  }

  // Live Desk Gaming HUD Polling
  async function pollGamingHud() {
    if (activeTab !== 'gaming' && activeTab !== 'home') return;
    try {
      const headers = {};
      if (pairingToken) headers['X-Pairing-Token'] = pairingToken;

      const res = await fetch(apiUrl('/api/hud'), { headers });
      if (!res.ok) return;
      const d = await res.json();

      document.getElementById('gaming-active-game').textContent = d.activeGame || 'Standby';
      document.getElementById('desk-fps').textContent = d.fps > 0 ? d.fps : '--';
      document.getElementById('desk-ssd').textContent = d.ssdReadMBps > 0 ? d.ssdReadMBps : '--';
      document.getElementById('desk-hit').textContent = d.cacheHitRate + '%';
      document.getElementById('desk-cpu').textContent = d.cpuTempC + '°C';
      document.getElementById('desk-gpu').textContent = d.gpuTempC + '°C';
      document.getElementById('desk-prefetch').textContent = d.activePrefetches;

      const pill = document.getElementById('hud-mode-pill');
      if (d.isGaming) {
        pill.textContent = 'GAME LIVE';
        pill.style.borderColor = 'var(--ok)';
        pill.style.color = 'var(--ok)';
      } else {
        pill.textContent = 'STANDBY';
        pill.style.borderColor = 'var(--dim)';
        pill.style.color = 'var(--dim)';
      }
    } catch { }
  }

  // File Explorer: Drives & Folders
  window.loadDrives = async function() {
    currentBrowsePath = '';
    const breadcrumb = document.getElementById('breadcrumb-bar');
    breadcrumb.innerHTML = '<span class="breadcrumb-item active">🏠 Cloud-SSD</span>';

    const drivesBox = document.getElementById('drives-container');
    const drivesList = document.getElementById('drives-list');
    const filesList = document.getElementById('files-list-container');
    filesList.innerHTML = '';
    drivesBox.style.display = 'block';
    drivesList.innerHTML = '<div style="font-size:12px; color:var(--dim);">Loading drives...</div>';

    try {
      const headers = {};
      if (pairingToken) headers['X-Pairing-Token'] = pairingToken;

      const res = await fetch(apiUrl('/api/drives'), { headers });
      const drives = await res.json();
      drivesList.innerHTML = '';

      if (!drives || drives.length === 0) {
        drivesList.innerHTML = '<div style="font-size:12.5px; color:var(--dim);">No virtual drives currently mounted on PC.</div>';
        return;
      }

      drives.forEach(d => {
        const item = document.createElement('div');
        item.className = 'file-item';
        item.innerHTML = `
          <div class="file-info">
            <div class="file-icon">${d.isCloud ? '☁️' : '🎮'}</div>
            <div class="file-meta">
              <div class="file-name">${d.displayName} (${d.driveLetter}:)</div>
              <div class="file-size">${d.remoteSpec} · ${d.state}</div>
            </div>
          </div>
          <div class="file-actions">
            <span style="font-size: 18px; color: var(--dim);">➔</span>
          </div>
        `;
        item.onclick = () => loadFolder(d.root);
        drivesList.appendChild(item);
      });
    } catch (err) {
      drivesList.innerHTML = '<div style="font-size:12px; color:var(--warn);">PC is currently offline. Viewing Offline Vault.</div>';
    }
  };

  window.loadFolder = async function(folderPath) {
    currentBrowsePath = folderPath;
    document.getElementById('drives-container').style.display = 'none';

    // Update Breadcrumbs
    const breadcrumb = document.getElementById('breadcrumb-bar');
    breadcrumb.innerHTML = '';
    const rootSpan = document.createElement('span');
    rootSpan.className = 'breadcrumb-item';
    rootSpan.textContent = '🏠 Drives';
    rootSpan.onclick = () => loadDrives();
    breadcrumb.appendChild(rootSpan);

    const separator = document.createElement('span');
    separator.style.color = 'var(--dim)';
    separator.textContent = ' > ';
    breadcrumb.appendChild(separator);

    const currentSpan = document.createElement('span');
    currentSpan.className = 'breadcrumb-item active';
    currentSpan.textContent = folderPath.split('\\').filter(Boolean).pop() || folderPath;
    breadcrumb.appendChild(currentSpan);

    const filesList = document.getElementById('files-list-container');
    filesList.innerHTML = '<div style="font-size:12px; color:var(--dim); padding:10px;">Loading files...</div>';

    try {
      const headers = {};
      if (pairingToken) headers['X-Pairing-Token'] = pairingToken;

      const res = await fetch(apiUrl(`/api/files?path=${encodeURIComponent(folderPath)}`), { headers });
      const data = await res.json();
      filesList.innerHTML = '';

      // Back to parent button if available
      if (data.parentPath) {
        const parentBtn = document.createElement('div');
        parentBtn.className = 'file-item';
        parentBtn.innerHTML = `
          <div class="file-info">
            <div class="file-icon">📁</div>
            <div class="file-meta">
              <div class="file-name">.. (Parent Folder)</div>
            </div>
          </div>
        `;
        parentBtn.onclick = () => loadFolder(data.parentPath);
        filesList.appendChild(parentBtn);
      }

      // Render Subdirectories
      if (data.folders) {
        data.folders.forEach(f => {
          const item = document.createElement('div');
          item.className = 'file-item';
          item.innerHTML = `
            <div class="file-info">
              <div class="file-icon">📁</div>
              <div class="file-meta">
                <div class="file-name">${f.name}</div>
                <div class="file-size">Directory</div>
              </div>
            </div>
            <div class="file-actions">
              <span style="font-size: 16px; color: var(--dim);">➔</span>
            </div>
          `;
          item.onclick = () => loadFolder(f.path);
          filesList.appendChild(item);
        });
      }

      // Render Files
      if (data.files && data.files.length > 0) {
        data.files.forEach(file => {
          const item = document.createElement('div');
          item.className = 'file-item';

          let icon = '📄';
          if (file.isVideo) icon = '🎬';
          else if (file.isAudio) icon = '🎵';
          else if (file.isSave) icon = '🎮';
          else if (file.extension === '.zip' || file.extension === '.rar') icon = '📦';

          item.innerHTML = `
            <div class="file-info" style="flex: 1;">
              <div class="file-icon">${icon}</div>
              <div class="file-meta">
                <div class="file-name">${file.name}</div>
                <div class="file-size">${file.sizeFormatted}</div>
              </div>
            </div>
            <div class="file-actions">
              ${file.isVideo ? `<button class="icon-btn" title="Stream Video" onclick="event.stopPropagation(); streamVideo('${encodeURIComponent(file.path)}', '${file.name}')">▶️</button>` : ''}
              <button class="icon-btn" title="Download" onclick="event.stopPropagation(); downloadFile('${encodeURIComponent(file.path)}')">⬇️</button>
              <button class="icon-btn" title="Save to Offline Vault" onclick="event.stopPropagation(); saveToVault('${encodeURIComponent(file.path)}', '${file.name}', ${file.size})">💾</button>
            </div>
          `;

          if (file.isVideo) {
            item.onclick = () => streamVideo(encodeURIComponent(file.path), file.name);
          } else {
            item.onclick = () => downloadFile(encodeURIComponent(file.path));
          }

          filesList.appendChild(item);
        });
      } else if (!data.folders || data.folders.length === 0) {
        filesList.innerHTML += '<div style="font-size:12.5px; color:var(--dim); padding:10px;">This folder is empty.</div>';
      }

    } catch (err) {
      filesList.innerHTML = '<div style="font-size:12px; color:var(--warn); padding:10px;">Could not load files from PC. Check connection.</div>';
    }
  };

  // Video Streaming Modal
  window.streamVideo = function(encodedPath, fileName) {
    const modal = document.getElementById('video-modal');
    const title = document.getElementById('video-modal-title');
    const player = document.getElementById('active-video-player');

    title.textContent = fileName || '4K/1080p Local Video Stream';
    let url = apiUrl(`/api/stream?path=${encodedPath}`);
    if (pairingToken) url += `&token=${encodeURIComponent(pairingToken)}`;

    player.src = url;
    modal.classList.add('active');
    player.play().catch(() => {});
    addRecentActivity(`Streamed: ${fileName}`);
  };

  window.closeVideoModal = function() {
    const modal = document.getElementById('video-modal');
    const player = document.getElementById('active-video-player');
    player.pause();
    player.src = '';
    modal.classList.remove('active');
  };

  // File Download
  window.downloadFile = function(encodedPath) {
    let url = apiUrl(`/api/download?path=${encodedPath}`);
    if (pairingToken) url += `&token=${encodeURIComponent(pairingToken)}`;
    window.open(url, '_blank');
    showToast('Download started');
  };

  // File Upload
  window.triggerFileUpload = function() {
    const uploader = document.getElementById('file-uploader');
    if (uploader) uploader.click();
  };

  window.openPhotoPicker = function() {
    const picker = document.getElementById('photo-uploader');
    if (picker) picker.click();
  };

  window.handleFileSelection = async function(event) {
    const files = event.target.files;
    if (!files || files.length === 0) return;

    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      showToast(`Uploading ${file.name}...`);

      try {
        let url = apiUrl(`/api/upload?filename=${encodeURIComponent(file.name)}`);
        if (pairingToken) url += `&token=${encodeURIComponent(pairingToken)}`;

        const res = await fetch(url, {
          method: 'POST',
          headers: { 'Content-Length': file.size },
          body: file
        });

        if (res.ok) {
          showToast(`✓ ${file.name} uploaded to PC`);
          addRecentActivity(`Sent to PC: ${file.name}`);
        } else {
          showToast(`Upload failed: ${file.name}`);
        }
      } catch (err) {
        showToast(`Upload error: ${err.message}`);
      }
    }
  };

  // IndexedDB Phone Offline Vault
  const DB_NAME = 'CloudSSDMobileDB';
  const STORE_NAME = 'offline_vault';

  function openVaultDb() {
    return new Promise((resolve, reject) => {
      const req = indexedDB.open(DB_NAME, 1);
      req.onupgradeneeded = (e) => {
        const db = e.target.result;
        if (!db.objectStoreNames.contains(STORE_NAME)) {
          db.createObjectStore(STORE_NAME, { keyPath: 'id', autoIncrement: true });
        }
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  }

  window.saveToVault = async function(encodedPath, fileName, fileSize) {
    showToast(`Saving ${fileName} to Vault...`);
    try {
      let url = apiUrl(`/api/download?path=${encodedPath}`);
      if (pairingToken) url += `&token=${encodeURIComponent(pairingToken)}`;

      const res = await fetch(url);
      const blob = await res.blob();

      const db = await openVaultDb();
      const tx = db.transaction(STORE_NAME, 'readwrite');
      const store = tx.objectStore(STORE_NAME);

      store.add({
        name: fileName,
        size: blob.size,
        sizeFormatted: (blob.size / (1024 * 1024)).toFixed(1) + ' MB',
        type: blob.type,
        data: blob,
        savedAt: new Date().toISOString()
      });

      tx.oncomplete = () => {
        showToast(`✓ ${fileName} saved to Offline Vault`);
        renderVaultFiles();
      };
    } catch (err) {
      showToast(`Vault save failed: ${err.message}`);
    }
  };

  async function renderVaultFiles() {
    const list = document.getElementById('vault-files-list');
    const usedSizeEl = document.getElementById('vault-used-size');
    if (!list) return;

    try {
      const db = await openVaultDb();
      const tx = db.transaction(STORE_NAME, 'readonly');
      const store = tx.objectStore(STORE_NAME);
      const req = store.getAll();

      req.onsuccess = () => {
        const items = req.result || [];
        list.innerHTML = '';

        if (items.length === 0) {
          list.innerHTML = '<div style="font-size:12.5px; color:var(--dim); padding:8px;">No offline files saved yet.</div>';
          if (usedSizeEl) usedSizeEl.textContent = '0 MB';
          return;
        }

        let totalBytes = 0;
        items.forEach(item => {
          totalBytes += item.size;
          const el = document.createElement('div');
          el.className = 'file-item';
          el.innerHTML = `
            <div class="file-info" style="flex: 1;">
              <div class="file-icon">💾</div>
              <div class="file-meta">
                <div class="file-name">${item.name}</div>
                <div class="file-size">${item.sizeFormatted} · Saved Offline</div>
              </div>
            </div>
            <div class="file-actions">
              <button class="icon-btn" title="Open" onclick="openVaultItem(${item.id})">📂</button>
              <button class="icon-btn" title="Delete" onclick="deleteVaultItem(${item.id})">🗑️</button>
            </div>
          `;
          list.appendChild(el);
        });

        if (usedSizeEl) {
          usedSizeEl.textContent = (totalBytes / (1024 * 1024)).toFixed(1) + ' MB';
        }
      };
    } catch {
      list.innerHTML = '<div style="font-size:12px; color:var(--warn);">Could not access local Vault storage.</div>';
    }
  }

  window.openVaultItem = async function(id) {
    const db = await openVaultDb();
    const tx = db.transaction(STORE_NAME, 'readonly');
    const store = tx.objectStore(STORE_NAME);
    const req = store.get(id);

    req.onsuccess = () => {
      const item = req.result;
      if (!item) return;

      const blobUrl = URL.createObjectURL(item.data);
      if (item.data.type.startsWith('video/')) {
        const modal = document.getElementById('video-modal');
        const player = document.getElementById('active-video-player');
        document.getElementById('video-modal-title').textContent = item.name;
        player.src = blobUrl;
        modal.classList.add('active');
        player.play().catch(() => {});
      } else {
        const a = document.createElement('a');
        a.href = blobUrl;
        a.download = item.name;
        a.click();
      }
    };
  };

  window.deleteVaultItem = async function(id) {
    const db = await openVaultDb();
    const tx = db.transaction(STORE_NAME, 'readwrite');
    tx.objectStore(STORE_NAME).delete(id);
    tx.oncomplete = () => {
      showToast('Item deleted from Vault');
      renderVaultFiles();
    };
  };

  window.clearVault = async function() {
    if (!confirm('Are you sure you want to clear all offline vault files?')) return;
    const db = await openVaultDb();
    const tx = db.transaction(STORE_NAME, 'readwrite');
    tx.objectStore(STORE_NAME).clear();
    tx.oncomplete = () => {
      showToast('Offline Vault cleared');
      renderVaultFiles();
    };
  };

  // Game Saves
  window.loadGameSaves = async function() {
    const savesList = document.getElementById('game-saves-list');
    if (!savesList) return;
    savesList.innerHTML = '<div style="font-size:12px; color:var(--dim);">Loading game saves...</div>';

    try {
      const headers = {};
      if (pairingToken) headers['X-Pairing-Token'] = pairingToken;

      const res = await fetch(apiUrl('/api/saves'), { headers });
      const saves = await res.json();
      savesList.innerHTML = '';

      if (!saves || saves.length === 0) {
        savesList.innerHTML = '<div style="font-size:12.5px; color:var(--dim); padding:6px;">No game save backups found in Cloud-SSD yet.</div>';
        return;
      }

      saves.forEach(s => {
        const el = document.createElement('div');
        el.className = 'file-item';
        el.innerHTML = `
          <div class="file-info" style="flex: 1;">
            <div class="file-icon">🎮</div>
            <div class="file-meta">
              <div class="file-name">${s.name}</div>
              <div class="file-size">${s.sizeFormatted}</div>
            </div>
          </div>
          <div class="file-actions">
            <button class="icon-btn" title="Download Save" onclick="downloadFile('${encodeURIComponent(s.path)}')">⬇️</button>
            <button class="icon-btn" title="Save Offline" onclick="saveToVault('${encodeURIComponent(s.path)}', '${s.name}', ${s.size})">💾</button>
          </div>
        `;
        savesList.appendChild(el);
      });
    } catch {
      savesList.innerHTML = '<div style="font-size:12px; color:var(--dim);">PC is offline. Check Offline Vault for cached saves.</div>';
    }
  };

  // Pairing Modal & Auth
  window.openPairModal = function() {
    document.getElementById('input-token').value = pairingToken;
    document.getElementById('input-device-name').value = deviceName;
    document.getElementById('pair-modal').classList.add('active');
  };

  window.closePairModal = function() {
    document.getElementById('pair-modal').classList.remove('active');
  };

  window.submitPairing = async function() {
    const t = document.getElementById('input-token').value.trim();
    const n = document.getElementById('input-device-name').value.trim() || 'Android Device';

    if (!t) {
      showToast('Please enter pairing token');
      return;
    }

    try {
      const res = await fetch(apiUrl(`/api/pair?token=${encodeURIComponent(t)}&name=${encodeURIComponent(n)}`), {
        method: 'POST'
      });
      const data = await res.json();

      if (data.success) {
        pairingToken = t;
        deviceName = n;
        localStorage.setItem('cloudssd_token', t);
        localStorage.setItem('cloudssd_devicename', n);
        closePairModal();
        showToast('✓ Successfully paired with PC!');
        pollStatus();
      } else {
        showToast('Pairing failed: Invalid token');
      }
    } catch (err) {
      showToast('Error connecting to PC');
    }
  };

  // Recent Activity Helper
  function addRecentActivity(msg) {
    const container = document.getElementById('recent-transfers-list');
    if (!container) return;
    const time = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    const line = document.createElement('div');
    line.style.padding = '4px 0';
    line.innerHTML = `• ${msg} <span style="color:var(--dim); font-size:10px;">(${time})</span>`;

    if (container.children.length === 1 && container.children[0].textContent.includes('No recent')) {
      container.innerHTML = '';
    }
    container.prepend(line);
  }

  window.clearRecentActivity = function() {
    const container = document.getElementById('recent-transfers-list');
    if (container) container.innerHTML = '<div>No recent file transfers yet.</div>';
  };

  function updateSettingsDisplay() {
    document.getElementById('settings-pc-name').textContent = isConnected ? 'Connected PC' : 'Offline';
  }

  window.setLanguage = function(lang) {
    showToast(`Language set to ${lang}`);
  };

  // Initial Boot
  pollStatus();
  statusPollTimer = setInterval(pollStatus, 3000);
  hudPollTimer = setInterval(pollGamingHud, 1000);

})();
