// Cloud-SSD phone client.
// Runs inside the Android app (window.AndroidBridge is present) and, with fewer features, in a phone browser
// opened from the PC's QR code. Every number on screen comes from the phone or the PC; when a value is not
// known yet the UI shows "—", never a placeholder figure.
(function () {
  'use strict';

  const native = window.AndroidBridge || null;
  const isApp = !!native;
  const $ = (id) => document.getElementById(id);

  // ------------------------------------------------------------------ icons
  const SVG = {
    folder: '<svg viewBox="0 0 24 24"><path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/></svg>',
    doc: '<svg viewBox="0 0 24 24"><path d="M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z"/></svg>',
    pdf: '<svg viewBox="0 0 24 24"><path d="M20 2H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-8.5 7.5c0 .83-.67 1.5-1.5 1.5H9v2H7.5V7H10c.83 0 1.5.67 1.5 1.5v1zm5 2c0 .83-.67 1.5-1.5 1.5h-2.5V7H15c.83 0 1.5.67 1.5 1.5v3zm4-3H19v1h1.5V11H19v2h-1.5V7h3v1.5zM9 9.5h1v-1H9v1zm4.5 2H15v-3h-1.5v3z"/></svg>',
    image: '<svg viewBox="0 0 24 24"><path d="M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z"/></svg>',
    video: '<svg viewBox="0 0 24 24"><path d="M18 4l2 4h-3l-2-4h-2l2 4h-3l-2-4H8l2 4H7L5 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V4h-4z"/></svg>',
    audio: '<svg viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z"/></svg>',
    archive: '<svg viewBox="0 0 24 24"><path d="M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-6 10h-4v-2h4v2zm0-4h-4v-2h4v2zm0-4h-4V6h4v2z"/></svg>',
    apk: '<svg viewBox="0 0 24 24"><path d="M6 18c0 .55.45 1 1 1h1v3.5c0 .83.67 1.5 1.5 1.5s1.5-.67 1.5-1.5V19h2v3.5c0 .83.67 1.5 1.5 1.5s1.5-.67 1.5-1.5V19h1c.55 0 1-.45 1-1V8H6v10zM3.5 8C2.67 8 2 8.67 2 9.5v7c0 .83.67 1.5 1.5 1.5S5 17.33 5 16.5v-7C5 8.67 4.33 8 3.5 8zm17 0c-.83 0-1.5.67-1.5 1.5v7c0 .83.67 1.5 1.5 1.5s1.5-.67 1.5-1.5v-7c0-.83-.67-1.5-1.5-1.5zm-4.97-5.84l1.3-1.3c.2-.2.2-.51 0-.71-.2-.2-.51-.2-.71 0l-1.48 1.48C13.85 1.23 12.95 1 12 1c-.96 0-1.86.23-2.66.63L7.85.15c-.2-.2-.51-.2-.71 0-.2.2-.2.51 0 .71l1.31 1.31C6.97 3.26 6 5.01 6 7h12c0-1.99-.97-3.75-2.47-4.84zM10 5H9V4h1v1zm5 0h-1V4h1v1z"/></svg>',
    star: '<svg viewBox="0 0 24 24"><path d="M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z"/></svg>',
    more: '<svg viewBox="0 0 24 24"><path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z"/></svg>',
    download: '<svg viewBox="0 0 24 24"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>',
    upload: '<svg viewBox="0 0 24 24"><path d="M9 16h6v-6h4l-7-7-7 7h4zm-4 2h14v2H5z"/></svg>',
    play: '<svg viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg>',
    chevron: '<svg viewBox="0 0 24 24"><path d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z"/></svg>',
    close: '<svg viewBox="0 0 24 24"><path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/></svg>',
    check: '<svg viewBox="0 0 24 24"><path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"/></svg>',
    open: '<svg viewBox="0 0 24 24"><path d="M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z"/></svg>',
    pc: '<svg viewBox="0 0 24 24"><path d="M20 18c1.1 0 1.99-.9 1.99-2L22 6c0-1.1-.9-2-2-2H4c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2H0v2h24v-2h-4zM4 6h16v10H4V6z"/></svg>',
    share: '<svg viewBox="0 0 24 24"><path d="M18 16.08c-.76 0-1.44.3-1.96.77L8.91 12.7c.05-.23.09-.46.09-.7s-.04-.47-.09-.7l7.05-4.11c.54.5 1.25.81 2.04.81 1.66 0 3-1.34 3-3s-1.34-3-3-3-3 1.34-3 3c0 .24.04.47.09.7L8.04 9.81C7.5 9.31 6.79 9 6 9c-1.66 0-3 1.34-3 3s1.34 3 3 3c.79 0 1.5-.31 2.04-.81l7.12 4.16c-.05.21-.08.43-.08.65 0 1.61 1.31 2.92 2.92 2.92s2.92-1.31 2.92-2.92c0-1.61-1.31-2.92-2.92-2.92z"/></svg>',
    info: '<svg viewBox="0 0 24 24"><path d="M11 7h2v2h-2zm0 4h2v6h-2zm1-9C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z"/></svg>',
    cloud: '<svg viewBox="0 0 24 24"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z"/></svg>',
    stream: '<svg viewBox="0 0 24 24"><path d="M10 16.5l6-4.5-6-4.5v9zM12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z"/></svg>'
  };

  const EXT = {
    images: ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'heic', 'heif', 'dng', 'svg'],
    videos: ['mp4', 'mkv', 'webm', 'avi', 'mov', 'm4v', '3gp', 'ts', 'flv', 'wmv'],
    audio: ['mp3', 'wav', 'flac', 'aac', 'ogg', 'm4a', 'opus', 'amr', 'wma'],
    text: ['txt', 'log', 'json', 'xml', 'md', 'cs', 'java', 'js', 'html', 'css', 'py', 'ini', 'cfg', 'csv', 'yml', 'yaml', 'bat', 'ps1', 'sh'],
    archive: ['zip', 'rar', '7z', 'tar', 'gz', 'iso'],
    apk: ['apk', 'apks', 'xapk', 'apkm']
  };
  // Formats Android's WebView can really play; other videos are handed to a player app.
  const PLAYABLE_VIDEO = ['mp4', 'webm', 'm4v', 'mov', '3gp'];

  // ------------------------------------------------------------------ small helpers
  const ls = {
    get(k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
    set(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* private mode or full: the app still works, just forgets */ } },
    del(k) { try { localStorage.removeItem(k); } catch (e) { } },
    json(k, fallback) { try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : fallback; } catch (e) { return fallback; } }
  };

  function esc(s) {
    return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }
  function extOf(name) {
    const i = String(name || '').lastIndexOf('.');
    return i < 0 ? '' : String(name).slice(i + 1).toLowerCase();
  }
  function kindOf(name) {
    const e = extOf(name);
    for (const k of ['images', 'videos', 'audio', 'archive', 'apk', 'text']) if (EXT[k].includes(e)) return k;
    return e === 'pdf' ? 'pdf' : 'other';
  }
  function iconFor(name) {
    switch (kindOf(name)) {
      case 'images': return SVG.image;
      case 'videos': return SVG.video;
      case 'audio': return SVG.audio;
      case 'archive': return SVG.archive;
      case 'apk': return SVG.apk;
      case 'pdf': return SVG.pdf;
      default: return SVG.doc;
    }
  }
  function mimeOf(name) {
    const map = {
      mp4: 'video/mp4', m4v: 'video/mp4', mkv: 'video/x-matroska', webm: 'video/webm', avi: 'video/x-msvideo', mov: 'video/quicktime',
      mp3: 'audio/mpeg', wav: 'audio/wav', flac: 'audio/flac', m4a: 'audio/mp4', ogg: 'audio/ogg',
      jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif', webp: 'image/webp',
      pdf: 'application/pdf', zip: 'application/zip', apk: 'application/vnd.android.package-archive', txt: 'text/plain'
    };
    return map[extOf(name)] || 'application/octet-stream';
  }
  /** 1024-based, the way Windows and the PC app show sizes. */
  function fmtBytes(b) {
    if (b == null || isNaN(b)) return '—';
    if (b < 1) return '0 B';
    const u = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
    const i = Math.min(u.length - 1, Math.floor(Math.log(b) / Math.log(1024)));
    const v = b / Math.pow(1024, i);
    return (i === 0 ? v.toFixed(0) : v >= 100 ? v.toFixed(0) : v >= 10 ? v.toFixed(1) : v.toFixed(2)) + ' ' + u[i];
  }
  /** 1000-based, the way Android's own Settings and Files app show phone storage (a "256 GB" phone). */
  function fmtDec(b) {
    if (b == null || isNaN(b)) return '—';
    if (b < 1) return '0 B';
    const u = ['B', 'kB', 'MB', 'GB', 'TB'];
    const i = Math.min(u.length - 1, Math.floor(Math.log(b) / Math.log(1000)));
    const v = b / Math.pow(1000, i);
    return (i === 0 ? v.toFixed(0) : v >= 100 ? v.toFixed(0) : v >= 10 ? v.toFixed(1) : v.toFixed(2)) + ' ' + u[i];
  }
  function pct(part, total) {
    return total > 0 ? Math.min(100, Math.max(0, (part / total) * 100)) : 0;
  }
  function fmtDuration(sec) {
    sec = Math.max(0, Math.floor(sec));
    const h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
    return h > 0 ? `${h}h ${m}m` : m > 0 ? `${m}m ${s}s` : `${s}s`;
  }
  function timeAgo(when) {
    const t = typeof when === 'number' ? when : Date.parse(when);
    if (!t) return '';
    const s = Math.max(0, (Date.now() - t) / 1000);
    if (s < 60) return 'just now';
    if (s < 3600) return Math.floor(s / 60) + ' min ago';
    if (s < 86400) return Math.floor(s / 3600) + ' h ago';
    return new Date(t).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });
  }
  function fmtDate(when) {
    const t = typeof when === 'number' ? when : Date.parse(when);
    if (!t) return '';
    const d = new Date(t);
    const opts = d.getFullYear() === new Date().getFullYear() ? { day: 'numeric', month: 'short' } : { day: 'numeric', month: 'short', year: 'numeric' };
    return d.toLocaleDateString(undefined, opts);
  }
  function setText(id, text) { const el = $(id); if (el && el.textContent !== text) el.textContent = text; }
  function setWidth(id, percent) { const el = $(id); if (el) el.style.width = percent.toFixed(1) + '%'; }
  function show(id, on) { const el = $(id); if (el) el.style.display = on ? '' : 'none'; }

  let toastTimer = null;
  function toast(msg) {
    const el = $('toast');
    if (!el) return;
    el.textContent = msg;
    el.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => el.classList.remove('show'), 2800);
  }

  function emptyBlock(icon, title, sub, btnLabel, btnAct, extraAttrs) {
    return `<div class="empty-block">
      <div class="empty-block-icon">${icon}</div>
      <div class="empty-block-title">${esc(title)}</div>
      ${sub ? `<div class="empty-block-sub">${esc(sub)}</div>` : ''}
      ${btnLabel ? `<button class="m3-filled-btn" data-act="${btnAct}" ${extraAttrs || ''}>${esc(btnLabel)}</button>` : ''}
    </div>`;
  }
  function skeleton(rows) {
    return Array.from({ length: rows || 6 }, () => '<div class="skeleton-row"><div class="skeleton-icon"></div><div class="skeleton-lines"><div></div><div></div></div></div>').join('');
  }

  // ------------------------------------------------------------------ link to the PC
  const link = {
    server: (ls.get('cloudssd_server_url') || '').replace(/\/+$/, ''),
    token: ls.get('cloudssd_token') || '',
    state: 'init',        // init | unpaired | offline | online | paused
    status: null,         // last /api/status from the PC
    pcName: ls.get('cloudssd_pc_name') || '',
    since: 0,
    ping: null,
    fails: 0
  };
  let appInfo = {};
  let activeTab = 'home';

  function normalizeServer(input) {
    let v = String(input || '').trim().replace(/\/+$/, '');
    if (!v) return '';
    if (!/^https?:\/\//i.test(v)) v = 'http://' + v;
    if (!/:\d+$/.test(v.replace(/^https?:\/\//i, ''))) v += ':8090';
    return v;
  }
  function saveCreds(server, token) {
    link.server = (server || '').replace(/\/+$/, '');
    link.token = token || '';
    ls.set('cloudssd_server_url', link.server);
    if (link.token) ls.set('cloudssd_token', link.token); else ls.del('cloudssd_token');
    if (isApp) { try { native.setServerBase(link.server, link.token); } catch (e) { } }
  }
  function apiUrl(path, params) {
    // The token rides in the query string: a custom header would make the WebView send an extra OPTIONS request
    // before every call, doubling the time each folder takes to open.
    // encodeURIComponent, not URLSearchParams: that one writes a space as "+", which older PC versions read as a
    // real plus sign ("Z:\My+Folder" → Folder not found).
    const all = Object.assign({}, params, { token: link.token });
    return link.server + path + '?' + Object.keys(all).map(k => encodeURIComponent(k) + '=' + encodeURIComponent(all[k])).join('&');
  }
  async function api(path, opts) {
    opts = opts || {};
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), opts.timeout || 8000);
    try {
      const res = await fetch(apiUrl(path, opts.params), { method: opts.method || 'GET', signal: ctrl.signal, cache: 'no-store' });
      let data = null;
      try { data = await res.json(); } catch (e) { /* not JSON */ }
      if (res.status === 401) onUnpaired();
      return { ok: res.ok, status: res.status, data };
    } finally {
      clearTimeout(timer);
    }
  }
  function apiError(r, fallback) {
    return (r && r.data && r.data.error) || fallback || (r ? 'PC answered HTTP ' + r.status : 'PC not reachable');
  }

  function setLinkState(state) {
    if (link.state === state) return;
    const was = link.state;
    link.state = state;
    if (state === 'online') {
      link.since = Date.now();
      link.fails = 0;
      if (was !== 'init') toast('Connected to ' + (link.pcName || 'PC'));
      onConnected();
    }
    renderLink();
  }

  async function pollStatus() {
    if (link.state === 'paused' || !link.server || !link.token) return;
    const t0 = performance.now();
    try {
      const r = await api('/api/status', { timeout: 4000 });
      if (r.ok && r.data && r.data.isPaired) {
        link.ping = Math.round(performance.now() - t0);
        link.status = r.data;
        if (r.data.pcName && r.data.pcName !== link.pcName) { link.pcName = r.data.pcName; ls.set('cloudssd_pc_name', link.pcName); }
        setLinkState('online');
        renderLink();
        renderStatusTiles();
      } else if (r.ok && r.data && r.data.isPaired === false) {
        onUnpaired();
      } else if (r.status !== 401) {
        onOffline();
      }
    } catch (e) {
      onOffline();
    }
  }
  function onOffline() {
    if (link.state === 'paused' || link.state === 'unpaired') return;
    link.fails++;
    link.ping = null;
    setLinkState('offline');
    renderLink();
    renderStatusTiles();
    // The PC's address changes when the router hands out a new one: look for it again on this Wi-Fi.
    if (isApp && (link.fails === 2 || link.fails % 20 === 0)) startDiscovery('relocate');
  }
  function onUnpaired() {
    if (link.state === 'unpaired') return;
    link.token = '';
    ls.del('cloudssd_token');
    if (isApp) { try { native.setServerBase(link.server, ''); } catch (e) { } }
    setLinkState('unpaired');
    renderStatusTiles();
    showPair(link.pcName ? `${link.pcName} no longer accepts this phone. Enter the new pairing code.` : '');
  }
  function onConnected() {
    refreshHistory();
    if (activeTab === 'ssd') openSsdTab();
    if (activeTab === 'gaming') { pollHud(); loadSaves(); }
  }

  function renderLink() {
    const online = link.state === 'online';
    const name = link.pcName || 'PC';
    const dotClass = online ? '' : 'offline';
    $('header-status-dot').className = 'profile-status-dot ' + (online ? 'online' : 'offline');
    $('home-conn-dot').className = 'pc-conn-pulse-dot ' + dotClass;
    $('modal-status-dot').className = 'conn-modal-pulse-dot ' + dotClass;
    setText('pc-avatar', link.pcName ? link.pcName[0].toUpperCase() : '?');

    const titles = {
      init: 'Connecting…', online: 'Connected to ' + name, offline: name + ' is not reachable',
      paused: 'Disconnected from ' + name, unpaired: 'Not paired with a PC'
    };
    const subs = {
      init: link.server.replace(/^https?:\/\//, ''),
      online: link.server.replace(/^https?:\/\//, '') + ' · local Wi-Fi, no mobile data',
      offline: 'Is Cloud-SSD open on the PC, and both on the same Wi-Fi?',
      paused: 'Tap Connect to use the PC again',
      unpaired: 'Tap Connect and enter the code shown on the PC'
    };
    setText('home-conn-title', titles[link.state] || titles.init);
    setText('home-conn-sub', subs[link.state] || '');
    const btn = $('home-conn-toggle-btn'), mbtn = $('modal-connect-btn');
    btn.className = 'pc-conn-toggle-btn ' + (online ? 'disconnect' : 'connect');
    btn.textContent = online ? 'Disconnect' : link.state === 'offline' ? 'Retry' : 'Connect';
    mbtn.className = 'conn-master-btn ' + (online ? 'disconnect' : 'connect');
    mbtn.textContent = online ? 'Disconnect from PC' : link.state === 'offline' ? 'Try again' : 'Connect to PC';

    const tag = $('modal-status-tag');
    tag.className = 'conn-status-tag ' + (online ? 'connected' : 'offline');
    tag.textContent = { online: 'Connected', offline: 'PC not reachable', paused: 'Disconnected', unpaired: 'Not paired', init: 'Connecting…' }[link.state];
    setText('modal-pc-name', link.pcName || 'No PC');
    setText('modal-pc-os', (link.status && link.status.os) || 'Cloud-SSD');
    setText('modal-pc-ip', link.server ? link.server.replace(/^https?:\/\//, '') : '—');
    setText('modal-phone-ip', appInfo.phoneIp || (isApp ? 'Wi-Fi is off' : 'this browser'));
    const ping = $('home-conn-ping');
    ping.style.display = online && link.ping != null ? '' : 'none';
    ping.textContent = link.ping + ' ms';
    setText('modal-ping-val', online && link.ping != null ? link.ping + ' ms' : '—');
    const st = link.status;
    if (online && st) {
      const drives = st.drives || [];
      setText('modal-drives-val', drives.length ? drives.map(d => d.driveLetter + (d.isMounted ? ' on' : ' off')).join(' · ') : (st.drivesCount || 0) + ' online');
      setText('modal-bytes-val', st.stats ? fmtBytes(st.stats.totalTransferredBytes || 0) : '—');
    } else {
      setText('modal-drives-val', '—');
      setText('modal-bytes-val', '—');
    }
    renderUptime();
    setText('gaming-status-badge', online ? 'CONNECTED' : 'OFFLINE');
    $('gaming-status-badge').className = 'gaming-badge' + (online ? '' : ' offline');
    show('gaming-live-tag', online);
  }
  function renderUptime() {
    const online = link.state === 'online';
    const text = online ? fmtDuration((Date.now() - link.since) / 1000) : '—';
    setText('home-conn-time', online ? 'Connected for ' + text : { offline: 'Offline', paused: 'Paused', unpaired: 'Not paired', init: '…' }[link.state] || '—');
    setText('modal-conn-uptime', text);
  }

  // ------------------------------------------------------------------ home tiles
  function renderPhoneTiles() {
    if (!isApp) {
      setText('hw-ram-pct', 'App only'); setText('hw-ram-val', '—');
      setText('hw-phone-free', 'App only'); setText('hw-phone-val', '—');
      return;
    }
    let s;
    try { s = JSON.parse(native.getPhoneStats()); } catch (e) { return; }
    if (s.ramTotal > 0) {
      const p = pct(s.ramUsed, s.ramTotal);
      setText('hw-ram-pct', Math.round(p) + '%');
      setText('hw-ram-val', `${fmtBytes(s.ramUsed)} / ${fmtBytes(s.ramTotal)}`);
      setWidth('hw-ram-bar', p);
    }
    if (s.storageTotal > 0) {
      setText('hw-phone-free', fmtDec(s.storageFree) + ' free');
      setText('hw-phone-val', `${fmtDec(s.storageUsed)} / ${fmtDec(s.storageTotal)}`);
      setWidth('hw-phone-bar', pct(s.storageUsed, s.storageTotal));
      setText('browser-storage-free', fmtDec(s.storageFree) + ' free of ' + fmtDec(s.storageTotal));
      phone.totals = s;
    }
    if (s.storageAccess && s.storageAccess !== phone.access) { phone.access = s.storageAccess; renderPermBanner(); }
  }

  /**
   * Space over all accounts. "Free" is what each provider reports (for Google it already subtracts Gmail and
   * Photos), and "used" is the rest, so used + free always equals the total shown.
   */
  function cloudTotals(c) {
    if (!c) return null;
    const accounts = (c.accounts || []).map(a => {
      if (!(a.totalBytes > 0)) return a;
      const free = a.freeBytes != null ? a.freeBytes : a.totalBytes - (a.usedBytes || 0);
      return Object.assign({}, a, { freeBytes: free, usedBytes: a.totalBytes - free });
    });
    const known = accounts.filter(a => a.totalBytes > 0);
    const total = known.reduce((s, a) => s + a.totalBytes, 0);
    const free = known.reduce((s, a) => s + a.freeBytes, 0);
    return Object.assign({}, c, { accounts, totalBytes: total, freeBytes: free, usedBytes: total - free });
  }

  function renderStatusTiles() {
    const st = link.status;
    const online = link.state === 'online' && st;
    if (!online) {
      const why = { offline: 'PC offline', paused: 'Disconnected', unpaired: 'Not paired', init: '…' }[link.state] || '—';
      setText('hw-ssd-badge', why); setText('hw-ssd-val', '—'); setWidth('hw-ssd-bar', 0);
      setText('hw-pc-status-badge', why); setText('hw-pc-drives-val', '—'); setWidth('hw-pc-bar', 0);
      show('laptop-storage-saver-card', false);
      show('home-game-banner', false);
      return;
    }

    // Cloud SSD: real quota of the connected accounts.
    const c = cloudTotals(st.cloud);
    if (c && c.totalBytes > 0) {
      setText('hw-ssd-badge', fmtBytes(c.freeBytes) + ' free');
      setText('hw-ssd-val', `${fmtBytes(c.usedBytes)} / ${fmtBytes(c.totalBytes)}`);
      setWidth('hw-ssd-bar', Math.max(pct(c.usedBytes, c.totalBytes), c.usedBytes > 0 ? 1.5 : 0));
    } else if (c && c.accountsCount === 0) {
      setText('hw-ssd-badge', 'No account'); setText('hw-ssd-val', 'Add one on the PC'); setWidth('hw-ssd-bar', 0);
    } else {
      setText('hw-ssd-badge', c ? c.accountsCount + ' accounts' : 'Online'); setText('hw-ssd-val', 'Reading sizes…'); setWidth('hw-ssd-bar', 0);
    }

    // PC drives: which letters are really connected.
    const drives = st.drives || [];
    const on = drives.filter(d => d.isMounted);
    setText('hw-pc-status-badge', link.pcName || 'PC');
    if (drives.length) {
      setText('hw-pc-drives-val', on.length ? on.map(d => d.driveLetter).join(' ') + ' online' : 'All disconnected');
      setWidth('hw-pc-bar', pct(on.length, drives.length));
    } else {
      setText('hw-pc-drives-val', (st.drivesCount || 0) + ' online');
      setWidth('hw-pc-bar', st.drivesCount ? 100 : 0);
    }

    // Laptop space saver.
    const sv = st.storage;
    show('laptop-storage-saver-card', !!sv);
    if (sv) {
      setText('laptop-free-pill', sv.laptopFreeGb + ' GB free');
      setText('laptop-threshold-pill', 'Starts below ' + sv.thresholdGb + ' GB');
      setText('laptop-storage-sub', sv.autoOffloadEnabled ? 'Moves old files to ' + (sv.targetDrive || 'the cloud drive') + ' when the laptop is nearly full' : 'Automatic moving is switched off on the PC');
      const pill = $('laptop-status-pill');
      pill.textContent = sv.isUnderThreshold ? 'Low space' : sv.autoOffloadEnabled ? 'On' : 'Off';
      pill.className = 'pc-conn-pill ' + (sv.isUnderThreshold ? 'warn' : 'ping');
    }

    show('home-game-banner', !!(st.gameRunning && st.activeGame));
    if (st.gameRunning && st.activeGame) setText('banner-game-name', st.activeGame);
    if (!ssd.path && activeTab === 'ssd') openSsdTab();
    renderSsdDrives();
  }

  // ------------------------------------------------------------------ sheets
  const SHEETS = ['connection-modal', 'file-actions-sheet', 'file-viewer-modal', 'ram-breakdown-modal', 'storage-modal', 'cloud-modal',
    'drives-manager-modal', 'transfers-modal', 'sort-sheet', 'confirm-modal'];
  const openSheets = [];
  function openSheet(id) {
    const el = $(id);
    if (!el) return;
    el.classList.add('active');
    const i = openSheets.indexOf(id);
    if (i >= 0) openSheets.splice(i, 1);
    openSheets.push(id);
    document.body.classList.add('sheet-open');
  }
  function closeSheet(id) {
    const el = $(id);
    if (!el) return;
    el.classList.remove('active');
    const i = openSheets.indexOf(id);
    if (i >= 0) openSheets.splice(i, 1);
    if (!openSheets.length) document.body.classList.remove('sheet-open');
    if (id === 'file-viewer-modal') stopViewer();
    if (id === 'confirm-modal' && confirmResolve) { confirmResolve(false); confirmResolve = null; }
  }
  function isOpen(id) { return openSheets.includes(id); }

  let confirmResolve = null;
  function confirmDialog(title, text, yesLabel) {
    setText('confirm-title', title);
    setText('confirm-text', text);
    setText('confirm-yes-btn', yesLabel || 'OK');
    openSheet('confirm-modal');
    return new Promise(resolve => { confirmResolve = resolve; });
  }

  // ------------------------------------------------------------------ pairing
  const pair = { pcs: [], selected: '', scanning: false, purpose: '' };

  function showPair(message) {
    $('pair-overlay').style.display = 'flex';
    const err = $('pair-error');
    err.style.display = message ? '' : 'none';
    err.textContent = message || '';
    if (isApp) {
      startDiscovery('pair');
    } else if (/^https?:/.test(location.protocol)) {
      // Opened from the PC itself: that PC is the one to pair with.
      pair.pcs = [{ url: location.origin, ip: location.host, pcName: link.pcName || 'This PC' }];
      pair.selected = location.origin;
      fetch(location.origin + '/api/status').then(r => r.json()).then(d => { if (d.pcName) { pair.pcs[0].pcName = d.pcName; renderPairPcs(); } }).catch(() => { });
    }
    renderPairPcs();
  }
  function hidePair() { $('pair-overlay').style.display = 'none'; }

  function startDiscovery(purpose) {
    if (!isApp) return;
    pair.purpose = purpose;
    pair.scanning = true;
    if (purpose === 'pair') { renderPairPcs(); }
    let port = 8090;
    const m = /:(\d+)$/.exec(link.server);
    if (m) port = parseInt(m[1], 10);
    try { native.discoverPcs(port); } catch (e) { pair.scanning = false; }
  }
  function onDiscovery(d) {
    pair.pcs = d.found || [];
    pair.noWifi = !!d.noWifi;
    if (d.done) pair.scanning = false;
    if (!pair.selected && pair.pcs.length) pair.selected = pair.pcs[0].url;
    if (pair.selected && !pair.pcs.some(p => p.url === pair.selected)) pair.selected = pair.pcs.length ? pair.pcs[0].url : '';
    renderPairPcs();
    if (pair.purpose === 'relocate' && d.done) relocate(pair.pcs);
  }
  /** Our PC got a new address: the one that accepts our token is ours. */
  async function relocate(pcs) {
    if (link.state !== 'offline' || !link.token) return;
    for (const pc of pcs) {
      if (pc.url === link.server) continue;
      try {
        const res = await fetch(pc.url + '/api/status?token=' + encodeURIComponent(link.token), { cache: 'no-store' });
        const data = await res.json();
        if (data && data.isPaired) {
          saveCreds(pc.url, link.token);
          toast('Found ' + (data.pcName || 'your PC') + ' at its new address');
          pollStatus();
          return;
        }
      } catch (e) { /* not ours */ }
    }
  }
  function renderPairPcs() {
    const box = $('pair-pc-list');
    if (!box) return;
    let html = pair.pcs.map(p => `
      <button class="pair-pc ${p.url === pair.selected ? 'selected' : ''}" data-act="pair-pick" data-url="${esc(p.url)}">
        <span class="pair-pc-icon">${SVG.pc}</span>
        <span class="pair-pc-text"><strong>${esc(p.pcName)}</strong><span>${esc(p.ip)}</span></span>
        <span class="pair-pc-check">${SVG.check}</span>
      </button>`).join('');
    if (pair.scanning) html += '<div class="pair-scan"><span class="spinner"></span>Looking for PCs on this Wi-Fi…</div>';
    else if (!pair.pcs.length) {
      html += `<div class="pair-scan none">${isApp
        ? (pair.noWifi ? 'This phone is not on Wi-Fi. Join the same Wi-Fi as the PC (or connect the PC to this phone\'s hotspot), then tap Search.'
          : 'No PC found yet. Open Cloud-SSD on the PC, make sure both are on the same Wi-Fi, then tap Search — or type the PC address below.')
        : 'Type the address shown in Cloud-SSD on the PC.'}</div>`;
    }
    box.innerHTML = html;
    const btn = $('pair-rescan-btn');
    if (btn) btn.textContent = pair.scanning ? '…' : 'Search';
  }
  async function submitPair() {
    const err = $('pair-error');
    const fail = (m) => { err.textContent = m; err.style.display = ''; $('pair-submit-btn').disabled = false; $('pair-submit-btn').textContent = 'Connect'; };
    const typed = normalizeServer($('pair-address').value);
    const url = typed || pair.selected;
    const code = $('pair-code').value.replace(/\D/g, '');
    if (!url) return fail('Choose your PC from the list or type its address.');
    if (code.length !== 6) return fail('Enter the 6-digit pairing code shown on the PC.');
    err.style.display = 'none';
    $('pair-submit-btn').disabled = true;
    $('pair-submit-btn').textContent = 'Connecting…';
    const name = appInfo.model ? `${appInfo.manufacturer || ''} ${appInfo.model}`.trim() : 'Phone browser';
    try {
      const res = await fetch(`${url}/api/pair?code=${code}&name=${encodeURIComponent(name)}`, { method: 'POST', cache: 'no-store' });
      let data = {};
      try { data = await res.json(); } catch (e) { }
      if (res.ok && data.token) {
        saveCreds(url, data.token);
        if (data.pcName) { link.pcName = data.pcName; ls.set('cloudssd_pc_name', data.pcName); }
        $('pair-code').value = '';
        $('pair-submit-btn').disabled = false;
        $('pair-submit-btn').textContent = 'Connect';
        hidePair();
        link.state = 'init';
        toast('Paired with ' + (link.pcName || 'PC'));
        pollStatus();
      } else if (res.status === 404) {
        fail('This PC runs an older Cloud-SSD without pairing codes. Update Cloud-SSD on the PC.');
      } else {
        fail(data.error || 'The PC refused the code (HTTP ' + res.status + ').');
      }
    } catch (e) {
      fail('Could not reach ' + url.replace(/^https?:\/\//, '') + '. Is Cloud-SSD open on the PC and is this the same Wi-Fi?');
    }
  }
  function applyPairLink(o) {
    if (!o || !o.host || !o.token) return false;
    saveCreds(normalizeServer(o.host), o.token);
    hidePair();
    link.state = 'init';
    pollStatus();
    return true;
  }

  // ------------------------------------------------------------------ transfers
  const transfers = new Map();   // id → live transfer (from the app, or a browser upload)
  let xferHistory = [];          // finished transfers as recorded by the PC
  let historyFilter = 'all';
  let localXferId = 0;

  function onTransfer(t) {
    const isNew = !transfers.has(t.id);
    const prev = transfers.get(t.id) || {};
    const cur = Object.assign(prev, t);
    // A transfer that just started on Home must be seen without scrolling.
    if (isNew && activeTab === 'home' && !openSheets.length) {
      setTimeout(() => { const s = $('transfers-section'); if (s) s.scrollIntoView({ block: 'nearest', behavior: 'smooth' }); }, 50);
    }
    if (!cur.startedAt && t.state === 'active') cur.startedAt = Date.now();
    transfers.set(t.id, cur);
    if (t.state === 'done') {
      toast((cur.type === 'upload' ? 'Sent to PC: ' : 'Saved to Downloads: ') + cur.name);
      setTimeout(() => { transfers.delete(t.id); renderTransfers(); }, 4000);
      setTimeout(refreshHistory, 600);
      if (cur.type === 'download') phone.report = null; // a new file landed on the phone
    } else if (t.state === 'cancelled') {
      setTimeout(() => { transfers.delete(t.id); renderTransfers(); }, 2500);
    } else if (t.state === 'failed') {
      toast((cur.type === 'upload' ? 'Could not send ' : 'Could not download ') + cur.name);
    }
    renderTransfers();
  }

  function transferLine(t) {
    const dir = t.type === 'upload' ? 'To PC' : 'To phone';
    if (t.state === 'queued') return dir + ' · waiting…';
    if (t.state === 'done') return dir + ' · done · ' + fmtBytes(t.bytes);
    if (t.state === 'cancelled') return dir + ' · cancelled';
    if (t.state === 'failed') return t.error || 'Failed';
    const parts = [dir, fmtBytes(t.bytes) + (t.total > 0 ? ' / ' + fmtBytes(t.total) : '')];
    if (t.speed > 0) {
      parts.push(fmtBytes(t.speed) + '/s');
      if (t.total > 0) parts.push(fmtDuration((t.total - t.bytes) / t.speed) + ' left');
    }
    return parts.join(' · ');
  }

  function renderTransfers() {
    const list = Array.from(transfers.values()).sort((a, b) => Math.abs(a.id) - Math.abs(b.id));
    const busy = list.some(t => t.state === 'active' || t.state === 'queued');
    show('transfers-live-dot', busy);
    for (const boxId of ['transfers-active', 'transfers-modal-active']) {
      const box = $(boxId);
      if (!box) continue;
      const seen = new Set();
      for (const t of list) {
        seen.add(String(t.id));
        let card = box.querySelector(`[data-xid="${t.id}"]`);
        if (!card) {
          card = document.createElement('div');
          card.className = 'xfer-card';
          card.dataset.xid = t.id;
          card.innerHTML = `<div class="xfer-icon"></div>
            <div class="xfer-body">
              <div class="xfer-top"><span class="xfer-name"></span><span class="xfer-pct"></span></div>
              <div class="transfer-progress-track"><div class="transfer-progress-fill"></div></div>
              <div class="xfer-sub"></div>
            </div>
            <button class="xfer-cancel" data-act="xfer-cancel" data-id="${t.id}">${SVG.close}</button>`;
          box.appendChild(card);
        }
        const p = t.state === 'done' ? 100 : pct(t.bytes, t.total);
        card.className = 'xfer-card ' + t.type + ' ' + t.state;
        card.querySelector('.xfer-icon').innerHTML = t.state === 'done' ? SVG.check : t.type === 'upload' ? SVG.upload : SVG.download;
        card.querySelector('.xfer-name').textContent = t.name;
        card.querySelector('.xfer-pct').textContent = t.state === 'active' && t.total > 0 ? Math.floor(p) + '%' : t.state === 'done' ? '100%' : '';
        const fill = card.querySelector('.transfer-progress-fill');
        fill.style.width = p + '%';
        fill.classList.toggle('indeterminate', t.state === 'active' && !(t.total > 0));
        card.querySelector('.xfer-sub').textContent = transferLine(t);
        card.querySelector('.xfer-cancel').title = t.state === 'failed' ? 'Dismiss' : 'Cancel';
        card.querySelector('.xfer-cancel').style.display = t.state === 'done' || t.state === 'cancelled' ? 'none' : '';
      }
      box.querySelectorAll('[data-xid]').forEach(el => { if (!seen.has(el.dataset.xid)) el.remove(); });
    }
    renderTransferPill(list);
  }

  /** A small pill above the bottom bar on every tab, so a transfer started anywhere stays visible. */
  function renderTransferPill(list) {
    const pill = $('transfer-pill');
    if (!pill) return;
    const running = list.filter(t => t.state === 'active' || t.state === 'queued');
    if (!running.length || (activeTab === 'home' && !openSheets.length)) { pill.style.display = 'none'; return; }
    const cur = running.find(t => t.state === 'active') || running[0];
    pill.style.display = 'flex';
    const p = pct(cur.bytes, cur.total);
    pill.querySelector('.transfer-pill-text').textContent =
      (cur.type === 'upload' ? 'Sending ' : 'Downloading ') + (running.length > 1 ? running.length + ' files' : cur.name) +
      (cur.total > 0 ? ' · ' + Math.floor(p) + '%' : '') + (cur.speed > 0 ? ' · ' + fmtBytes(cur.speed) + '/s' : '');
    pill.querySelector('.transfer-pill-fill').style.width = p + '%';
  }

  async function refreshHistory() {
    if (link.state !== 'online') return;
    try {
      const r = await api('/api/transfers');
      if (r.ok && Array.isArray(r.data)) { xferHistory = r.data; renderHistory(); }
    } catch (e) { /* keep the last list */ }
  }
  const DIRS = {
    upload: { label: 'Sent to PC', cls: 'sent', icon: SVG.upload },
    download: { label: 'Saved to phone', cls: 'received', icon: SVG.download },
    stream: { label: 'Streamed', cls: 'stream', icon: SVG.stream },
    offload: { label: 'Space saver', cls: 'offload', icon: SVG.cloud }
  };
  function historyRow(t, i) {
    const d = DIRS[t.direction] || DIRS.upload;
    const failed = t.status && !/^Completed/i.test(t.status);
    return `<div class="transfer-card-item" data-act="history-open" data-i="${i}">
      <div class="tc-left">
        <div class="tc-icon-wrap ${d.cls}">${d.icon}</div>
        <div class="tc-info">
          <div class="tc-name">${esc(t.fileName)}</div>
          <div class="tc-meta">
            <span class="tc-dir-badge ${d.cls}">${d.label}</span>
            <span>${esc(t.sizeFormatted || fmtBytes(t.bytes))}</span>
            ${t.speedMBps > 0.1 && t.direction !== 'offload' ? `<span class="tc-speed-pill">${t.speedMBps} MB/s</span>` : ''}
            <span>${esc(timeAgo(t.timestamp))}</span>
            ${failed ? `<span class="tc-failed">${esc(t.status)}</span>` : ''}
          </div>
        </div>
      </div>
    </div>`;
  }
  function renderHistory() {
    const recent = $('transfers-recent');
    const mine = xferHistory.filter(t => t.direction !== 'offload');
    if (recent) {
      recent.innerHTML = mine.length
        ? mine.slice(0, 5).map(t => historyRow(t, xferHistory.indexOf(t))).join('')
        : '<div class="recent-empty-card">Nothing sent yet. Use "Send Any File" to send something to the PC.</div>';
    }
    const all = $('transfers-modal-list');
    if (all) {
      const rows = xferHistory.filter(t => historyFilter === 'all' || t.direction === historyFilter);
      all.innerHTML = rows.length ? rows.slice(0, 100).map(t => historyRow(t, xferHistory.indexOf(t))).join('') : '<div class="recent-empty-card">No transfers here yet.</div>';
    }
    setText('transfers-sub', xferHistory.length + ' recorded on ' + (link.pcName || 'the PC'));
  }

  function sendFiles(kind) {
    if (link.state !== 'online') { toast(link.state === 'unpaired' ? 'Pair with your PC first' : 'PC is not connected'); if (link.state === 'unpaired') showPair(''); return; }
    if (isApp) {
      try { native.pickAndUploadFiles(kind); } catch (e) { toast('Cannot open the file picker'); }
    } else {
      $(kind === 'photos' ? 'photo-uploader' : 'file-uploader').click();
    }
  }
  /** Browser (no app): upload with XMLHttpRequest so the same live progress card can be shown. */
  function uploadInBrowser(file) {
    const id = --localXferId;
    onTransfer({ id, type: 'upload', name: file.name, state: 'queued', bytes: 0, total: file.size, speed: 0 });
    const xhr = new XMLHttpRequest();
    let lastT = Date.now(), lastB = 0;
    xhr.open('POST', apiUrl('/api/upload', { filename: file.name }));
    xhr.upload.onprogress = (e) => {
      const now = Date.now();
      const t = transfers.get(id) || {};
      let speed = t.speed || 0;
      if (now - lastT >= 300) { speed = (e.loaded - lastB) * 1000 / (now - lastT); lastT = now; lastB = e.loaded; }
      onTransfer({ id, type: 'upload', name: file.name, state: 'active', bytes: e.loaded, total: e.total || file.size, speed });
    };
    xhr.onload = () => {
      if (xhr.status === 200) onTransfer({ id, type: 'upload', name: file.name, state: 'done', bytes: file.size, total: file.size, speed: 0 });
      else {
        let msg = 'PC answered HTTP ' + xhr.status;
        try { msg = JSON.parse(xhr.responseText).error || msg; } catch (e) { }
        onTransfer({ id, type: 'upload', name: file.name, state: 'failed', bytes: 0, total: file.size, speed: 0, error: msg });
      }
    };
    xhr.onerror = () => onTransfer({ id, type: 'upload', name: file.name, state: 'failed', bytes: 0, total: file.size, speed: 0, error: 'PC not reachable' });
    xhr.onabort = () => onTransfer({ id, type: 'upload', name: file.name, state: 'cancelled', bytes: 0, total: file.size, speed: 0 });
    transfers.get(id).xhr = xhr;
    xhr.send(file);
  }
  function cancelTransfer(id) {
    const t = transfers.get(id);
    if (!t) return;
    if (t.state === 'failed') { transfers.delete(id); renderTransfers(); return; }
    if (t.xhr) t.xhr.abort();
    else if (isApp) { try { native.cancelTransfer(id); } catch (e) { } }
  }

  // ------------------------------------------------------------------ starred (kept on this phone)
  let starred = ls.json('cloudssd_starred', []);
  function isStarred(f) { return starred.some(s => s.path === f.path && s.src === f.src); }
  function toggleStar(f) {
    if (isStarred(f)) starred = starred.filter(s => !(s.path === f.path && s.src === f.src));
    else starred.unshift({ name: f.name, path: f.path, size: f.size, modified: f.modified, src: f.src });
    ls.set('cloudssd_starred', JSON.stringify(starred.slice(0, 300)));
    setText('cat-stat-starred', starred.length ? starred.length + ' files' : 'None yet');
  }

  // ------------------------------------------------------------------ file actions + viewer
  let sheetFile = null;
  let sheetActions = [];

  function streamUrl(path) { return apiUrl('/api/stream', { path }); }
  function fileUrl(path) { return 'file://' + path.split('/').map(encodeURIComponent).join('/'); }
  function thumbUrl(f) { return 'http://cloudssd.thumb/?p=' + encodeURIComponent(f.path) + '&m=' + (f.modified || 0); }

  /** f = {name, path, size, modified, src: 'cloud' | 'phone'} */
  function openFileSheet(f) {
    sheetFile = f;
    const k = kindOf(f.name);
    const cloud = f.src === 'cloud';
    const canView = k === 'images' || k === 'audio' || (k === 'videos' && PLAYABLE_VIDEO.includes(extOf(f.name))) || (cloud && k === 'text');
    sheetActions = [];
    if (canView) sheetActions.push({ icon: SVG.play, title: k === 'images' ? 'View' : k === 'text' ? 'Read' : 'Play', sub: cloud ? 'Streams from the PC over Wi-Fi' : 'On this phone', fn: () => openViewer(f) });
    if (isApp) sheetActions.push({ icon: SVG.share, title: 'Open with another app', sub: cloud ? 'Video player, gallery, PDF reader…' : 'Choose an app', fn: () => openExternally(f) });
    if (cloud) {
      sheetActions.push({ icon: SVG.download, title: 'Save to phone', sub: 'Into the Downloads folder · ' + fmtBytes(f.size), fn: () => downloadToPhone(f) });
      sheetActions.push({ icon: SVG.pc, title: 'Open on the PC screen', sub: 'Opens in its Windows app', fn: () => openOnPc(f) });
    } else {
      sheetActions.push({ icon: SVG.upload, title: 'Send to PC', sub: fmtBytes(f.size) + ' over Wi-Fi', fn: () => sendPhoneFiles([f]) });
    }
    sheetActions.push({ icon: SVG.star, title: isStarred(f) ? 'Remove star' : 'Add star', sub: 'Starred files are listed on the Phone tab', fn: () => { toggleStar(f); toast(isStarred(f) ? 'Starred' : 'Star removed'); } });
    sheetActions.push({ icon: SVG.info, title: 'Details', sub: f.path, keep: true, fn: () => toast(f.path) });

    setText('sheet-file-name', f.name);
    setText('sheet-file-meta', [fmtBytes(f.size), fmtDate(f.modified), cloud ? 'on the PC' : 'on this phone'].filter(Boolean).join(' · '));
    $('sheet-file-icon').innerHTML = iconFor(f.name);
    $('sheet-actions-list').innerHTML = sheetActions.map((a, i) => `
      <div class="m3-sheet-row" data-act="sheet-action" data-i="${i}">
        ${a.icon}
        <div class="m3-sheet-row-text"><div>${esc(a.title)}</div><div class="m3-sheet-row-sub">${esc(a.sub)}</div></div>
      </div>`).join('');
    openSheet('file-actions-sheet');
  }
  function openExternally(f) {
    try {
      if (f.src === 'cloud') native.openWith(streamUrl(f.path), mimeOf(f.name), f.name);
      else native.openLocalFile(f.path);
    } catch (e) { toast('Cannot open this file'); }
  }
  function downloadToPhone(f) {
    const url = apiUrl('/api/download', { path: f.path });
    if (isApp) {
      try { native.downloadFile(url, f.name); toast('Downloading ' + f.name); } catch (e) { toast('Download could not start'); }
    } else {
      const a = document.createElement('a');
      a.href = url; a.download = f.name;
      document.body.appendChild(a); a.click(); a.remove();
      toast('Download started in the browser');
    }
  }
  async function openOnPc(f) {
    try {
      const r = await api('/api/open-pc', { method: 'POST', params: { path: f.path } });
      toast(r.ok ? 'Opened on the PC: ' + f.name : apiError(r));
    } catch (e) { toast('PC not reachable'); }
  }
  function sendPhoneFiles(files) {
    if (link.state !== 'online') { toast('PC is not connected'); return; }
    if (!isApp) return;
    files.forEach(f => { try { native.uploadFileByPath(f.path); } catch (e) { } });
    toast(files.length === 1 ? 'Sending ' + files[0].name : 'Sending ' + files.length + ' files to the PC');
  }

  let viewerFile = null;
  function openViewer(f) {
    viewerFile = f;
    const k = kindOf(f.name);
    const url = f.src === 'cloud' ? streamUrl(f.path) : fileUrl(f.path);
    setText('viewer-filename', f.name);
    setText('viewer-meta', fmtBytes(f.size) + (f.src === 'cloud' ? ' · streaming from the PC' : ' · on this phone'));
    ['video', 'image', 'audio', 'text', 'generic'].forEach(s => show('viewer-section-' + s, false));

    if (k === 'videos') {
      const v = $('native-video-player');
      show('viewer-section-video', true);
      v.onerror = () => { if (viewerFile === f) viewerFallback(f, 'This phone cannot play this video format here.'); };
      v.src = url;
      v.play().catch(() => { });
    } else if (k === 'images') {
      show('viewer-section-image', true);
      const img = $('viewer-image-elem');
      img.onerror = () => { if (viewerFile === f) viewerFallback(f, 'This picture could not be shown.'); };
      img.src = url;
    } else if (k === 'audio') {
      show('viewer-section-audio', true);
      setText('viewer-audio-title', f.name);
      const a = $('viewer-audio-player');
      a.onerror = () => { if (viewerFile === f) viewerFallback(f, 'This audio format cannot be played here.'); };
      a.src = url;
      a.play().catch(() => { });
    } else if (k === 'text' && f.src === 'cloud') {
      show('viewer-section-text', true);
      const pre = $('viewer-text-content');
      pre.textContent = 'Loading…';
      api('/api/preview', { params: { path: f.path }, timeout: 30000 }).then(r => {
        if (viewerFile !== f) return;
        if (r.ok && r.data && r.data.success) pre.textContent = r.data.isBinary ? '(This file is not text.)' : r.data.content + (r.data.truncated ? '\n\n… (first 256 KB shown)' : '');
        else pre.textContent = apiError(r, 'Could not read the file');
      }).catch(() => { if (viewerFile === f) pre.textContent = 'PC not reachable'; });
    } else {
      viewerFallback(f, '');
      return;
    }
    openSheet('file-viewer-modal');
  }
  function viewerFallback(f, why) {
    ['video', 'image', 'audio', 'text'].forEach(s => show('viewer-section-' + s, false));
    show('viewer-section-generic', true);
    $('viewer-generic-icon').innerHTML = iconFor(f.name);
    setText('viewer-generic-name', f.name);
    const sub = document.querySelector('#viewer-section-generic .generic-viewer-sub');
    if (sub) sub.textContent = why || 'No built-in preview for this kind of file';
    $('viewer-generic-actions').innerHTML =
      (isApp ? `<button class="m3-filled-btn" data-act="viewer-external">${SVG.share}<span>Open with another app</span></button>` : '') +
      (f.src === 'cloud' ? `<button class="m3-tonal-btn" data-act="viewer-download">${SVG.download}<span>Save to phone</span></button>` : '');
    openSheet('file-viewer-modal');
  }
  function stopViewer() {
    viewerFile = null;
    for (const id of ['native-video-player', 'viewer-audio-player']) {
      const el = $(id);
      if (el) { el.onerror = null; el.pause(); el.removeAttribute('src'); el.load(); }
    }
    const img = $('viewer-image-elem');
    if (img) { img.onerror = null; img.removeAttribute('src'); }
  }

  // ------------------------------------------------------------------ Phone tab (files on this phone)
  const phone = {
    access: '', totals: null, report: ls.json('cloudssd_storage_report', null), scanning: false,
    stack: [], view: null, items: { folders: [], files: [] }, seq: 0, grid: null,
    selecting: false, selected: new Set()
  };
  const CATS = [
    { key: 'images', label: 'Images', color: '#F48FB1', icon: SVG.image },
    { key: 'videos', label: 'Videos', color: '#EF9A9A', icon: SVG.video },
    { key: 'audio', label: 'Audio', color: '#FDD663', icon: SVG.audio },
    { key: 'documents', label: 'Documents', color: '#78D9EC', icon: SVG.doc },
    { key: 'apps', label: 'APKs & archives', color: '#6DD58C', icon: SVG.apk },
    { key: 'other', label: 'Other files', color: '#B39DDB', icon: SVG.folder }
  ];

  function renderPermBanner() {
    if (!isApp) { show('phone-perm-banner', false); return; }
    const a = phone.access;
    show('phone-perm-banner', a !== 'all');
    if (a === 'none') {
      setText('phone-perm-title', 'Allow access to phone files');
      setText('phone-perm-sub', 'Needed to show your photos, videos, documents and folders. Android opens a screen with one switch: turn it on and come back.');
      setText('phone-perm-btn', 'Allow');
    } else if (a === 'media') {
      setText('phone-perm-title', 'Only photos, videos and audio are visible');
      setText('phone-perm-sub', 'Turn on "All files access" to also see documents, APKs and every folder, like the phone\'s own Files app.');
      setText('phone-perm-btn', 'Allow all files');
    }
  }
  function requestScan() {
    if (!isApp || phone.scanning) return;
    phone.scanning = true;
    setText('storage-scan-tag', 'Reading…');
    try { native.scanStorage(); } catch (e) { phone.scanning = false; }
  }
  function onStorageReport(r) {
    phone.scanning = false;
    if (r.error && !r.cats) return;
    phone.report = r;
    phone.access = r.access || phone.access;
    ls.set('cloudssd_storage_report', JSON.stringify(r));
    renderPermBanner();
    renderStorage();
  }
  function storageSegments(r) {
    const segs = CATS.map(c => ({ label: c.label, color: c.color, bytes: (r.cats[c.key] || {}).bytes || 0, count: (r.cats[c.key] || {}).count || 0, key: c.key, icon: c.icon }));
    segs.push({ label: 'Apps & system', color: '#8E918F', bytes: r.appsSystemBytes || 0, key: 'system', icon: SVG.apk });
    return segs;
  }
  function segBar(segs, total) {
    return segs.filter(s => s.bytes > 0).map(s =>
      `<div class="bar-seg" style="width:${Math.max(0.8, pct(s.bytes, total)).toFixed(2)}%;background:${s.color}" title="${esc(s.label)}"></div>`).join('');
  }
  function renderStorage() {
    const r = phone.report;
    if (!r || !r.cats) return;
    const segs = storageSegments(r);
    // Phone tab: category tiles + the small panel.
    for (const c of CATS) {
      const s = r.cats[c.key];
      if (s && $('cat-stat-' + c.key)) setText('cat-stat-' + c.key, s.count ? `${s.count.toLocaleString()} · ${fmtDec(s.bytes)}` : 'None');
    }
    const dl = r.cats.downloads;
    if (dl) setText('cat-stat-downloads', dl.count ? `${dl.count.toLocaleString()} · ${fmtDec(dl.bytes)}` : 'Empty');
    setText('cat-stat-starred', starred.length ? starred.length + ' files' : 'None yet');
    $('browser-storage-bar').innerHTML = segBar(segs, r.total);
    $('browser-storage-legend').innerHTML = segs.filter(s => s.bytes > 0).map(s =>
      `<span><span class="legend-dot" style="background:${s.color}"></span>${esc(s.label)} ${fmtDec(s.bytes)}</span>`).join('');
    setText('browser-storage-free', fmtDec(r.free) + ' free of ' + fmtDec(r.total));

    // Storage sheet.
    setText('storage-val-used', fmtDec(r.used));
    setText('storage-val-free', fmtDec(r.free));
    setText('storage-val-total', fmtDec(r.total));
    setText('storage-device-sub', [appInfo.manufacturer, appInfo.model].filter(Boolean).join(' ') || 'Internal storage');
    setText('storage-scan-tag', 'Live');
    $('storage-bar').innerHTML = segBar(segs, r.total);
    $('storage-rows').innerHTML = segs.map(s => `
      <div class="usage-row ${s.key !== 'system' && s.key !== 'other' && s.count ? 'tappable' : ''}" ${s.key !== 'system' && s.key !== 'other' ? `data-act="phone-cat" data-cat="${s.key}" data-title="${esc(s.label)}"` : ''}>
        <span class="usage-dot" style="background:${s.color}"></span>
        <div class="usage-main">
          <div class="usage-name">${esc(s.label)}</div>
          <div class="usage-sub">${s.key === 'system' ? 'Installed apps, their data and Android' : s.count ? s.count.toLocaleString() + ' files' : 'None'}</div>
          <div class="usage-bar"><div style="width:${pct(s.bytes, r.used).toFixed(1)}%;background:${s.color}"></div></div>
        </div>
        <div class="usage-val">${fmtDec(s.bytes)}</div>
      </div>`).join('') + `
      <div class="usage-row">
        <span class="usage-dot free"></span>
        <div class="usage-main"><div class="usage-name">Free</div><div class="usage-sub">Ready to use</div></div>
        <div class="usage-val">${fmtDec(r.free)}</div>
      </div>`;
    $('storage-note').innerHTML = r.access === 'all'
      ? 'Same numbers as Settings → Storage. Tap a kind to see its files.'
      : (r.access === 'none'
        ? 'Android has not let this app see your files yet, so everything is counted inside "Apps &amp; system". '
        : 'Android is showing this app only photos, videos and audio, so documents, APKs and other files are counted inside "Apps &amp; system". ')
        + '<button class="link-btn" data-act="grant-storage">Allow all files</button>';
  }

  function phoneOpen(view) {
    if (!isApp) return;
    if (phone.view) phone.stack.push(phone.view);
    phoneShow(view);
  }
  function phoneShow(view) {
    phone.view = view;
    phone.selecting = false;
    phone.selected.clear();
    renderSelectBar();
    show('phone-home', false);
    show('phone-list', true);
    setText('phone-list-title', view.title);
    setText('phone-current-path', view.type === 'dir' ? prettyPhonePath(view.path) : view.type === 'cat' ? 'Newest first' : '');
    const box = $('phone-explorer-container');
    box.className = 'phone-file-list';
    box.innerHTML = skeleton(7);
    window.scrollTo(0, 0);
    if (view.type === 'starred') {
      phone.items = { folders: [], files: starred.map(s => Object.assign({}, s)) };
      renderPhoneItems({});
      return;
    }
    const req = ++phone.seq;
    try {
      if (view.type === 'dir') native.listDir(req, view.path || '');
      else native.listCategory(req, view.cat);
    } catch (e) {
      box.innerHTML = emptyBlock(SVG.folder, 'Could not read the phone', String(e && e.message || e));
    }
  }
  function phoneBack() {
    if (phone.selecting) { phone.selecting = false; phone.selected.clear(); renderSelectBar(); renderPhoneItems({}); return true; }
    if (!phone.view) return false;
    const prev = phone.stack.pop();
    if (prev) { phoneShow(prev); return true; }
    phone.view = null;
    show('phone-list', false);
    show('phone-home', true);
    return true;
  }
  function prettyPhonePath(path) {
    return String(path || '').replace(/^\/storage\/emulated\/0\/?/, 'Internal storage/').replace(/\/$/, '').split('/').join(' › ');
  }
  function onPhoneList(d) {
    if (d.reqId === search.phoneReq) { renderSearchPhone(d); return; }
    if (d.reqId !== phone.seq || !phone.view) return;
    if (phone.view.type === 'dir' && d.path) {
      phone.view.path = d.path;
      setText('phone-current-path', prettyPhonePath(d.path));
      if (!phone.view.titled) setText('phone-list-title', d.path === d.root ? 'Internal storage' : d.path.split('/').pop());
    }
    phone.items = {
      folders: d.folders || [],
      files: (d.files || []).map(f => Object.assign(f, { src: 'phone' }))
    };
    renderPhoneItems(d);
  }
  function phoneGrid() {
    if (phone.grid !== null) return phone.grid;
    return !!(phone.view && phone.view.type === 'cat' && (phone.view.cat === 'images' || phone.view.cat === 'videos'));
  }
  function renderPhoneItems(d) {
    const box = $('phone-explorer-container');
    const { folders, files } = phone.items;
    if (d.error && !folders.length && !files.length) {
      box.className = 'phone-file-list';
      box.innerHTML = emptyBlock(SVG.folder, d.error, phone.access !== 'all' ? 'Allow "All files access" so this app can read every folder.' : '', phone.access !== 'all' ? 'Allow all files' : '', 'grant-storage');
      return;
    }
    if (!folders.length && !files.length) {
      box.className = 'phone-file-list';
      const limited = phone.access !== 'all' && phone.view && (phone.view.type === 'dir' || ['documents', 'apps', 'downloads'].includes(phone.view.cat));
      box.innerHTML = emptyBlock(SVG.folder, phone.view && phone.view.type === 'starred' ? 'No starred files yet' : 'Nothing here',
        limited ? 'Android hides documents, APKs and other files from this app until "All files access" is on.' : '',
        limited ? 'Allow all files' : '', 'grant-storage');
      return;
    }
    const grid = phoneGrid();
    box.className = grid ? 'thumb-grid' : 'phone-file-list';
    let html = '';
    folders.forEach((fo, i) => {
      html += `<div class="phone-item-row" data-act="phone-folder" data-i="${i}">
        <div class="phone-item-icon folder">${SVG.folder}</div>
        <div class="phone-item-info">
          <div class="phone-item-name">${esc(fo.name)}</div>
          <div class="phone-item-meta">${fo.itemCount != null ? fo.itemCount + (fo.itemCount === 1 ? ' item' : ' items') : 'Folder'}</div>
        </div>
        <div class="phone-item-chevron">${SVG.chevron}</div>
      </div>`;
    });
    files.forEach((f, i) => {
      const k = kindOf(f.name);
      const hasThumb = f.src === 'phone' && (k === 'images' || k === 'videos');
      const sel = phone.selected.has(i);
      if (grid) {
        html += `<div class="thumb-tile ${sel ? 'selected' : ''}" data-act="phone-file" data-i="${i}">
          <div class="thumb-tile-icon">${iconFor(f.name)}</div>
          ${hasThumb ? `<img loading="lazy" decoding="async" src="${esc(thumbUrl(f))}" onerror="this.remove()" alt="">` : ''}
          ${k === 'videos' ? `<div class="thumb-tile-play">${SVG.play}</div>` : ''}
          <div class="thumb-tile-check">${SVG.check}</div>
          ${hasThumb ? '' : `<div class="thumb-tile-name">${esc(f.name)}</div>`}
        </div>`;
      } else {
        html += `<div class="phone-item-row ${sel ? 'selected' : ''}" data-act="phone-file" data-i="${i}">
          <div class="phone-item-icon">${iconFor(f.name)}${hasThumb ? `<img loading="lazy" decoding="async" src="${esc(thumbUrl(f))}" onerror="this.remove()" alt="">` : ''}<div class="row-check">${SVG.check}</div></div>
          <div class="phone-item-info">
            <div class="phone-item-name">${esc(f.name)}</div>
            <div class="phone-item-meta">${fmtBytes(f.size)}${f.modified ? ' · ' + fmtDate(f.modified) : ''}${f.src === 'cloud' ? ' · on the PC' : ''}</div>
          </div>
          ${f.src === 'phone' ? `<button class="btn-file-action send" data-act="phone-send" data-i="${i}">Send</button>` : ''}
        </div>`;
      }
    });
    if (d.truncated) html += '<div class="list-note">Showing the newest files only.</div>';
    box.innerHTML = html;
    show('phone-select-btn', files.some(f => f.src === 'phone'));
  }
  function renderSelectBar() {
    show('phone-select-bar', phone.selecting);
    setText('phone-select-count', phone.selected.size + ' selected');
    setText('phone-select-btn', phone.selecting ? 'Done' : 'Select');
    document.body.classList.toggle('selecting', phone.selecting);
  }

  // ------------------------------------------------------------------ Cloud SSD tab (drives on the PC)
  const ssd = {
    path: '', root: '', cache: new Map(Object.entries(ls.json('cloudssd_dircache', {}))), view: { folders: [], files: [] },
    seq: 0, grid: ls.get('cloudssd_layout') === 'grid', sort: ls.get('cloudssd_sort') || 'name-asc', localDrives: [], showAll: false
  };
  const SORTS = [
    { key: 'name-asc', label: 'Name (A to Z)' }, { key: 'name-desc', label: 'Name (Z to A)' },
    { key: 'date-desc', label: 'Newest first' }, { key: 'date-asc', label: 'Oldest first' },
    { key: 'size-desc', label: 'Largest first' }, { key: 'size-asc', label: 'Smallest first' }
  ];

  function ssdPlaces() {
    const st = link.status || {};
    const places = (st.drives || []).map(d => ({ root: d.driveLetter + '\\', label: `${d.driveLetter} ${d.displayName}`, cloud: true, mounted: d.isMounted, id: d.id }));
    if (st.uploadDirectory) places.push({ root: st.uploadDirectory, label: 'From phone', cloud: false, mounted: true });
    for (const d of ssd.localDrives) places.push({ root: d.root, label: `${d.driveLetter} PC disk`, cloud: false, mounted: true });
    return places;
  }
  function placeOf(path) {
    const p = String(path || '').toLowerCase();
    return ssdPlaces().filter(pl => p.startsWith(pl.root.toLowerCase())).sort((a, b) => b.root.length - a.root.length)[0] || null;
  }
  function renderSsdDrives() {
    const strip = $('ssd-drives-strip');
    if (!strip) return;
    const cur = placeOf(ssd.path);
    const html = ssdPlaces().map(pl => `
      <button class="ssd-drive-pill ${cur && cur.root === pl.root ? 'active' : ''} ${pl.mounted ? '' : 'off'}" data-act="ssd-place" data-root="${esc(pl.root)}">
        ${pl.cloud ? SVG.cloud : pl.label === 'From phone' ? SVG.upload : SVG.pc}<span>${esc(pl.label)}${pl.mounted ? '' : ' · off'}</span>
      </button>`).join('');
    if (strip.dataset.html !== html) { strip.innerHTML = html; strip.dataset.html = html; }
  }
  function openSsdTab() {
    if (link.state !== 'online') { renderSsdOffline(); return; }
    if (!ssd.localDrives.length) loadLocalDrives();
    if (!ssd.path) {
      const places = ssdPlaces();
      const first = places.find(p => p.id === 'cloudssd' && p.mounted) || places.find(p => p.cloud && p.mounted) || places[0];
      if (!first) { renderSsdDrives(); $('ssd-items-container').innerHTML = skeleton(6); return; }
      loadSsd(first.root);
    } else {
      loadSsd(ssd.path);
    }
  }
  async function loadLocalDrives() {
    try {
      const r = await api('/api/drives', { timeout: 15000 });
      if (r.ok && Array.isArray(r.data)) {
        drivesData = r.data;
        ssd.localDrives = r.data.filter(d => !d.isCloud && d.root);
        renderSsdDrives();
      }
    } catch (e) { }
  }
  function renderSsdOffline() {
    renderSsdDrives();
    $('ssd-breadcrumbs-path').textContent = '';
    $('ssd-items-container').className = 'ssd-items-container list-view';
    $('ssd-items-container').innerHTML = link.state === 'unpaired'
      ? emptyBlock(SVG.pc, 'Not paired with a PC', 'Pair once with the code shown in Cloud-SSD on the PC.', 'Pair now', 'open-pair')
      : emptyBlock(SVG.pc, 'PC is not connected', 'Cloud-SSD must be open on the PC, on the same Wi-Fi.', 'Try again', 'toggle-link');
  }
  function renderCrumbs() {
    const pl = placeOf(ssd.path);
    const box = $('ssd-breadcrumbs-path');
    if (!ssd.path) { box.innerHTML = ''; return; }
    const root = pl ? pl.root : ssd.path.slice(0, 3);
    const rest = ssd.path.slice(root.length).split('\\').filter(Boolean);
    let acc = root.replace(/\\$/, '');
    let html = `<span class="crumb" data-act="ssd-crumb" data-path="${esc(root)}">${esc(pl ? pl.label : root)}</span>`;
    for (const seg of rest) {
      acc += '\\' + seg;
      html += `<span class="crumb-sep">›</span><span class="crumb" data-act="ssd-crumb" data-path="${esc(acc)}">${esc(seg)}</span>`;
    }
    box.innerHTML = html;
    box.scrollLeft = box.scrollWidth;
    ssd.root = root;
    $('ssd-back-btn').classList.toggle('disabled', !rest.length);
  }
  function persistDirCache() {
    // Keep the 12 most recently opened folders so the tab opens instantly next time; skip huge folders.
    const entries = Array.from(ssd.cache.entries()).slice(-12).filter(([, v]) => (v.folders.length + v.files.length) <= 400);
    ls.set('cloudssd_dircache', JSON.stringify(Object.fromEntries(entries)));
  }
  async function loadSsd(path, force) {
    ssd.path = path;
    ssd.showAll = false;
    renderCrumbs();
    renderSsdDrives();
    const box = $('ssd-items-container');
    const cached = ssd.cache.get(path);
    if (cached) renderSsd(cached);
    else { box.className = 'ssd-items-container list-view'; box.innerHTML = skeleton(8); }
    const seq = ++ssd.seq;
    $('ssd-refresh-btn').classList.add('spinning');
    try {
      const r = await api('/api/files', { params: { path }, timeout: 60000 });
      if (seq !== ssd.seq) return;
      if (r.ok && r.data) {
        const data = { folders: r.data.folders || [], files: r.data.files || [] };
        ssd.cache.delete(path);
        ssd.cache.set(path, data);
        persistDirCache();
        renderSsd(data);
      } else {
        ssd.cache.delete(path);
        const pl = placeOf(path);
        if (r.data && r.data.driveMissing && pl && pl.cloud) {
          box.innerHTML = emptyBlock(SVG.cloud, `${pl.label} is not connected`, 'The drive is switched off on the PC. Connect it to browse its files.', 'Connect drive', 'drive-mount', `data-id="${esc(pl.id)}"`);
        } else if (r.status === 403) {
          box.innerHTML = emptyBlock(SVG.pc, 'Browsing is switched off', apiError(r));
        } else if (r.status !== 401) {
          box.innerHTML = emptyBlock(SVG.folder, apiError(r, 'Could not open this folder'), path, 'Try again', 'ssd-refresh');
        }
      }
    } catch (e) {
      if (seq !== ssd.seq) return;
      if (!cached) box.innerHTML = emptyBlock(SVG.pc, 'PC did not answer', 'The folder could not be loaded. Check the Wi-Fi and try again.', 'Try again', 'ssd-refresh');
      else toast('Showing saved list — PC did not answer');
    } finally {
      if (seq === ssd.seq) $('ssd-refresh-btn').classList.remove('spinning');
    }
  }
  function sortFiles(files) {
    const [by, dir] = ssd.sort.split('-');
    const m = dir === 'desc' ? -1 : 1;
    const cmp = {
      name: (a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }),
      date: (a, b) => (Date.parse(a.modified) || 0) - (Date.parse(b.modified) || 0),
      size: (a, b) => (a.size || 0) - (b.size || 0)
    }[by] || (() => 0);
    return files.slice().sort((a, b) => m * cmp(a, b));
  }
  function renderSsd(data) {
    const box = $('ssd-items-container');
    const [by] = ssd.sort.split('-');
    const folders = by === 'size' ? data.folders.slice() : sortFiles(data.folders);
    const files = sortFiles(data.files).map(f => Object.assign({}, f, { src: 'cloud' }));
    ssd.view = { folders, files };
    box.className = 'ssd-items-container ' + (ssd.grid ? 'grid-view' : 'list-view');
    if (!folders.length && !files.length) { box.innerHTML = emptyBlock(SVG.folder, 'This folder is empty', ''); return; }
    const LIMIT = 300;
    const total = folders.length + files.length;
    let n = 0, html = '';
    for (let i = 0; i < folders.length && (ssd.showAll || n < LIMIT); i++, n++) {
      html += `<div class="explorer-item folder-item" data-act="ssd-folder" data-i="${i}">
        <div class="item-icon folder-icon">${SVG.folder}</div>
        <div class="item-details"><div class="item-name">${esc(folders[i].name)}</div><div class="item-meta">Folder${folders[i].modified ? ' · ' + fmtDate(folders[i].modified) : ''}</div></div>
        <div class="item-action-icon">${SVG.chevron}</div>
      </div>`;
    }
    for (let i = 0; i < files.length && (ssd.showAll || n < LIMIT); i++, n++) {
      const f = files[i];
      html += `<div class="explorer-item file-item" data-act="ssd-file" data-i="${i}">
        <div class="item-icon kind-${kindOf(f.name)}">${iconFor(f.name)}</div>
        <div class="item-details"><div class="item-name">${esc(f.name)}</div><div class="item-meta">${fmtBytes(f.size)}${f.modified ? ' · ' + fmtDate(f.modified) : ''}</div></div>
        <button class="item-more-btn" data-act="ssd-more" data-i="${i}">${SVG.more}</button>
      </div>`;
    }
    if (n < total) html += `<button class="m3-tonal-btn list-more" data-act="ssd-show-all">Show all ${total} items</button>`;
    else html += `<div class="list-note">${folders.length} folders · ${files.length} files</div>`;
    box.innerHTML = html;
  }
  function ssdUp() {
    if (!ssd.path || ssd.path.length <= ssd.root.length) return false;
    const trimmed = ssd.path.replace(/\\+$/, '');
    let parent = trimmed.slice(0, trimmed.lastIndexOf('\\'));
    if (parent.length < ssd.root.length) parent = ssd.root;
    if (/^[A-Za-z]:$/.test(parent)) parent += '\\';
    loadSsd(parent);
    return true;
  }

  // ------------------------------------------------------------------ PC drives + cloud storage sheets
  let drivesData = null;
  const driveBusy = {};

  async function loadDrives() {
    if (link.state !== 'online') { renderDrives(); renderCloud(); return; }
    try {
      const r = await api('/api/drives', { timeout: 15000 });
      if (r.ok && Array.isArray(r.data)) {
        drivesData = r.data;
        ssd.localDrives = r.data.filter(d => !d.isCloud && d.root);
      } else if (r.status === 403) {
        drivesData = { error: apiError(r) };
      }
    } catch (e) { /* keep the last list; the tag shows offline */ }
    renderDrives();
    renderCloud();
  }
  function driveSpace(d) {
    if (!d.isMounted) return 'Not connected on the PC';
    if (d.totalBytes > 0) return `${fmtBytes(d.totalBytes - d.freeBytes)} used of ${fmtBytes(d.totalBytes)} · <strong>${fmtBytes(d.freeBytes)} free</strong>`;
    return 'Connected';
  }
  function driveCard(d, compact) {
    // Starting/Stopping/Failed come from the PC: it is working on the drive (Failed = it is retrying by itself).
    const busy = driveBusy[d.id] || (!d.isMounted && d.state === 'Starting' ? 'mount' : d.state === 'Stopping' ? 'unmount' : '');
    const retrying = !d.isMounted && d.state === 'Failed' && !busy;
    let actions = '';
    if (d.canToggle) {
      actions += d.isMounted
        ? `<button class="drive-btn unmount" data-act="drive-unmount" data-id="${esc(d.id)}" ${busy ? 'disabled' : ''}>${busy === 'unmount' ? '<span class="spinner"></span>Disconnecting…' : 'Disconnect'}</button>`
        : `<button class="drive-btn mount" data-act="drive-mount" data-id="${esc(d.id)}" ${busy ? 'disabled' : ''}>${busy === 'mount' ? '<span class="spinner"></span>Connecting…' : 'Connect'}</button>`;
    }
    if (d.isMounted && d.root) actions += `<button class="drive-btn browse" data-act="drive-browse" data-root="${esc(d.root)}">Browse files</button>`;
    return `<div class="drive-manager-card ${d.isMounted ? 'mounted' : ''}">
      <div class="drive-card-header">
        <span class="drive-letter-pill ${d.isCloud ? 'cloud' : 'local'}">${esc(d.driveLetter)}</span>
        <span class="drive-conn-badge ${d.isMounted ? 'mounted' : 'disconnected'}">${busy ? (busy === 'mount' ? 'Connecting…' : 'Disconnecting…') : d.isMounted ? '● Connected' : retrying ? 'PC is retrying…' : '○ Disconnected'}</span>
      </div>
      <div class="drive-card-name">${esc(d.displayName)}${d.readOnly ? ' <span class="ro-tag">read-only</span>' : ''}</div>
      <div class="drive-card-type">${esc(d.connectionType || '')}</div>
      ${d.isCloud && d.remoteSpec && !compact ? `<div class="drive-card-accounts">${esc(d.remoteSpec).split(' + ').join('<br>')}</div>` : ''}
      <div class="drive-card-space">${driveSpace(d)}</div>
      ${d.isMounted && d.totalBytes > 0 ? `<div class="usage-bar wide"><div style="width:${Math.max(1, pct(d.totalBytes - d.freeBytes, d.totalBytes)).toFixed(1)}%"></div></div>` : ''}
      <div class="drive-card-actions">${actions}</div>
    </div>`;
  }
  function renderDrives() {
    const box = $('drives-list-container');
    if (!box) return;
    if (link.state !== 'online') { box.innerHTML = emptyBlock(SVG.pc, 'PC is not connected', 'Drives are shown live from the PC.', 'Try again', 'toggle-link'); return; }
    if (!drivesData) { box.innerHTML = skeleton(3); return; }
    if (drivesData.error) { box.innerHTML = emptyBlock(SVG.pc, 'Switched off on the PC', drivesData.error); return; }
    const cloud = drivesData.filter(d => d.isCloud), local = drivesData.filter(d => !d.isCloud);
    setText('drives-sub', `${cloud.filter(d => d.isMounted).length} of ${cloud.length} cloud drives connected on ${link.pcName || 'the PC'}`);
    box.innerHTML = (cloud.length ? '<div class="sheet-subtitle">Cloud drives</div>' + cloud.map(d => driveCard(d)).join('')
      : emptyBlock(SVG.cloud, 'No cloud drive yet', 'Add a cloud account in Cloud-SSD on the PC.')) +
      (local.length ? '<div class="sheet-subtitle">Disks in the PC</div>' + local.map(d => driveCard(d)).join('') : '');
  }
  function renderCloud() {
    const st = link.status;
    const c = cloudTotals(st && st.cloud);
    const online = link.state === 'online';
    setText('cloud-live-tag', online ? 'Live from the PC' : 'PC offline');
    $('cloud-live-tag').className = 'conn-status-tag ' + (online ? 'connected' : 'offline');
    if (!online || !c) {
      ['cloud-val-used', 'cloud-val-free', 'cloud-val-total'].forEach(id => setText(id, '—'));
      $('cloud-bar').innerHTML = '';
      $('cloud-accounts').innerHTML = online ? '<div class="recent-empty-card">Update Cloud-SSD on the PC to see account sizes here.</div>' : '';
      $('cloud-drives').innerHTML = '';
      setText('cloud-note', online ? '' : 'Connect to the PC to see your cloud storage.');
      return;
    }
    setText('cloud-val-used', c.totalBytes > 0 ? fmtBytes(c.usedBytes) : '—');
    setText('cloud-val-free', c.totalBytes > 0 ? fmtBytes(c.freeBytes) : '—');
    setText('cloud-val-total', c.totalBytes > 0 ? fmtBytes(c.totalBytes) : '—');
    setText('cloud-sub', c.accountsCount + (c.accountsCount === 1 ? ' account' : ' accounts') + ' joined into one drive');
    const colors = ['#A8C7FA', '#6DD58C', '#FDD663', '#F48FB1', '#78D9EC', '#B39DDB'];
    const accounts = c.accounts || [];
    $('cloud-bar').innerHTML = c.totalBytes > 0
      ? accounts.filter(a => a.usedBytes > 0).map((a, i) => `<div class="bar-seg" style="width:${Math.max(0.8, pct(a.usedBytes, c.totalBytes)).toFixed(2)}%;background:${colors[i % colors.length]}"></div>`).join('')
      : '';
    $('cloud-accounts').innerHTML = accounts.length ? accounts.map((a, i) => `
      <div class="usage-row">
        <span class="usage-dot" style="background:${colors[i % colors.length]}"></span>
        <div class="usage-main">
          <div class="usage-name">${esc(a.name)}</div>
          <div class="usage-sub">${a.totalBytes > 0 ? `${fmtBytes(a.usedBytes)} used of ${fmtBytes(a.totalBytes)}` : a.reachable === false ? 'Could not be reached just now' : 'Reading size…'}</div>
          ${a.totalBytes > 0 ? `<div class="usage-bar"><div style="width:${Math.max(1, pct(a.usedBytes, a.totalBytes)).toFixed(1)}%;background:${colors[i % colors.length]}"></div></div>` : ''}
        </div>
        <div class="usage-val">${a.totalBytes > 0 ? fmtBytes(a.freeBytes) + '<small>free</small>' : '—'}</div>
      </div>`).join('') : '<div class="recent-empty-card">No cloud account is connected. Add one in Cloud-SSD on the PC.</div>';
    const cloudDrives = Array.isArray(drivesData) ? drivesData.filter(d => d.isCloud) : [];
    $('cloud-drives').innerHTML = cloudDrives.length ? cloudDrives.map(d => driveCard(d, true)).join('') : skeleton(2);
    setText('cloud-note', c.refreshedAt ? 'Sizes checked with the cloud ' + timeAgo(c.refreshedAt) + '. The PC re-checks while this is open.' : 'The PC is asking the cloud for sizes…');
  }
  async function toggleDrive(id, mount) {
    if (driveBusy[id]) return;
    const d = Array.isArray(drivesData) ? drivesData.find(x => x.id === id) : null;
    const label = d ? `${d.driveLetter} ${d.displayName}` : 'this drive';
    if (!mount) {
      const ok = await confirmDialog('Disconnect ' + label + '?',
        'The drive letter disappears on the PC until you connect it again. Programs using files on it will lose them; files still uploading are finished first.', 'Disconnect');
      if (!ok) return;
    }
    driveBusy[id] = mount ? 'mount' : 'unmount';
    renderDrives(); renderCloud();
    try {
      const r = await api(mount ? '/api/drives/mount' : '/api/drives/unmount', { method: 'POST', params: { id }, timeout: 120000 });
      if (r.ok && r.data && r.data.success) toast(r.data.message);
      // A failed connect is usually the cloud asking the PC to slow down; the PC retries by itself and this list follows it.
      else toast(apiError(r, 'The PC could not do that') + (mount ? ' The PC keeps trying; watch the status here.' : ''));
    } catch (e) {
      toast('PC did not answer');
    }
    delete driveBusy[id];
    await pollStatus();
    await loadDrives();
    if (activeTab === 'ssd' && ssd.path) loadSsd(ssd.path, true);
  }

  // ------------------------------------------------------------------ RAM sheet
  function renderRam() {
    if (!isApp) {
      $('ram-rows').innerHTML = '';
      setText('ram-note', 'Phone memory can only be read by the Cloud-SSD Android app.');
      return;
    }
    let d;
    try { d = JSON.parse(native.getPhoneRamBreakdown()); } catch (e) { return; }
    const total = d.totalBytes || 0;
    setText('ram-val-used', fmtBytes(d.usedBytes));
    setText('ram-val-free', fmtBytes(d.availableBytes));
    setText('ram-val-total', fmtBytes(total));
    setText('ram-device-sub', ([appInfo.manufacturer, appInfo.model].filter(Boolean).join(' ') || 'This phone') + (d.lowMemory ? ' · low on memory' : ''));
    const rows = d.detailed ? [
      { label: 'Apps & services', sub: 'Memory apps are holding right now', bytes: d.appsBytes, color: '#60A5FA' },
      { label: 'Cached files', sub: 'Kept to reopen things fast; freed when needed', bytes: d.cachedBytes, color: '#34D399' },
      { label: 'System & hardware', sub: 'Android, graphics and drivers', bytes: d.otherBytes, color: '#C084FC' },
      { label: 'Kernel', sub: 'Core of the operating system', bytes: d.kernelBytes, color: '#FBBF24' },
      { label: 'Free', sub: 'Not used by anything', bytes: d.freeBytes, color: 'rgba(255,255,255,0.28)' }
    ] : [
      { label: 'In use', sub: 'Everything except what Android can free', bytes: d.usedBytes, color: '#60A5FA' },
      { label: 'Available', sub: 'Free plus what Android can free', bytes: d.availableBytes, color: 'rgba(255,255,255,0.28)' }
    ];
    $('ram-bar').innerHTML = rows.map(r => `<div class="ram-seg" style="width:${pct(r.bytes, total).toFixed(2)}%;background:${r.color}"></div>`).join('');
    let html = rows.map(r => `
      <div class="usage-row">
        <span class="usage-dot" style="background:${r.color}"></span>
        <div class="usage-main"><div class="usage-name">${r.label}</div><div class="usage-sub">${r.sub}</div></div>
        <div class="usage-val">${fmtBytes(r.bytes)}<small>${Math.round(pct(r.bytes, total))}%</small></div>
      </div>`).join('');
    if (d.swapTotalBytes > 0) {
      html += `<div class="usage-row">
        <span class="usage-dot" style="background:#F472B6"></span>
        <div class="usage-main"><div class="usage-name">Extended RAM (swap)</div><div class="usage-sub">Storage used as extra memory</div>
          <div class="usage-bar"><div style="width:${pct(d.swapUsedBytes, d.swapTotalBytes).toFixed(1)}%;background:#F472B6"></div></div></div>
        <div class="usage-val">${fmtBytes(d.swapUsedBytes)}<small>of ${fmtBytes(d.swapTotalBytes)}</small></div>
      </div>`;
    }
    for (const p of (d.processes || [])) {
      html += `<div class="usage-row">
        <span class="usage-dot" style="background:#A8C7FA"></span>
        <div class="usage-main"><div class="usage-name">${esc(p.name)}</div><div class="usage-sub">${esc(p.pkg)}</div></div>
        <div class="usage-val">${fmtBytes(p.bytes)}</div>
      </div>`;
    }
    $('ram-rows').innerHTML = html;
    setText('ram-note', 'Read from the phone every second. Android only lets an app see its own memory by name, so other apps are counted together under "Apps & services".');
  }

  // ------------------------------------------------------------------ gaming
  async function pollHud() {
    if (link.state !== 'online') {
      ['hud-cpu-val', 'hud-gpu-val', 'hud-ram-val', 'hud-ssd-read', 'hud-cache-hit', 'hud-ping-val'].forEach(id => setText(id, '—'));
      setText('gaming-hero-title', 'PC Gaming Companion');
      setText('gaming-hero-sub', link.state === 'unpaired' ? 'Pair with your PC to see live numbers' : 'PC is not connected');
      return;
    }
    const num = (v, suffix) => typeof v === 'number' ? Math.round(v) + suffix : '—';
    try {
      const t0 = performance.now();
      const r = await api('/api/hud', { timeout: 4000 });
      const rtt = Math.round(performance.now() - t0);
      if (r.status === 403) { setText('gaming-hero-sub', apiError(r)); return; }
      if (!r.ok || !r.data) return;
      const d = r.data;
      setText('hud-cpu-val', num(d.cpuLoad, '%'));
      setText('hud-cpu-label', typeof d.cpuTempC === 'number' ? `CPU load · ${Math.round(d.cpuTempC)}°C` : 'CPU load');
      setText('hud-gpu-val', num(d.gpuLoad, '%'));
      setText('hud-ram-val', num(d.ramUsedPercent, '%'));
      setText('hud-ssd-read', typeof d.ssdReadMBps === 'number' ? d.ssdReadMBps.toFixed(1) : '—');
      setText('hud-cache-hit', num(d.cacheHitRate, '%'));
      setText('hud-ping-val', rtt + ' ms');
      setText('gaming-hero-title', d.isGaming ? d.activeGame : 'No game running');
      setText('gaming-hero-sub', d.isGaming ? 'Running on ' + (link.pcName || 'the PC') + ' from the cloud drive' : 'Live numbers from ' + (link.pcName || 'the PC'));
    } catch (e) { /* the status poll reports the outage */ }
  }
  const REMOTE_LABELS = { volup: 'Volume up', voldown: 'Volume down', mute: 'Mute toggled', playpause: 'Play / pause', next: 'Next track', prev: 'Previous track' };
  async function remote(key) {
    if (link.state !== 'online') { toast('PC is not connected'); return; }
    try {
      const r = await api('/api/remote', { method: 'POST', params: { action: key }, timeout: 5000 });
      if (r.ok) toast(REMOTE_LABELS[key] + ' on ' + (link.pcName || 'PC'));
      else if (r.status === 404) toast('Update Cloud-SSD on the PC to use the remote');
      else toast(apiError(r));
    } catch (e) { toast('PC did not answer'); }
  }
  async function loadSaves() {
    const box = $('game-saves-list');
    if (!box) return;
    if (link.state !== 'online') { box.innerHTML = '<div class="recent-empty-card">PC is not connected.</div>'; return; }
    try {
      const r = await api('/api/saves', { timeout: 15000 });
      if (r.ok && Array.isArray(r.data) && r.data.length) {
        box.innerHTML = r.data.slice(0, 50).map(s => `
          <div class="transfer-card-item"><div class="tc-left">
            <div class="tc-icon-wrap stream">${SVG.archive}</div>
            <div class="tc-info"><div class="tc-name">${esc(s.name)}</div><div class="tc-meta"><span>${esc(s.sizeFormatted || fmtBytes(s.size))}</span><span>${esc(fmtDate(s.modified))}</span></div></div>
          </div></div>`).join('');
      } else if (r.ok) {
        box.innerHTML = '<div class="recent-empty-card">No save backups on the PC yet. Turn on cloud save sync for a game in Cloud-SSD.</div>';
      } else {
        box.innerHTML = `<div class="recent-empty-card">${esc(apiError(r))}</div>`;
      }
    } catch (e) {
      box.innerHTML = '<div class="recent-empty-card">PC did not answer.</div>';
    }
  }

  // ------------------------------------------------------------------ search
  const search = { q: '', timer: null, seq: 0, phoneReq: -1, cloud: [], phone: [] };
  function onSearchInput(value) {
    const q = value.trim();
    show('clear-search-btn', value.length > 0);
    clearTimeout(search.timer);
    if (q.length < 2) { closeSearch(false); return; }
    search.timer = setTimeout(() => runSearch(q), 350);
  }
  function closeSearch(clearInput) {
    search.q = '';
    search.seq++;
    $('search-panel').classList.remove('active');
    document.body.classList.remove('searching');
    if (clearInput) { $('global-search-input').value = ''; show('clear-search-btn', false); $('global-search-input').blur(); }
  }
  async function runSearch(q) {
    search.q = q;
    const seq = ++search.seq;
    $('search-panel').classList.add('active');
    document.body.classList.add('searching');
    const cloudBox = $('search-cloud-results'), phoneBox = $('search-phone-results');
    show('search-phone-section', isApp);
    if (isApp) {
      phoneBox.innerHTML = skeleton(3);
      search.phoneReq = 1000000 + seq;
      try { native.searchPhone(search.phoneReq, q); } catch (e) { phoneBox.innerHTML = ''; }
    }
    if (link.state !== 'online') {
      cloudBox.innerHTML = '<div class="recent-empty-card">PC is not connected, so cloud files cannot be searched.</div>';
      setText('search-cloud-count', '0');
      return;
    }
    const pl = placeOf(ssd.path) || ssdPlaces().find(p => p.cloud && p.mounted);
    if (!pl) { cloudBox.innerHTML = '<div class="recent-empty-card">No cloud drive is connected.</div>'; return; }
    const where = ssd.path && placeOf(ssd.path) ? ssd.path : pl.root;
    setText('search-cloud-where', 'in ' + (where.length > 28 ? '…' + where.slice(-26) : where));
    cloudBox.innerHTML = skeleton(4);
    try {
      const r = await api('/api/files', { params: { path: where, search: q }, timeout: 30000 });
      if (seq !== search.seq) return;
      search.cloud = r.ok && r.data ? (r.data.files || []).map(f => Object.assign(f, { src: 'cloud' })) : [];
      setText('search-cloud-count', String(search.cloud.length));
      cloudBox.innerHTML = search.cloud.length ? search.cloud.map((f, i) => `
        <div class="explorer-item file-item" data-act="search-cloud" data-i="${i}">
          <div class="item-icon kind-${kindOf(f.name)}">${iconFor(f.name)}</div>
          <div class="item-details"><div class="item-name">${esc(f.name)}</div><div class="item-meta">${fmtBytes(f.size)} · ${esc(f.path.slice(0, f.path.lastIndexOf('\\')))}</div></div>
        </div>`).join('') + (r.data.truncated ? '<div class="list-note">More matches exist; open a folder to search inside it.</div>' : '')
        : `<div class="recent-empty-card">${r.ok ? 'No file named like that here (searches 3 folders deep).' : esc(apiError(r))}</div>`;
    } catch (e) {
      if (seq === search.seq) cloudBox.innerHTML = '<div class="recent-empty-card">PC did not answer.</div>';
    }
  }
  function renderSearchPhone(d) {
    search.phone = (d.files || []).map(f => Object.assign(f, { src: 'phone' }));
    setText('search-phone-count', String(search.phone.length));
    $('search-phone-results').innerHTML = search.phone.length ? search.phone.map((f, i) => `
      <div class="phone-item-row" data-act="search-phone" data-i="${i}">
        <div class="phone-item-icon">${iconFor(f.name)}</div>
        <div class="phone-item-info"><div class="phone-item-name">${esc(f.name)}</div><div class="phone-item-meta">${fmtBytes(f.size)} · ${esc(prettyPhonePath(f.path.slice(0, f.path.lastIndexOf('/'))))}</div></div>
      </div>`).join('') : '<div class="recent-empty-card">No file named like that on this phone.</div>';
  }

  // ------------------------------------------------------------------ tabs
  function switchTab(tab) {
    closeSearch(true);
    activeTab = tab;
    document.querySelectorAll('.tab-screen').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.nav-tab').forEach(el => el.classList.remove('active'));
    $('tab-' + tab).classList.add('active');
    $('nav-' + tab).classList.add('active');
    document.body.dataset.tab = tab;
    window.scrollTo(0, 0);
    if (tab === 'home') { renderPhoneTiles(); refreshHistory(); }
    else if (tab === 'browser') openPhoneTab();
    else if (tab === 'ssd') openSsdTab();
    else if (tab === 'gaming') { pollHud(); loadSaves(); }
    renderTransfers();
  }
  function openPhoneTab() {
    show('phone-webonly-note', !isApp);
    show('phone-home', isApp && !phone.view);
    if (!isApp) return;
    try { phone.access = native.getStorageAccess(); } catch (e) { }
    renderPermBanner();
    renderStorage();
    requestScan();
  }

  // ------------------------------------------------------------------ actions (one click handler for the whole page)
  const actions = {
    'tab': (el) => switchTab(el.dataset.tab),
    'search-clear': () => closeSearch(true),
    'open-connection': () => { renderLink(); openSheet('connection-modal'); },
    'toggle-link': () => {
      if (link.state === 'online') { setLinkState('paused'); renderStatusTiles(); toast('Disconnected from ' + (link.pcName || 'PC')); }
      else if (!link.token || !link.server) { closeSheet('connection-modal'); showPair(''); }
      else { link.state = 'init'; renderLink(); toast('Connecting…'); pollStatus(); }
    },
    'open-pair': () => showPair(''),
    'save-address': () => {
      const v = normalizeServer($('manual-server-ip').value);
      if (!v) return;
      saveCreds(v, link.token);
      link.state = 'init';
      renderLink();
      toast('Trying ' + v.replace(/^https?:\/\//, ''));
      if (link.token) pollStatus(); else { closeSheet('connection-modal'); $('pair-address').value = v.replace(/^https?:\/\//, ''); showPair(''); }
    },
    'find-pc': () => { if (isApp) { toast('Searching this Wi-Fi…'); link.state = 'offline'; startDiscovery('relocate'); } else toast('Only in the Android app'); },
    'unpair': async () => {
      if (!await confirmDialog('Unpair this phone?', 'You will need the pairing code from the PC to connect again.', 'Unpair')) return;
      closeSheet('connection-modal');
      onUnpaired();
    },
    'pair-pick': (el) => { pair.selected = el.dataset.url; $('pair-address').value = ''; renderPairPcs(); },
    'pair-rescan': () => {
      const typed = normalizeServer($('pair-address').value);
      if (typed) { pair.selected = ''; submitPair(); } else if (isApp) startDiscovery('pair');
    },
    'pair-submit': () => submitPair(),
    'pair-close': () => hidePair(),

    'open-ram': () => { renderRam(); openSheet('ram-breakdown-modal'); },
    'open-storage': () => {
      if (!isApp) { toast('Phone storage is shown in the Android app'); return; }
      renderStorage(); requestScan(); openSheet('storage-modal');
    },
    'open-cloud': () => { renderCloud(); openSheet('cloud-modal'); loadDrives(); },
    'open-drives': () => { renderDrives(); openSheet('drives-manager-modal'); loadDrives(); },
    'open-transfers': () => { renderHistory(); renderTransfers(); openSheet('transfers-modal'); refreshHistory(); },
    'transfers-filter': (el) => {
      historyFilter = el.dataset.filter;
      document.querySelectorAll('#transfers-filter-row .transfer-chip').forEach(c => c.classList.toggle('active', c === el));
      renderHistory();
    },
    'history-open': (el) => {
      const t = xferHistory[+el.dataset.i];
      if (!t || !t.filePath || t.direction === 'offload') return;
      openFileSheet({ name: t.fileName, path: t.filePath, size: t.bytes, modified: t.timestamp, src: 'cloud' });
    },
    'xfer-cancel': (el) => cancelTransfer(+el.dataset.id),
    'send': (el) => sendFiles(el.dataset.kind),
    'offload-now': async () => {
      if (link.state !== 'online') { toast('PC is not connected'); return; }
      if (!await confirmDialog('Free up space on the laptop?', 'Cloud-SSD moves old files from the laptop to the cloud drive now. They stay available through the cloud drive.', 'Start')) return;
      toast('The PC is moving files…');
      try {
        const r = await api('/api/storage/offload-now', { method: 'POST', timeout: 300000 });
        toast(r.data && r.data.message ? r.data.message : apiError(r));
      } catch (e) { toast('PC did not answer'); }
      pollStatus(); refreshHistory();
    },

    'grant-storage': () => {
      if (!isApp) return;
      // Android 11+: one switch ("All files access") covers photos, documents and folders. Older: the classic dialog.
      try { if (appInfo.sdk >= 30) native.requestAllFilesAccess(); else native.requestAllPermissions(); } catch (e) { }
    },
    'phone-rescan': () => { requestScan(); toast('Reading phone storage…'); },
    'phone-cat': (el) => {
      if (!isApp) return;
      closeSheet('storage-modal');
      if (activeTab !== 'browser') switchTab('browser');
      phone.stack = []; phone.view = null; phone.grid = null;
      phoneOpen({ type: 'cat', cat: el.dataset.cat, title: el.dataset.title });
    },
    'phone-starred': () => { phone.stack = []; phone.view = null; phone.grid = null; phoneOpen({ type: 'starred', title: 'Starred' }); },
    'phone-dir': (el) => {
      if (!isApp) { toast('Only in the Android app'); return; }
      closeSheet('storage-modal');
      if (activeTab !== 'browser') switchTab('browser');
      phone.stack = []; phone.view = null; phone.grid = null;
      phoneOpen({ type: 'dir', path: el.dataset.path || '', title: 'Internal storage' });
    },
    'phone-folder': (el) => { const fo = phone.items.folders[+el.dataset.i]; if (fo) phoneOpen({ type: 'dir', path: fo.path, title: fo.name, titled: true }); },
    'phone-file': (el) => {
      const i = +el.dataset.i, f = phone.items.files[i];
      if (!f) return;
      if (phone.selecting) {
        if (phone.selected.has(i)) phone.selected.delete(i); else phone.selected.add(i);
        el.classList.toggle('selected', phone.selected.has(i));
        renderSelectBar();
      } else openFileSheet(f);
    },
    'phone-send': (el) => { const f = phone.items.files[+el.dataset.i]; if (f) sendPhoneFiles([f]); },
    'phone-back': () => phoneBack(),
    'phone-layout': () => { phone.grid = !phoneGrid(); renderPhoneItems({}); },
    'phone-select': () => { phone.selecting = !phone.selecting; phone.selected.clear(); renderSelectBar(); renderPhoneItems({}); },
    'phone-select-cancel': () => { phone.selecting = false; phone.selected.clear(); renderSelectBar(); renderPhoneItems({}); },
    'phone-select-send': () => {
      const files = Array.from(phone.selected).map(i => phone.items.files[i]).filter(f => f && f.src === 'phone');
      if (!files.length) { toast('Tap files to select them'); return; }
      sendPhoneFiles(files);
      phone.selecting = false; phone.selected.clear(); renderSelectBar(); renderPhoneItems({});
    },

    'ssd-place': (el) => loadSsd(el.dataset.root),
    'ssd-crumb': (el) => loadSsd(el.dataset.path),
    'ssd-up': () => ssdUp(),
    'ssd-refresh': () => { if (link.state !== 'online') pollStatus(); else if (ssd.path) loadSsd(ssd.path, true); else openSsdTab(); },
    'ssd-layout': () => { ssd.grid = !ssd.grid; ls.set('cloudssd_layout', ssd.grid ? 'grid' : 'list'); const c = ssd.cache.get(ssd.path); if (c) renderSsd(c); },
    'ssd-sort': () => {
      $('sort-options').innerHTML = SORTS.map(s => `<div class="m3-sheet-row ${s.key === ssd.sort ? 'chosen' : ''}" data-act="ssd-sort-pick" data-key="${s.key}">${s.key === ssd.sort ? SVG.check : '<svg viewBox="0 0 24 24"></svg>'}<div class="m3-sheet-row-text">${s.label}</div></div>`).join('');
      openSheet('sort-sheet');
    },
    'ssd-sort-pick': (el) => { ssd.sort = el.dataset.key; ls.set('cloudssd_sort', ssd.sort); closeSheet('sort-sheet'); const c = ssd.cache.get(ssd.path); if (c) renderSsd(c); },
    'ssd-show-all': () => { ssd.showAll = true; const c = ssd.cache.get(ssd.path); if (c) renderSsd(c); },
    'ssd-folder': (el) => { const fo = ssd.view.folders[+el.dataset.i]; if (fo) loadSsd(fo.path); },
    'ssd-file': (el) => { const f = ssd.view.files[+el.dataset.i]; if (f) openFileSheet(f); },
    'ssd-more': (el) => { const f = ssd.view.files[+el.dataset.i]; if (f) openFileSheet(f); },
    'search-cloud': (el) => { const f = search.cloud[+el.dataset.i]; if (f) openFileSheet(f); },
    'search-phone': (el) => { const f = search.phone[+el.dataset.i]; if (f) openFileSheet(f); },

    'drive-mount': (el) => toggleDrive(el.dataset.id, true),
    'drive-unmount': (el) => toggleDrive(el.dataset.id, false),
    'drive-browse': (el) => {
      ['drives-manager-modal', 'cloud-modal'].forEach(closeSheet);
      if (activeTab !== 'ssd') { ssd.path = el.dataset.root; switchTab('ssd'); } else loadSsd(el.dataset.root);
    },

    'remote': (el) => remote(el.dataset.key),
    'saves-refresh': () => loadSaves(),

    'sheet-action': (el) => {
      const a = sheetActions[+el.dataset.i];
      if (!a) return;
      if (!a.keep) closeSheet('file-actions-sheet');
      a.fn();
    },
    'viewer-more': () => { if (viewerFile) { const f = viewerFile; closeSheet('file-viewer-modal'); openFileSheet(f); } },
    'viewer-external': () => { if (viewerFile) openExternally(viewerFile); },
    'viewer-download': () => { if (viewerFile) downloadToPhone(viewerFile); },
    'sheet-close': (el, e) => { if (e.target === el) closeSheet(el.id); },
    'sheet-close-btn': (el) => { const s = el.closest('.modal-backdrop, .m3-bottom-sheet-backdrop'); if (s) closeSheet(s.id); },
    'confirm-yes': () => { const r = confirmResolve; confirmResolve = null; closeSheet('confirm-modal'); if (r) r(true); },
    'confirm-no': () => closeSheet('confirm-modal'),
    'transfer-pill': () => actions['open-transfers']()
  };

  document.addEventListener('click', (e) => {
    const el = e.target.closest('[data-act]');
    if (!el || el.disabled) return;
    const fn = actions[el.dataset.act];
    if (fn) fn(el, e);
  });

  // ------------------------------------------------------------------ Android hooks
  window.onAndroidBack = function () {
    if (openSheets.length) { closeSheet(openSheets[openSheets.length - 1]); return true; }
    if ($('pair-overlay').style.display !== 'none') { hidePair(); return true; }
    if (document.body.classList.contains('searching') || $('global-search-input').value) { closeSearch(true); return true; }
    if (activeTab === 'browser' && phoneBack()) return true;
    if (activeTab === 'ssd' && link.state === 'online' && ssdUp()) return true;
    if (activeTab !== 'home') { switchTab('home'); return true; }
    return false;
  };

  /** Results from the app's native side. */
  window.onNative = function (event, data) {
    try {
      switch (event) {
        case 'transfer': onTransfer(data); break;
        case 'discovery': onDiscovery(data); break;
        case 'storage': onStorageReport(data); break;
        case 'list': onPhoneList(data); break;
        case 'pair': if (applyPairLink(data)) toast('Paired with your PC'); break;
        case 'resume':
          if (data.storageAccess !== phone.access) {
            phone.access = data.storageAccess;
            renderPermBanner();
            requestScan();
            if (phone.view && activeTab === 'browser') phoneShow(phone.view);
          }
          refreshAppInfo();
          if (link.state !== 'paused' && link.state !== 'unpaired') pollStatus();
          break;
      }
    } catch (err) {
      console.error('onNative ' + event + ': ' + (err && err.stack || err));
    }
  };

  function refreshAppInfo() {
    if (!isApp) return;
    try { appInfo = JSON.parse(native.getAppInfo()); } catch (e) { appInfo = {}; }
    setText('modal-app-version', `Cloud-SSD app ${appInfo.version || ''} · ${[appInfo.manufacturer, appInfo.model].filter(Boolean).join(' ')} · Android ${appInfo.android || ''}`);
  }

  // ------------------------------------------------------------------ the clock: everything "live" runs from here
  let ticks = 0;
  function tick() {
    if (document.hidden) return;
    ticks++;
    renderUptime();
    if (ticks % 3 === 0) pollStatus();
    if (activeTab === 'home' && ticks % 2 === 0) renderPhoneTiles();
    if (isOpen('ram-breakdown-modal')) renderRam();
    if (isOpen('storage-modal')) { renderPhoneTiles(); if (ticks % 15 === 0) requestScan(); }
    if ((isOpen('drives-manager-modal') || isOpen('cloud-modal')) && ticks % 4 === 0 && !Object.keys(driveBusy).length) loadDrives();
    if (activeTab === 'gaming' && ticks % 2 === 0) pollHud();
    if (activeTab === 'home' && ticks % 20 === 0) refreshHistory();
    if (activeTab === 'browser' && !phone.view && ticks % 30 === 0) requestScan();
  }

  // ------------------------------------------------------------------ start
  function boot() {
    document.body.dataset.tab = 'home';
    document.body.classList.toggle('in-app', isApp);
    refreshAppInfo();

    // A leftover service worker from old versions served stale files: remove it.
    if ('serviceWorker' in navigator && /^https?:/.test(location.protocol)) {
      navigator.serviceWorker.getRegistrations().then(rs => rs.forEach(r => r.unregister())).catch(() => { });
    }

    // Opened from the PC's QR code in a browser: http://<pc>:8090/?token=…
    const params = new URLSearchParams(location.search);
    const urlToken = params.get('token');
    if (/^https?:/.test(location.protocol)) {
      if (urlToken) {
        saveCreds(location.origin, urlToken);
        try { window.history.replaceState(null, '', location.pathname); } catch (e) { }
        if (!isApp) {
          // The same phone may have the app installed: offer to hand the pairing over to it.
          $('open-in-app-link').href = `intent://pair?host=${encodeURIComponent(location.host)}&token=${encodeURIComponent(urlToken)}#Intent;scheme=cloudssd;package=com.cloudssd.mobile;end`;
          if (/Android/i.test(navigator.userAgent)) show('browser-app-banner', true);
        }
      } else if (!link.server) {
        link.server = location.origin;
      }
    }
    if (isApp) {
      try {
        const pending = native.consumePairLink();
        if (pending) applyPairLink(JSON.parse(pending));
      } catch (e) { }
      try { native.setServerBase(link.server, link.token); } catch (e) { }
      try { phone.access = native.getStorageAccess(); } catch (e) { }
    }

    $('global-search-input').addEventListener('input', (e) => onSearchInput(e.target.value));
    $('pair-code').addEventListener('input', (e) => {
      const d = e.target.value.replace(/\D/g, '').slice(0, 6);
      e.target.value = d.length > 3 ? d.slice(0, 3) + ' ' + d.slice(3) : d;
    });
    $('pair-code').addEventListener('keydown', (e) => { if (e.key === 'Enter') submitPair(); });
    for (const id of ['file-uploader', 'photo-uploader']) {
      $(id).addEventListener('change', (e) => {
        Array.from(e.target.files || []).forEach(uploadInBrowser);
        e.target.value = '';
      });
    }
    document.addEventListener('visibilitychange', () => { if (!document.hidden && link.state !== 'paused' && link.state !== 'unpaired') pollStatus(); });

    renderLink();
    renderStatusTiles();
    renderPhoneTiles();
    renderPermBanner();
    renderStorage();
    renderHistory();
    setText('cat-stat-starred', starred.length ? starred.length + ' files' : 'None yet');

    if (link.server && link.token) pollStatus();
    else { link.state = 'unpaired'; renderLink(); renderStatusTiles(); showPair(''); }
    setInterval(tick, 1000);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
