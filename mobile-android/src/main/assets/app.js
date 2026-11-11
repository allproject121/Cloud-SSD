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
    // In the app the key lives encrypted on the phone (native side); a browser keeps it in its own storage.
    server: (ls.get('cloudssd_server_url') || '').replace(/\/+$/, ''),
    token: isApp ? '' : (ls.get('cloudssd_token') || ''),
    lan: (ls.get('cloudssd_server_url') || '').replace(/\/+$/, ''),   // the address it was paired on (home)
    away: ls.json('cloudssd_away_urls', []),                          // Tailscale addresses the PC reported
    pcId: ls.get('cloudssd_pc_id') || '',
    route: 'lan',         // lan | tailnet
    state: 'init',        // init | unpaired | offline | online | paused
    status: null,         // last /api/status from the PC
    pcName: ls.get('cloudssd_pc_name') || '',
    since: 0,
    ping: null,
    fails: 0,
    upgrading: false
  };
  let appInfo = {};
  let activeTab = 'home';

  /** True when the PC told us it can do this (an older PC simply lacks newer features). */
  function pcHas(f) { return !!(link.status && Array.isArray(link.status.features) && link.status.features.includes(f)); }

  /** A random id for this phone (or browser), so pairing again updates its entry on the PC instead of adding one. */
  function installId() {
    if (appInfo.installId) return appInfo.installId;
    let id = ls.get('cloudssd_install_id');
    if (!id) {
      const b = new Uint8Array(16);
      (window.crypto || {}).getRandomValues ? crypto.getRandomValues(b) : b.forEach((_, i) => { b[i] = Math.random() * 256; });
      id = Array.from(b, x => x.toString(16).padStart(2, '0')).join('');
      ls.set('cloudssd_install_id', id);
    }
    return id;
  }
  function deviceName() {
    return appInfo.model ? `${appInfo.manufacturer || ''} ${appInfo.model}`.trim() : 'Phone browser';
  }

  function normalizeServer(input) {
    let v = String(input || '').trim().replace(/\/+$/, '');
    if (!v) return '';
    if (!/^https?:\/\//i.test(v)) v = 'http://' + v;
    if (!/:\d+$/.test(v.replace(/^https?:\/\//i, ''))) v += ':8090';
    return v;
  }
  /** Remembers the PC and this phone's key. {@code server} is the home address it was paired on. */
  function saveCreds(server, token) {
    link.server = (server || '').replace(/\/+$/, '');
    link.lan = link.server;
    link.route = 'lan';
    link.token = token || '';
    ls.set('cloudssd_server_url', link.server);
    if (isApp) {
      ls.del('cloudssd_token');
      try { native.setServerBase(link.server, link.token); } catch (e) { }
    } else if (link.token) ls.set('cloudssd_token', link.token); else ls.del('cloudssd_token');
  }
  /** Switches the address in use (home ↔ Tailscale) without touching the key or the paired address. */
  function useServer(url, route) {
    link.server = url;
    link.route = route;
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
    // The PC went away: the Cloud SSD tab carries on from Google Drive when this phone is signed in.
    if (state !== 'online' && was === 'online' && activeTab === 'ssd') openSsdTab();
  }

  async function pollStatus() {
    if (link.state === 'paused' || !link.server || !link.token) return;
    const t0 = performance.now();
    try {
      const r = await api('/api/status', { timeout: 4000 });
      if (r.ok && r.data && r.data.isPaired) {
        link.ping = Math.round(performance.now() - t0);
        onStatus(r.data);
        setLinkState('online');
        renderLink();
        renderStatusTiles();
        // At home again while on Tailscale: switch back to the faster, free home address.
        if (link.route === 'tailnet' && link.lan && (ticks % 30 === 0)) tryHome();
      } else if (r.ok && r.data && r.data.isPaired === false) {
        onUnpaired();
      } else if (r.status === 403 && r.data && r.data.code) {
        onOffline(r.data.error);
      } else if (r.status !== 401) {
        onOffline();
      }
    } catch (e) {
      onOffline();
    }
  }

  /** What the PC reports: remembered, and handed to the native side (Files app, camera backup, transfers). */
  function onStatus(st) {
    link.status = st;
    if (st.pcName && st.pcName !== link.pcName) { link.pcName = st.pcName; ls.set('cloudssd_pc_name', link.pcName); }
    if (st.pcId && st.pcId !== link.pcId) { link.pcId = st.pcId; ls.set('cloudssd_pc_id', st.pcId); }
    const port = (/:(\d+)$/.exec(link.lan || link.server) || [])[1] || '8090';
    const away = st.away && st.away.allowed ? (st.away.ips || []).map(ip => `http://${ip.includes(':') ? '[' + ip + ']' : ip}:${port}`)
      .concat(st.away.dnsName ? [`http://${st.away.dnsName}:${port}`] : []) : [];
    if (JSON.stringify(away) !== JSON.stringify(link.away)) { link.away = away; ls.set('cloudssd_away_urls', JSON.stringify(away)); }
    if (isApp) {
      try { native.setServerCandidates(JSON.stringify({ lan: link.lan, away: link.away, pcId: link.pcId })); } catch (e) { }
      try { native.setPcInfo(JSON.stringify({ features: st.features || [], backupCustom: !!(st.backup && st.backup.custom) })); } catch (e) { }
    }
    // A phone paired before per-phone keys gets its own key once, without the user doing anything.
    if (st.auth === 'legacy' && pcHas('device-tokens') && !link.upgrading) upgradeKey();
  }
  async function upgradeKey() {
    link.upgrading = true;
    try {
      const r = await api('/api/pair/upgrade', { method: 'POST', params: { installId: installId(), name: deviceName() } });
      if (r.ok && r.data && r.data.token) {
        const server = link.server, route = link.route;
        saveCreds(link.lan || server, r.data.token);
        useServer(server, route);
      }
    } catch (e) { /* tried again at the next status */ }
    link.upgrading = false;
  }

  /** The address answers as our PC (its public id), checked before the key is sent there. */
  async function isOurPc(url) {
    try {
      const ctrl = new AbortController();
      const t = setTimeout(() => ctrl.abort(), 3500);
      const res = await fetch(url + '/api/status', { cache: 'no-store', signal: ctrl.signal });
      clearTimeout(t);
      const d = await res.json();
      return !!(d && d.pcName && (!link.pcId || !d.pcId || d.pcId === link.pcId));
    } catch (e) { return false; }
  }
  /** Home address down: try the PC's Tailscale addresses (away from home, or Wi-Fi without the PC). */
  async function tryAway() {
    for (const url of link.away) {
      if (url === link.server) continue;
      if (await isOurPc(url)) {
        useServer(url, 'tailnet');
        toast('Reaching ' + (link.pcName || 'your PC') + ' through Tailscale');
        pollStatus();
        return true;
      }
    }
    return false;
  }
  async function tryHome() {
    if (!link.lan || link.lan === link.server) return;
    if (await isOurPc(link.lan)) { useServer(link.lan, 'lan'); pollStatus(); }
  }

  function onOffline(reason) {
    if (link.state === 'paused' || link.state === 'unpaired') return;
    link.fails++;
    link.ping = null;
    link.offlineWhy = reason || '';
    setLinkState('offline');
    renderLink();
    renderStatusTiles();
    if (link.fails === 1 || link.fails % 5 === 0) {
      if (link.route === 'tailnet' && link.lan) tryHome();
      else if (link.away.length) tryAway();
    }
    // The PC's address changes when the router hands out a new one: look for it again on this Wi-Fi.
    if (isApp && (link.fails === 2 || link.fails % 20 === 0) && !(appInfo.network && appInfo.network.wifi === false)) startDiscovery('relocate');
  }
  function onUnpaired() {
    if (link.state === 'unpaired') return;
    link.token = '';
    ls.del('cloudssd_token');
    if (isApp) { try { native.setServerBase(link.lan || link.server, ''); } catch (e) { } }
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
    const metered = appInfo.network && appInfo.network.metered;
    const via = link.route === 'tailnet' ? ' · via Tailscale' + (metered ? ' (mobile data)' : '') : ' · local Wi-Fi, no mobile data';
    const subs = {
      init: link.server.replace(/^https?:\/\//, ''),
      online: link.server.replace(/^https?:\/\//, '') + via,
      offline: link.offlineWhy || (link.away.length
        ? 'Is Cloud-SSD open on the PC? At home: same Wi-Fi. Away: Tailscale on in this phone.'
        : 'Is Cloud-SSD open on the PC, and both on the same Wi-Fi?') + (cloudSource() === 'drive' ? ' Google Drive works meanwhile.' : ''),
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
    setText('modal-pc-os', (link.status && link.status.os ? link.status.os : 'Cloud-SSD') + (link.status && link.status.version ? ' · Cloud-SSD ' + link.status.version : ''));
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

  // ---- the PC's RAM and Windows disk on Home (live; the last numbers with their time when the PC is away) ----
  let pcLive = ls.json('cloudssd_pc_live', null);
  let pcLiveBusy = false;
  async function refreshPcLive() {
    if (pcLiveBusy || link.state !== 'online' || !pcHas('space')) { renderPcTiles(); return; }
    pcLiveBusy = true;
    try {
      const r = await api('/api/space/live', { timeout: 4000 });
      if (r.ok && r.data) { pcLive = Object.assign({ seen: Date.now() }, r.data); ls.set('cloudssd_pc_live', JSON.stringify(pcLive)); }
    } catch (e) { /* the last numbers stay */ }
    pcLiveBusy = false;
    renderPcTiles();
  }
  function renderPcTiles() {
    const live = link.state === 'online' && pcHas('space') && pcLive && Date.now() - pcLive.seen < 15000;
    if (!pcLive) {
      setText('hw-pcram-pct', link.state === 'online' ? (pcHas('space') ? '…' : 'Update PC') : 'PC offline'); setText('hw-pcram-val', '—'); setWidth('hw-pcram-bar', 0);
      setText('hw-pcdisk-free', link.state === 'online' ? '…' : 'PC offline'); setText('hw-pcdisk-val', '—'); setWidth('hw-pcdisk-bar', 0);
      return;
    }
    const ram = pcLive.ram || {}, disk = pcLive.disk || {};
    const ramUsed = ram.total - ram.available, diskUsed = disk.total - disk.free;
    setText('hw-pcram-pct', live ? Math.round(pct(ramUsed, ram.total)) + '%' : 'seen ' + timeAgo(pcLive.seen));
    setText('hw-pcram-val', `${fmtBytes(ramUsed)} / ${fmtBytes(ram.total)}`);
    setWidth('hw-pcram-bar', pct(ramUsed, ram.total));
    setText('hw-pcdisk-free', live ? fmtBytes(disk.free) + ' free' : 'seen ' + timeAgo(pcLive.seen));
    setText('hw-pcdisk-val', `${fmtBytes(diskUsed)} / ${fmtBytes(disk.total)}`);
    setWidth('hw-pcdisk-bar', pct(diskUsed, disk.total));
    ['hw-pcram-pct', 'hw-pcdisk-free'].forEach(id => { const el = $(id); if (el) el.classList.toggle('stale', !live); });
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
      const accs = cloudSource() === 'drive' ? (drive.accounts || []).filter(a => a.totalBytes > 0) : [];
      if (accs.length) {
        const total = accs.reduce((s, a) => s + a.totalBytes, 0), free = accs.reduce((s, a) => s + (a.freeBytes || 0), 0);
        setText('hw-ssd-badge', fmtBytes(free) + ' free'); setText('hw-ssd-val', `${fmtBytes(total - free)} / ${fmtBytes(total)} · Google`);
        setWidth('hw-ssd-bar', pct(total - free, total));
      } else { setText('hw-ssd-badge', why); setText('hw-ssd-val', '—'); setWidth('hw-ssd-bar', 0); }
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
    'drives-manager-modal', 'transfers-modal', 'sort-sheet', 'confirm-modal', 'settings-modal', 'details-sheet', 'choice-sheet'];
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
    if (confirmResolve) { confirmResolve(false); confirmResolve = null; }
    openSheet('confirm-modal');
    return new Promise(resolve => { confirmResolve = resolve; });
  }

  /** A short list of choices in a bottom sheet: [{icon, title, sub, fn}]. */
  let choices = [];
  function choiceSheet(title, list) {
    choices = list;
    setText('choice-title', title);
    $('choice-list').innerHTML = list.map((a, i) => `
      <div class="m3-sheet-row ${a.danger ? 'danger' : ''}" data-act="choice-pick" data-i="${i}">
        ${a.icon || ''}
        <div class="m3-sheet-row-text"><div>${esc(a.title)}</div>${a.sub ? `<div class="m3-sheet-row-sub">${esc(a.sub)}</div>` : ''}</div>
      </div>`).join('');
    openSheet('choice-sheet');
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
    try {
      const res = await fetch(`${url}/api/pair?code=${code}&name=${encodeURIComponent(deviceName())}&installId=${installId()}`, { method: 'POST', cache: 'no-store' });
      let data = {};
      try { data = await res.json(); } catch (e) { }
      if (res.ok && data.token) {
        saveCreds(url, data.token);
        if (data.pcName) { link.pcName = data.pcName; ls.set('cloudssd_pc_name', data.pcName); }
        if (data.pcId) { link.pcId = data.pcId; ls.set('cloudssd_pc_id', data.pcId); }
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
  /**
   * A pairing link: {host, pair} (the PC's one-time QR secret, swapped here for this phone's own key) or, from an
   * older PC, {host, token}.
   */
  async function applyPairLink(o) {
    if (!o || !o.host || !(o.token || o.pair)) return false;
    const url = normalizeServer(o.host);
    if (o.pair) {
      try {
        const res = await fetch(`${url}/api/pair?pair=${encodeURIComponent(o.pair)}&name=${encodeURIComponent(deviceName())}&installId=${installId()}`, { method: 'POST', cache: 'no-store' });
        const data = await res.json().catch(() => ({}));
        if (!res.ok || !data.token) { showPair(data.error || 'This pairing link could not be used. Enter the 6-digit code instead.'); return false; }
        saveCreds(url, data.token);
        if (data.pcName) { link.pcName = data.pcName; ls.set('cloudssd_pc_name', data.pcName); }
        if (data.pcId) { link.pcId = data.pcId; ls.set('cloudssd_pc_id', data.pcId); }
      } catch (e) { showPair('Could not reach the PC. Is this the same Wi-Fi?'); return false; }
    } else saveCreds(url, o.token);
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
    if (t.state === 'waiting' && t.retryIn != null) cur.retryAt = Date.now() + t.retryIn * 1000;
    transfers.set(t.id, cur);
    const was = prev.state;
    if (t.state === 'done' && was !== 'done') {
      // Many files at once: one toast for the batch instead of one per file.
      const running = Array.from(transfers.values()).filter(x => x.state === 'active' || x.state === 'queued').length;
      if (running >= 2) xferDoneBatch++;
      else {
        toast(xferDoneBatch > 0 ? `${xferDoneBatch + 1} files done` : (cur.note ? cur.note + ': ' : (cur.type === 'upload' ? 'Sent to PC: ' : 'Saved to Downloads: ')) + cur.name);
        xferDoneBatch = 0;
      }
      setTimeout(() => { transfers.delete(t.id); renderTransfers(); }, 4000);
      setTimeout(refreshHistory, 600);
      if (cur.type === 'download') phone.report = null; // a new file landed on the phone
    } else if (t.state === 'cancelled') {
      setTimeout(() => { transfers.delete(t.id); renderTransfers(); }, 2500);
    } else if (t.state === 'failed' && was !== 'failed') {
      toast((cur.type === 'upload' ? 'Could not send ' : 'Could not download ') + cur.name + (cur.canRetry !== false ? ' — tap Retry' : ''));
    }
    renderTransfers();
  }
  let xferDoneBatch = 0;

  function transferLine(t) {
    const dir = t.type === 'upload' ? 'To PC' : 'To phone';
    if (t.state === 'queued') return dir + ' · waiting…';
    if (t.state === 'done') return dir + ' · done · ' + fmtBytes(t.bytes);
    if (t.state === 'cancelled') return dir + ' · cancelled';
    if (t.state === 'waiting') {
      const s = t.retryAt ? Math.max(0, Math.round((t.retryAt - Date.now()) / 1000)) : 0;
      return 'Connection lost · trying again' + (s > 0 ? ' in ' + s + ' s' : '…');
    }
    if (t.state === 'failed') {
      const where = t.total > 0 && t.bytes > 0 ? `Stopped at ${Math.floor(pct(t.bytes, t.total))}% · ` : '';
      return where + (t.error || 'Failed') + (t.resumable ? ' · Retry continues from there' : '');
    }
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
            <button class="xfer-retry" data-act="xfer-retry" data-id="${t.id}" title="Retry">Retry</button>
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
        card.querySelector('.xfer-retry').style.display = t.state === 'failed' && t.canRetry !== false ? '' : 'none';
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
  function randomHex(n) {
    const b = new Uint8Array(n);
    crypto.getRandomValues(b);
    return Array.from(b, x => x.toString(16).padStart(2, '0')).join('');
  }
  /**
   * Browser (no app): upload with XMLHttpRequest so the same live progress card can be shown. On a PC that can
   * continue uploads, a broken one goes on from where it stopped when Retry is tapped.
   */
  async function uploadInBrowser(file, existingId) {
    const id = existingId || --localXferId;
    const prev = transfers.get(id) || {};
    const uploadId = prev.uploadId || (pcHas('upload-resume') ? randomHex(16) : '');
    onTransfer({ id, type: 'upload', name: file.name, state: 'queued', bytes: prev.bytes || 0, total: file.size, speed: 0, file, uploadId, canRetry: true });
    let offset = 0;
    if (uploadId && existingId) {
      try {
        const st = await api('/api/upload/status', { params: { uploadId } });
        if (st.ok && st.data && st.data.total === file.size) offset = st.data.offset || 0;
      } catch (e) { /* start over */ }
    }
    const xhr = new XMLHttpRequest();
    let lastT = Date.now(), lastB = 0;
    const params = { filename: file.name };
    if (uploadId) Object.assign(params, { uploadId, offset, total: file.size });
    xhr.open('POST', apiUrl('/api/upload', params));
    xhr.upload.onprogress = (e) => {
      const now = Date.now();
      const t = transfers.get(id) || {};
      let speed = t.speed || 0;
      if (now - lastT >= 300) { speed = (e.loaded - lastB) * 1000 / (now - lastT); lastT = now; lastB = e.loaded; }
      onTransfer({ id, type: 'upload', name: file.name, state: 'active', bytes: offset + e.loaded, total: file.size, speed });
    };
    xhr.onload = () => {
      if (xhr.status === 200) onTransfer({ id, type: 'upload', name: file.name, state: 'done', bytes: file.size, total: file.size, speed: 0 });
      else {
        let msg = 'PC answered HTTP ' + xhr.status;
        try { msg = JSON.parse(xhr.responseText).error || msg; } catch (e) { }
        onTransfer({ id, type: 'upload', name: file.name, state: 'failed', total: file.size, speed: 0, error: msg, resumable: !!uploadId });
      }
    };
    xhr.onerror = () => onTransfer({ id, type: 'upload', name: file.name, state: 'failed', total: file.size, speed: 0, error: 'PC not reachable', resumable: !!uploadId });
    xhr.onabort = () => onTransfer({ id, type: 'upload', name: file.name, state: 'cancelled', bytes: 0, total: file.size, speed: 0 });
    transfers.get(id).xhr = xhr;
    xhr.send(offset > 0 ? file.slice(offset) : file);
  }
  function cancelTransfer(id) {
    const t = transfers.get(id);
    if (!t) return;
    if (t.state === 'failed') {
      transfers.delete(id);
      if (isApp && id > 0) { try { native.dismissTransfer(id); } catch (e) { } }
      renderTransfers();
      return;
    }
    if (t.xhr) t.xhr.abort();
    else if (isApp) { try { native.cancelTransfer(id); } catch (e) { } }
  }
  function retryTransfer(id) {
    const t = transfers.get(id);
    if (!t) return;
    if (t.file) { uploadInBrowser(t.file, id); return; }
    if (isApp && typeof native.retryTransfer === 'function') { try { native.retryTransfer(id); } catch (e) { toast('Could not retry'); } }
    else toast('Send the file again');
  }
  /** Transfers the app kept going in the background (or that stopped while it was closed). */
  function relistTransfers() {
    if (!isApp || typeof native.listTransfers !== 'function') return;
    try { JSON.parse(native.listTransfers()).forEach(t => { if (!transfers.has(t.id) || transfers.get(t.id).state !== t.state) onTransfer(t); }); } catch (e) { }
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

  /** "cloudssd:Photos/a.jpg" (a cloud item by drive id) rather than "Z:\Photos\a.jpg" (a path on the PC). */
  function isCloudId(p) { return /^[a-z0-9_-]{2,}:/i.test(String(p || '')) && !/^[a-z]:\\/i.test(String(p || '')); }

  // ---- Google Drive on the phone (no PC) ----
  const drive = { available: false, direct: true, accounts: [], roots: [] };
  function refreshDrive() {
    if (!isApp || typeof native.driveStatus !== 'function') return;
    try { Object.assign(drive, JSON.parse(native.driveStatus())); } catch (e) { }
  }
  /** Where cloud files come from right now: the PC, Google Drive directly, or nothing. */
  function cloudSource() {
    if (link.state === 'online') return 'pc';
    if (isApp && drive.direct && (drive.accounts || []).some(a => !a.needsReauth)) return 'drive';
    return 'none';
  }
  const cloudReqs = new Map();
  let cloudSeq = 0;
  /** A file operation on Google Drive through the app: resolves to {ok, status, data, error}. */
  function cloudOp(op, args) {
    return new Promise(resolve => {
      const id = ++cloudSeq;
      cloudReqs.set(id, resolve);
      try { native.cloudOp(id, op, JSON.stringify(args || {})); } catch (e) { cloudReqs.delete(id); resolve({ ok: false, error: 'Update the Cloud-SSD app' }); }
    });
  }

  /**
   * A link to one file that can be given to a player or another app. On a current PC it is a signed, short-lived
   * link without this phone's key; on an older PC the old kind; for Google Drive the app serves it itself.
   */
  async function mediaUrl(f, kind) {
    if (isCloudId(f.path) && cloudSource() === 'drive') return 'http://cloudssd.cloud/read?id=' + encodeURIComponent(f.path);
    if (pcHas('media-url')) {
      const params = isCloudId(f.path) ? { id: f.path, kind } : { path: f.path, kind };
      const r = await api('/api/media-url', { method: 'POST', params });
      if (r.ok && r.data && r.data.url) return link.server + r.data.url;
      throw new Error(apiError(r, 'The PC did not give a link'));
    }
    return isCloudId(f.path) ? apiUrl('/api/fs/read', { path: f.path }) : apiUrl(kind === 'download' ? '/api/download' : '/api/stream', { path: f.path });
  }

  /** f = {name, path, size, modified, src: 'cloud' | 'phone'}; list = the files around it (for swiping through photos) */
  function openFileSheet(f, list) {
    sheetFile = f;
    const k = kindOf(f.name);
    const cloud = f.src === 'cloud';
    const viaDrive = cloud && isCloudId(f.path) && cloudSource() === 'drive';
    const pdf = extOf(f.name) === 'pdf';
    const canView = k === 'images' || k === 'audio' || (k === 'videos' && PLAYABLE_VIDEO.includes(extOf(f.name))) || (cloud && k === 'text' && !viaDrive) || pdf;
    sheetActions = [];
    if (canView) sheetActions.push({
      icon: SVG.play, title: k === 'images' ? 'View' : k === 'text' || pdf ? 'Read' : 'Play',
      sub: !cloud ? 'On this phone' : viaDrive ? 'Streams from Google Drive' : 'Streams from the PC', fn: () => openViewer(f, list)
    });
    if (isApp) sheetActions.push({ icon: SVG.share, title: 'Open with another app', sub: cloud ? 'Video player, gallery, PDF reader…' : 'Choose an app', fn: () => openExternally(f) });
    if (cloud) {
      sheetActions.push({ icon: SVG.download, title: 'Save to phone', sub: 'Into the Downloads folder' + (f.size > 0 ? ' · ' + fmtBytes(f.size) : ''), fn: () => downloadToPhone(f) });
      if (!viaDrive && !isCloudId(f.path)) sheetActions.push({ icon: SVG.pc, title: 'Open on the PC screen', sub: 'Opens in its Windows app', fn: () => openOnPc(f) });
    } else {
      sheetActions.push({ icon: SVG.upload, title: 'Send to PC', sub: fmtBytes(f.size), fn: () => sendPhoneFiles([f]) });
    }
    // Rename / move / delete on a cloud drive (fs.js).
    if (cloud && window.CloudSsdFs) window.CloudSsdFs.fileActions(f).forEach(a => sheetActions.push(a));
    sheetActions.push({ icon: SVG.star, title: isStarred(f) ? 'Remove star' : 'Add star', sub: 'Starred files are listed on the Phone tab', fn: () => { toggleStar(f); toast(isStarred(f) ? 'Starred' : 'Star removed'); } });
    sheetActions.push({ icon: SVG.info, title: 'Details', sub: 'Size, dates, where it is', fn: () => openDetails(f) });

    setText('sheet-file-name', f.name);
    setText('sheet-file-meta', [f.size > 0 ? fmtBytes(f.size) : '', fmtDate(f.modified), !cloud ? 'on this phone' : viaDrive ? 'on Google Drive' : 'on the PC'].filter(Boolean).join(' · '));
    $('sheet-file-icon').innerHTML = iconFor(f.name);
    $('sheet-actions-list').innerHTML = sheetActions.map((a, i) => `
      <div class="m3-sheet-row" data-act="sheet-action" data-i="${i}">
        ${a.icon}
        <div class="m3-sheet-row-text"><div>${esc(a.title)}</div><div class="m3-sheet-row-sub">${esc(a.sub)}</div></div>
      </div>`).join('');
    openSheet('file-actions-sheet');
  }
  async function openExternally(f) {
    try {
      if (f.src !== 'cloud') { native.openLocalFile(f.path); return; }
      if (isCloudId(f.path) && cloudSource() === 'drive') { native.cloudOpenWith(f.path, f.name); return; }
      if (!pcHas('media-url') && isCloudId(f.path) && typeof native.cloudOpenWith === 'function') { native.cloudOpenWith(f.path, f.name); return; }
      if (!pcHas('media-url')) { toast('Update Cloud-SSD on the PC to open files in other apps'); return; }
      native.openWith(await mediaUrl(f, 'stream'), mimeOf(f.name), f.name);
    } catch (e) { toast(e && e.message ? e.message : 'Cannot open this file'); }
  }
  async function confirmBigOnMobileData(f) {
    const n = appInfo.network || {};
    if (!isApp || !n.metered || !(f.size > 100 * 1024 * 1024)) return true;
    return confirmDialog('Use mobile data?', `${f.name} is ${fmtBytes(f.size)} and this phone is on mobile data.`, 'Download');
  }
  async function downloadToPhone(f) {
    if (!await confirmBigOnMobileData(f)) return;
    if (isApp) {
      try {
        if (isCloudId(f.path) && cloudSource() === 'drive') native.downloadFile('cloud:' + f.path, f.name);
        else native.downloadFile(isCloudId(f.path) ? apiUrl('/api/fs/read', { path: f.path }) : apiUrl('/api/download', { path: f.path }), f.name);
        toast('Downloading ' + f.name);
      } catch (e) { toast('Download could not start'); }
    } else {
      try {
        const a = document.createElement('a');
        a.href = await mediaUrl(f, 'download'); a.download = f.name;
        document.body.appendChild(a); a.click(); a.remove();
        toast('Download started in the browser');
      } catch (e) { toast(e.message || 'Download could not start'); }
    }
  }

  // ---- Details ----
  async function openDetails(f) {
    const cloud = f.src === 'cloud';
    const where = !cloud ? prettyPhonePath(f.path.slice(0, f.path.lastIndexOf('/')))
      : isCloudId(f.path) ? f.path.slice(0, Math.max(f.path.lastIndexOf('/'), f.path.indexOf(':') + 1))
        : f.path.slice(0, f.path.lastIndexOf('\\'));
    const rows = [
      ['Type', f.isFolder ? 'Folder' : (extOf(f.name).toUpperCase() || 'File') + ' · ' + mimeOf(f.name)],
      ['Size', f.isFolder ? '—' : f.size > 0 ? fmtBytes(f.size) + ' (' + f.size.toLocaleString() + ' bytes)' : '—'],
      ['Modified', f.modified ? new Date(typeof f.modified === 'number' ? f.modified : Date.parse(f.modified)).toLocaleString() : '—'],
      ['Where', (!cloud ? 'This phone · ' : isCloudId(f.path) && cloudSource() === 'drive' ? 'Google Drive · ' : (link.pcName || 'PC') + ' · ') + where]
    ];
    const render = (extra) => {
      $('details-icon').innerHTML = f.isFolder ? SVG.folder : iconFor(f.name);
      setText('details-name', f.name);
      setText('details-kind', f.isFolder ? 'Folder' : kindOf(f.name) === 'other' ? 'File' : kindOf(f.name).replace(/s$/, ''));
      $('details-rows').innerHTML = rows.concat(extra || []).map(([k, v]) => `<div class="details-row"><span>${esc(k)}</span><strong>${esc(v)}</strong></div>`).join('') +
        `<div class="details-actions">
          <button class="m3-tonal-btn" data-act="details-copy">Copy location</button>
          ${f.isFolder && cloud && (pcHas('fs-details') || cloudSource() === 'drive') ? '<button class="m3-tonal-btn" data-act="details-size">Calculate size</button>' : ''}
        </div>`;
    };
    detailsFile = f;
    render();
    openSheet('details-sheet');
    if (cloud && pcHas('fs-details') && cloudSource() === 'pc' && !f.isFolder) {
      const id = isCloudId(f.path) ? f.path : (window.CloudSsdFs && window.CloudSsdFs.idOf(f.path));
      if (!id) return;
      try {
        const r = await api('/api/fs/stat', { params: { path: id, details: 1 } });
        if (!r.ok || !r.data || detailsFile !== f) return;
        const e = r.data.entry || {}, m = r.data.media || {};
        const extra = [];
        if (e.created) extra.push(['Created', new Date(e.created).toLocaleString()]);
        if (m.width) extra.push(['Picture', m.width + ' × ' + m.height + ' px']);
        if (m.dateTaken) extra.push(['Taken', m.dateTaken]);
        if (m.camera) extra.push(['Camera', m.camera]);
        if (r.data.fullPath) rows[3] = ['Where', (link.pcName || 'PC') + ' · ' + r.data.fullPath];
        render(extra);
      } catch (e) { /* the basic rows stay */ }
    }
  }
  let detailsFile = null;
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
  /** Where the viewer inside this page reads a file from (the key never leaves the app here). */
  function viewUrl(f) {
    if (f.src !== 'cloud') return fileUrl(f.path);
    if (isCloudId(f.path)) return cloudSource() === 'drive' ? 'http://cloudssd.cloud/read?id=' + encodeURIComponent(f.path) : apiUrl('/api/fs/read', { path: f.path });
    return streamUrl(f.path);
  }
  function viewerWhere(f) {
    return f.src !== 'cloud' ? ' · on this phone' : isCloudId(f.path) && cloudSource() === 'drive' ? ' · from Google Drive' : ' · streaming from the PC';
  }

  // Photos: swipe to the next one, pinch or double-tap to zoom.
  const gallery = { list: [], i: 0, scale: 1, x: 0, y: 0 };
  function showGalleryImage() {
    const f = gallery.list[gallery.i];
    if (!f) return;
    viewerFile = f;
    setText('viewer-filename', f.name);
    setText('viewer-meta', (f.size > 0 ? fmtBytes(f.size) : '') + viewerWhere(f));
    setText('gallery-count', gallery.list.length > 1 ? `${gallery.i + 1} / ${gallery.list.length}` : '');
    document.querySelectorAll('.gallery-bar .viewer-btn-icon').forEach(b => { b.style.visibility = gallery.list.length > 1 ? '' : 'hidden'; });
    const img = $('viewer-image-elem');
    zoomTo(1, 0, 0);
    img.onerror = () => {
      if (viewerFile !== f) return;
      // In a gallery the others can still be swiped to; a single picture gets the "open with" choices.
      if (gallery.list.length > 1) setText('gallery-count', `${gallery.i + 1} / ${gallery.list.length} · could not be shown`);
      else viewerFallback(f, 'This picture could not be shown.');
    };
    img.src = viewUrl(f);
    // The neighbours load quietly so swiping feels instant.
    [gallery.i + 1, gallery.i - 1].forEach(j => { const n = gallery.list[j]; if (n) new Image().src = viewUrl(n); });
  }
  function galleryStep(d) {
    if (gallery.list.length < 2) return;
    const j = gallery.i + d;
    if (j < 0 || j >= gallery.list.length) { toast(d > 0 ? 'Last picture' : 'First picture'); return; }
    gallery.i = j;
    show('viewer-section-generic', false);
    show('viewer-section-image', true);
    showGalleryImage();
  }
  function zoomTo(scale, x, y) {
    gallery.scale = Math.min(5, Math.max(1, scale));
    if (gallery.scale === 1) { x = 0; y = 0; }
    gallery.x = x; gallery.y = y;
    const img = $('viewer-image-elem');
    if (img) img.style.transform = gallery.scale === 1 ? '' : `translate(${x}px, ${y}px) scale(${gallery.scale})`;
  }
  function wireGallery() {
    const stage = $('gallery-stage');
    if (!stage) return;
    let start = null, pinch = null, lastTap = 0;
    const dist = (t) => Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY);
    stage.addEventListener('touchstart', (e) => {
      if (e.touches.length === 2) { pinch = { d: dist(e.touches), s: gallery.scale }; start = null; return; }
      const t = e.touches[0];
      start = { x: t.clientX, y: t.clientY, ox: gallery.x, oy: gallery.y, t: Date.now() };
    }, { passive: true });
    stage.addEventListener('touchmove', (e) => {
      if (pinch && e.touches.length === 2) { e.preventDefault(); zoomTo(pinch.s * dist(e.touches) / pinch.d, gallery.x, gallery.y); return; }
      if (start && gallery.scale > 1) { e.preventDefault(); const t = e.touches[0]; zoomTo(gallery.scale, start.ox + t.clientX - start.x, start.oy + t.clientY - start.y); }
    }, { passive: false });
    stage.addEventListener('touchend', (e) => {
      if (pinch) { if (e.touches.length === 0) pinch = null; return; }
      if (!start) return;
      const t = e.changedTouches[0];
      const dx = t.clientX - start.x, dy = t.clientY - start.y;
      const quick = Date.now() - start.t < 600;
      if (gallery.scale === 1 && quick && Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5) galleryStep(dx < 0 ? 1 : -1);
      else if (Math.abs(dx) < 10 && Math.abs(dy) < 10) {
        const now = Date.now();
        if (now - lastTap < 300) { zoomTo(gallery.scale > 1 ? 1 : 2.5, 0, 0); lastTap = 0; } else lastTap = now;
      }
      start = null;
    });
  }

  // PDFs: the app draws each page with Android's own PDF renderer; a browser uses its built-in reader.
  const pdf = { req: 0, key: '', file: null };
  function openPdfViewer(f) {
    const status = $('pdf-status'), pages = $('pdf-pages');
    show('viewer-section-pdf', true);
    pages.innerHTML = '';
    status.style.display = '';
    status.textContent = 'Opening…';
    const req = ++pdf.req;
    pdf.file = f;
    let source;
    if (f.src !== 'cloud') source = f.path;
    else if (isCloudId(f.path) && cloudSource() === 'drive') source = 'cloud:' + f.path;
    // Read inside the app, so the PC lists it as streamed, not as a file saved to the phone.
    else source = 'pc:' + (isCloudId(f.path) ? apiUrl('/api/fs/read', { path: f.path }) : apiUrl('/api/stream', { path: f.path }));
    try { native.openPdf(req, source, f.name, f.size || -1); } catch (e) { viewerFallback(f, 'Update the Cloud-SSD app to read PDFs here.'); }
  }
  function onPdf(ev) {
    if (ev.reqId !== pdf.req || !pdf.file) return;
    const status = $('pdf-status');
    if (ev.state === 'loading') {
      status.textContent = 'Loading… ' + fmtBytes(ev.bytes) + (ev.total > 0 ? ' of ' + fmtBytes(ev.total) : '');
    } else if (ev.state === 'error') {
      viewerFallback(pdf.file, ev.error);
    } else if (ev.state === 'ready') {
      pdf.key = ev.key;
      const n = (ev.pages || []).length;
      status.textContent = n + (n === 1 ? ' page' : ' pages');
      const w = Math.min(2400, Math.round(Math.min(window.innerWidth, 900) * (window.devicePixelRatio || 2)));
      $('pdf-pages').innerHTML = (ev.pages || []).map((p, i) =>
        `<img class="pdf-page" loading="lazy" decoding="async" alt="Page ${i + 1}" style="aspect-ratio:${p.w} / ${p.h}"
          src="http://cloudssd.pdf/?k=${encodeURIComponent(ev.key)}&p=${i}&w=${w}">`).join('');
    }
  }

  /** list: the files around this one (a folder), so photos can be swiped through. */
  function openViewer(f, list) {
    viewerFile = f;
    const k = kindOf(f.name);
    const url = viewUrl(f);
    setText('viewer-filename', f.name);
    setText('viewer-meta', (f.size > 0 ? fmtBytes(f.size) : '') + viewerWhere(f));
    ['video', 'image', 'audio', 'text', 'generic', 'pdf'].forEach(s => show('viewer-section-' + s, false));

    if (k === 'pdf') {
      if (!isApp) {
        // A phone browser has its own PDF reader: give it a short-lived link.
        mediaUrl(f, 'stream').then(u => window.open(u, '_blank')).catch(e => toast(e.message || 'Cannot open this PDF'));
        return;
      }
      openPdfViewer(f);
    } else if (k === 'videos') {
      const v = $('native-video-player');
      show('viewer-section-video', true);
      v.onerror = () => { if (viewerFile === f) viewerFallback(f, 'This phone cannot play this video format here.'); };
      v.src = url;
      v.play().catch(() => { });
    } else if (k === 'images') {
      show('viewer-section-image', true);
      gallery.list = (list || [f]).filter(x => kindOf(x.name) === 'images');
      gallery.i = Math.max(0, gallery.list.findIndex(x => x.path === f.path));
      if (!gallery.list.length) gallery.list = [f];
      showGalleryImage();
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
    ['video', 'image', 'audio', 'text', 'pdf'].forEach(s => show('viewer-section-' + s, false));
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
    if (img) { img.onerror = null; img.removeAttribute('src'); img.style.transform = ''; }
    gallery.list = []; gallery.scale = 1;
    pdf.req++;
    pdf.file = null;
    if (pdf.key) { try { native.closePdf(pdf.key); } catch (e) { } pdf.key = ''; }
    const pages = $('pdf-pages');
    if (pages) pages.innerHTML = '';
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
    document.body.classList.toggle('selecting', phone.selecting || ssd.selecting);
  }

  // ------------------------------------------------------------------ Cloud SSD tab (drives on the PC)
  const ssd = {
    path: '', root: '', cache: new Map(Object.entries(ls.json('cloudssd_dircache', {}))), view: { folders: [], files: [] },
    seq: 0, grid: ls.get('cloudssd_layout') === 'grid', sort: ls.get('cloudssd_sort') || 'name-asc', localDrives: [], showAll: false,
    selecting: false, selected: new Set(), suppressClick: 0
  };
  const SORTS = [
    { key: 'name-asc', label: 'Name (A to Z)' }, { key: 'name-desc', label: 'Name (Z to A)' },
    { key: 'date-desc', label: 'Newest first' }, { key: 'date-asc', label: 'Oldest first' },
    { key: 'size-desc', label: 'Largest first' }, { key: 'size-asc', label: 'Smallest first' }
  ];

  /** Path separator: "\" for PC paths, "/" inside a drive id ("cloudssd:Photos/2024"). */
  function sepOf(path) { return isCloudId(path) ? '/' : '\\'; }
  function whenOf(v) { return typeof v === 'number' ? v : (Date.parse(v) || 0); }

  function ssdPlaces() {
    // PC off but Google signed in on this phone: the same drives, straight from Google.
    if (cloudSource() === 'drive') return (drive.roots || []).map(r => ({ root: r.id + ':', label: r.title, cloud: true, mounted: true, id: r.id, drive: true }));
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
  async function loadDriveRoots() {
    const r = await cloudOp('roots', {});
    if (r.ok && Array.isArray(r.data)) drive.roots = r.data;
    return drive.roots || [];
  }
  function openSsdTab() {
    const source = cloudSource();
    // Coming from the PC to Google Drive (or back): the old folder is named the other way.
    if (ssd.path && isCloudId(ssd.path) !== (source === 'drive')) { ssd.path = ''; ssd.cache.clear(); }
    show('ssd-drive-note', source === 'drive');
    if (source === 'drive') {
      if (!(drive.roots || []).length) {
        $('ssd-items-container').innerHTML = skeleton(6);
        loadDriveRoots().then(roots => { if (roots.length && cloudSource() === 'drive') openSsdTab(); else renderSsdOffline(); });
        return;
      }
      loadSsd(ssd.path || ssdPlaces()[0].root);
      return;
    }
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
    const googleHint = isApp && drive.available && !(drive.accounts || []).length
      ? ' Or sign in to Google in Settings to open your Drive without the PC.' : '';
    $('ssd-items-container').innerHTML = link.state === 'unpaired'
      ? emptyBlock(SVG.pc, 'Not paired with a PC', 'Pair once with the code shown in Cloud-SSD on the PC.' + googleHint, 'Pair now', 'open-pair')
      : emptyBlock(SVG.pc, 'PC is not connected', (link.away.length ? 'Cloud-SSD must be open on the PC. Away from home, turn on Tailscale on this phone.' : 'Cloud-SSD must be open on the PC, on the same Wi-Fi.') + googleHint, 'Try again', 'toggle-link');
  }
  function renderCrumbs() {
    const pl = placeOf(ssd.path);
    const box = $('ssd-breadcrumbs-path');
    if (!ssd.path) { box.innerHTML = ''; return; }
    const sep = sepOf(ssd.path);
    const root = pl ? pl.root : isCloudId(ssd.path) ? ssd.path.slice(0, ssd.path.indexOf(':') + 1) : ssd.path.slice(0, 3);
    const rest = ssd.path.slice(root.length).split(sep).filter(Boolean);
    let acc = root.replace(/\\$/, '');
    let html = `<span class="crumb" data-act="ssd-crumb" data-path="${esc(root)}">${esc(pl ? pl.label : root)}</span>`;
    for (const seg of rest) {
      acc += (acc.endsWith(':') && sep === '/' ? '' : sep) + seg;
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
    if (ssd.path !== path) endSsdSelect();
    ssd.path = path;
    ssd.showAll = false;
    renderCrumbs();
    if (isCloudId(path) && cloudSource() === 'drive') { loadDriveFolder(path); return; }
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
  /** A folder on Google Drive, read by the app (PC off). */
  async function loadDriveFolder(path) {
    renderSsdDrives();
    const box = $('ssd-items-container');
    const cached = ssd.cache.get(path);
    if (cached) renderSsd(cached);
    else { box.className = 'ssd-items-container list-view'; box.innerHTML = skeleton(8); }
    const seq = ++ssd.seq;
    $('ssd-refresh-btn').classList.add('spinning');
    const r = await cloudOp('list', { path });
    if (seq !== ssd.seq) return;
    $('ssd-refresh-btn').classList.remove('spinning');
    if (r.ok && r.data) {
      const entries = r.data.entries || [];
      const data = {
        folders: entries.filter(e => e.isDirectory).map(e => ({ name: e.name, path: e.id, modified: e.modified })),
        files: entries.filter(e => !e.isDirectory).map(e => ({ name: e.name, path: e.id, size: e.size, modified: e.modified, exported: e.exported, readOnly: !e.canEdit }))
      };
      ssd.cache.set(path, data);
      renderSsd(data);
    } else if (!cached) {
      box.innerHTML = emptyBlock(SVG.cloud, r.error || 'Google Drive did not answer', 'Check the internet connection, or sign in again in Settings.', 'Try again', 'ssd-refresh');
    } else toast(r.error || 'Showing the saved list');
  }
  function sortFiles(files) {
    const [by, dir] = ssd.sort.split('-');
    const m = dir === 'desc' ? -1 : 1;
    const cmp = {
      name: (a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }),
      date: (a, b) => whenOf(a.modified) - whenOf(b.modified),
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
    // On a cloud drive a folder can be renamed, moved and deleted (fs.js), so it gets a "more" button.
    const manage = !!window.CloudSsdFs && window.CloudSsdFs.canManage(ssd.path);
    let n = 0, html = '';
    for (let i = 0; i < folders.length && (ssd.showAll || n < LIMIT); i++, n++) {
      const sel = ssd.selected.has('d' + i);
      html += `<div class="explorer-item folder-item ${sel ? 'selected' : ''}" data-act="ssd-folder" data-i="${i}" data-key="d${i}">
        <div class="item-icon folder-icon">${SVG.folder}<div class="row-check">${SVG.check}</div></div>
        <div class="item-details"><div class="item-name">${esc(folders[i].name)}</div><div class="item-meta">Folder${folders[i].modified ? ' · ' + fmtDate(folders[i].modified) : ''}</div></div>
        ${manage ? `<button class="item-more-btn" data-act="fs-folder-more" data-i="${i}">${SVG.more}</button>` : `<div class="item-action-icon">${SVG.chevron}</div>`}
      </div>`;
    }
    for (let i = 0; i < files.length && (ssd.showAll || n < LIMIT); i++, n++) {
      const f = files[i];
      const thumb = ssdThumb(f);
      const sel = ssd.selected.has('f' + i);
      html += `<div class="explorer-item file-item ${thumb ? 'has-thumb' : ''} ${sel ? 'selected' : ''}" data-act="ssd-file" data-i="${i}" data-key="f${i}">
        <div class="item-icon kind-${kindOf(f.name)}">${iconFor(f.name)}${thumb ? `<img loading="lazy" decoding="async" src="${esc(thumb)}" onerror="this.remove()" alt="">` : ''}${kindOf(f.name) === 'videos' && thumb ? `<div class="thumb-tile-play">${SVG.play}</div>` : ''}<div class="row-check">${SVG.check}</div></div>
        <div class="item-details"><div class="item-name">${esc(f.name)}</div><div class="item-meta">${f.size > 0 ? fmtBytes(f.size) : f.exported ? 'Google file' : '—'}${f.modified ? ' · ' + fmtDate(f.modified) : ''}</div></div>
        <button class="item-more-btn" data-act="ssd-more" data-i="${i}">${SVG.more}</button>
      </div>`;
    }
    if (n < total) html += `<button class="m3-tonal-btn list-more" data-act="ssd-show-all">Show all ${total} items</button>`;
    else html += `<div class="list-note">${folders.length} folders · ${files.length} files${cloudSource() === 'drive' ? ' · from Google Drive' : ''}</div>`;
    box.innerHTML = html;
    box.classList.toggle('selecting', ssd.selecting);
  }
  /** A small picture for photos and videos: made by the PC (and kept there), or by Google Drive. */
  function ssdThumb(f) {
    const k = kindOf(f.name);
    if (k !== 'images' && k !== 'videos') return '';
    if (isCloudId(f.path)) return cloudSource() === 'drive' ? 'http://cloudssd.cloud/thumb?id=' + encodeURIComponent(f.path) : '';
    if (!pcHas('fs-thumb') || !window.CloudSsdFs) return '';
    const id = window.CloudSsdFs.idOf(f.path);
    return id ? apiUrl('/api/fs/thumb', { path: id, w: ssd.grid ? 320 : 160 }) : '';
  }
  function ssdUp() {
    if (!ssd.path || ssd.path.length <= ssd.root.length) return false;
    const sep = sepOf(ssd.path);
    const trimmed = sep === '\\' ? ssd.path.replace(/\\+$/, '') : ssd.path.replace(/\/+$/, '');
    const cut = trimmed.lastIndexOf(sep);
    let parent = cut < 0 ? ssd.root : trimmed.slice(0, cut);
    if (parent.length < ssd.root.length) parent = ssd.root;
    if (/^[A-Za-z]:$/.test(parent)) parent += '\\';
    loadSsd(parent);
    return true;
  }

  // ---- several items at once on a cloud drive ----
  function ssdSelectedItems() {
    return Array.from(ssd.selected).map(k => {
      const i = +k.slice(1);
      if (k[0] === 'd') { const fo = ssd.view.folders[i]; return fo && { name: fo.name, path: fo.path, isFolder: true }; }
      const f = ssd.view.files[i];
      return f && Object.assign({}, f, { isFolder: false });
    }).filter(Boolean);
  }
  function renderSsdSelectBar() {
    show('ssd-select-bar', ssd.selecting);
    setText('ssd-select-count', ssd.selected.size + ' selected');
    document.body.classList.toggle('selecting', ssd.selecting || phone.selecting);
    const box = $('ssd-items-container');
    if (box) box.classList.toggle('selecting', ssd.selecting);
  }
  function startSsdSelect(key) {
    if (!ssd.selecting) { ssd.selecting = true; ssd.selected.clear(); }
    if (key) toggleSsdItem(key);
    renderSsdSelectBar();
  }
  function endSsdSelect() {
    if (!ssd.selecting && !ssd.selected.size) return;
    ssd.selecting = false;
    ssd.selected.clear();
    document.querySelectorAll('#ssd-items-container .selected').forEach(el => el.classList.remove('selected'));
    renderSsdSelectBar();
  }
  function toggleSsdItem(key) {
    if (ssd.selected.has(key)) ssd.selected.delete(key); else ssd.selected.add(key);
    const el = document.querySelector(`#ssd-items-container [data-key="${key}"]`);
    if (el) el.classList.toggle('selected', ssd.selected.has(key));
    renderSsdSelectBar();
  }
  async function ssdBulk(what) {
    const items = ssdSelectedItems();
    if (!items.length) { toast('Tap files to select them'); return; }
    if (what === 'download') {
      const files = items.filter(i => !i.isFolder && !i.exported);
      if (!files.length) {
        toast(items.some(i => i.isFolder) ? 'Folders cannot be saved at once; open them and select the files' : 'Google Docs files cannot be saved here; open them with another app');
        return;
      }
      const big = files.reduce((s, f) => s + (f.size || 0), 0);
      if (!await confirmBigOnMobileData({ name: files.length + ' files', size: big })) return;
      if (isApp) {
        const list = files.map(f => ({
          url: isCloudId(f.path) && cloudSource() === 'drive' ? 'cloud:' + f.path
            : isCloudId(f.path) ? apiUrl('/api/fs/read', { path: f.path }) : apiUrl('/api/download', { path: f.path }),
          name: f.name, size: f.size || -1
        }));
        try { native.downloadFiles(JSON.stringify(list)); toast('Saving ' + files.length + (files.length === 1 ? ' file' : ' files') + ' to Downloads'); }
        catch (e) { files.forEach(f => downloadToPhone(f)); }
      } else {
        for (const f of files) { await downloadToPhone(f); await new Promise(r => setTimeout(r, 700)); }
      }
      endSsdSelect();
      return;
    }
    if (!window.CloudSsdFs || !window.CloudSsdFs.canManage(ssd.path)) { toast('Files here cannot be changed from the phone'); return; }
    if (what === 'move') { window.CloudSsdFs.startMove(items); endSsdSelect(); }
    else if (what === 'delete') { if (await window.CloudSsdFs.bulkDelete(items)) endSsdSelect(); }
  }
  /** Long press on a cloud item starts selecting. */
  function wireSsdLongPress() {
    const box = $('ssd-items-container');
    if (!box) return;
    let timer = null;
    box.addEventListener('touchstart', (e) => {
      const el = e.target.closest('[data-key]');
      if (!el || e.target.closest('.item-more-btn')) return;
      timer = setTimeout(() => {
        timer = null;
        ssd.suppressClick = Date.now();
        if (navigator.vibrate) { try { navigator.vibrate(20); } catch (x) { } }
        startSsdSelect(el.dataset.key);
      }, 520);
    }, { passive: true });
    const cancel = () => { if (timer) { clearTimeout(timer); timer = null; } };
    box.addEventListener('touchmove', cancel, { passive: true });
    box.addEventListener('touchend', cancel);
    box.addEventListener('contextmenu', (e) => { if (e.target.closest('[data-key]')) e.preventDefault(); });
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
      { label: 'Free', sub: 'Not used by anything', bytes: d.freeBytes, color: 'rgb(var(--ink) / 0.28)' }
    ] : [
      { label: 'In use', sub: 'Everything except what Android can free', bytes: d.usedBytes, color: '#60A5FA' },
      { label: 'Available', sub: 'Free plus what Android can free', bytes: d.availableBytes, color: 'rgb(var(--ink) / 0.28)' }
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
        saves = r.data.slice(0, 50);
        box.innerHTML = saves.map((s, i) => `
          <div class="transfer-card-item ${s.gameId ? 'tappable' : ''}" ${s.gameId ? `data-act="save-open" data-i="${i}"` : ''}><div class="tc-left">
            <div class="tc-icon-wrap stream">${SVG.archive}</div>
            <div class="tc-info"><div class="tc-name">${esc(s.name)}</div><div class="tc-meta"><span>${esc(s.sizeFormatted || fmtBytes(s.size))}</span><span>${esc(fmtDate(s.modified))}</span>${s.kind === 'safety' ? '<span class="tc-dir-badge stream">Before a restore</span>' : ''}</div></div>
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
  let saves = [];
  function openSave(s) {
    const list = [{
      icon: SVG.download, title: 'Save to phone', sub: 'A copy of this backup (.zip) in Downloads · ' + (s.sizeFormatted || fmtBytes(s.size)),
      fn: () => {
        const name = `${s.gameName || 'game'} saves ${String(s.file).replace(/^(save|safety_pre_restore)_/i, '')}`.replace(/[\\/:*?"<>|]/g, '_');
        if (isApp) { try { native.downloadFile(apiUrl('/api/saves/download', { gameId: s.gameId, file: s.file }), name); toast('Downloading ' + name); } catch (e) { toast('Download could not start'); } }
        else {
          const a = document.createElement('a');
          a.href = apiUrl('/api/saves/download', { gameId: s.gameId, file: s.file }); a.download = name;
          document.body.appendChild(a); a.click(); a.remove();
        }
      }
    }];
    if (pcHas('save-restore')) list.push({
      icon: SVG.pc, title: 'Restore on the PC', sub: 'Puts these saves back for ' + (s.gameName || 'the game') + '. The current saves are kept as a copy first.',
      fn: async () => {
        if (!await confirmDialog('Restore these saves?', `${s.gameName || 'The game'} goes back to the saves from ${new Date(s.modified).toLocaleString()}. The game must be closed on the PC. Today's saves are kept as a safety copy.`, 'Restore')) return;
        toast('Restoring on the PC…');
        try {
          const r = await api('/api/saves/restore', { method: 'POST', params: { gameId: s.gameId, file: s.file }, timeout: 300000 });
          toast(r.ok && r.data && r.data.success ? (r.data.message || 'Saves restored') : apiError(r, 'Could not restore'));
          loadSaves();
        } catch (e) { toast('PC did not answer'); }
      }
    });
    choiceSheet(s.name, list);
  }

  // ------------------------------------------------------------------ look (light / dark)
  function themeMode() { const m = ls.get('cloudssd_theme'); return m === 'light' || m === 'dark' ? m : 'system'; }
  function applyTheme() {
    const mode = themeMode();
    let light;
    if (mode !== 'system') light = mode === 'light';
    else if (isApp && typeof appInfo.night === 'boolean') light = !appInfo.night;
    else light = !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches);
    document.documentElement.dataset.theme = light ? 'light' : 'dark';
    document.querySelectorAll('meta[name="theme-color"]').forEach(m => { m.content = light ? '#F8F9FC' : '#121316'; });
  }

  // ------------------------------------------------------------------ settings sheet
  let settingsData = null;
  const PERMS = [
    ['driveBrowsing', 'Browse the cloud drives'], ['streaming', 'Play and download files'], ['uploads', 'Send files to the PC'],
    ['fileManagement', 'Rename, move and delete on the cloud'], ['gamingHud', 'Gaming numbers and media remote'],
    ['pcControl', 'Control the PC (drives, Space saver, game saves)'], ['pcDisks', 'See the PC\'s own disks'], ['space', 'See the PC\'s storage and RAM'], ['tailscale', 'Reach the PC away from home (Tailscale)']
  ];
  async function openSettings() {
    closeSheet('connection-modal');
    refreshDrive();
    renderSettings();
    openSheet('settings-modal');
    if (link.state === 'online' && pcHas('settings')) {
      try {
        const r = await api('/api/settings', { timeout: 8000 });
        if (r.ok && r.data) { settingsData = r.data; if (isOpen('settings-modal')) renderSettings(); }
      } catch (e) { /* the parts known from the status stay */ }
    }
  }
  function settingsSection(title, body) { return `<div class="settings-section"><div class="sheet-subtitle">${esc(title)}</div>${body}</div>`; }
  function settingsRow(label, value, extra) {
    return `<div class="settings-row"><div class="settings-row-text"><div>${esc(label)}</div>${extra ? `<div class="settings-row-sub">${esc(extra)}</div>` : ''}</div><div class="settings-row-val">${value}</div></div>`;
  }
  function onOff(on) { return `<span class="settings-pill ${on ? 'on' : 'off'}">${on ? 'On' : 'Off'}</span>`; }
  function renderSettings() {
    const s = settingsData && link.state === 'online' ? settingsData : null;
    const st = link.status || {};
    const online = link.state === 'online';
    setText('settings-sub', online ? (link.pcName || 'PC') + (link.route === 'tailnet' ? ' · via Tailscale' : ' · home Wi-Fi') : 'This phone');
    let html = '';

    const mode = themeMode();
    html += settingsSection('Look', `<div class="transfer-chips-row settings-chips">${[['system', 'Like the phone'], ['light', 'Light'], ['dark', 'Dark']].map(([k, l]) =>
      `<button class="transfer-chip ${mode === k ? 'active' : ''}" data-act="theme" data-theme="${k}">${l}</button>`).join('')}</div>`);

    if (online) {
      const p = (s && s.permissions) || st.permissions;
      if (p) {
        html += settingsSection('What this phone may do', PERMS.filter(([k]) => k in p).map(([k, l]) => settingsRow(l, onOff(!!p[k]))).join('') +
          '<div class="sheet-note">Change these in Cloud-SSD on the PC → Mobile.</div>');
      }
      if (s && Array.isArray(s.devices)) {
        html += settingsSection('Paired phones', s.devices.map(d => settingsRow(d.name || 'Phone',
          d.isThisPhone ? '<span class="settings-pill on">This phone</span>' : d.isBlocked ? '<span class="settings-pill off">Blocked</span>' : '',
          (d.lastSeen ? 'Last seen ' + timeAgo(d.lastSeen) : 'Paired ' + fmtDate(d.pairedAt)) + (d.legacy ? ' · old shared key' : ''))).join('') +
          `<button class="m3-tonal-btn danger-text" data-act="unpair">Remove this phone from ${esc(link.pcName || 'the PC')}</button>`);
      }
      const off = s && s.offload;
      if (off) {
        const enabled = off.enabled != null ? off.enabled : off.Enabled;
        const threshold = off.minFreeSpaceThresholdGB != null ? off.minFreeSpaceThresholdGB : off.MinFreeSpaceThresholdGB;
        const targetName = off.targetDriveName || off.TargetDriveName || '';
        const total = off.laptopTotalSpaceGB || off.LaptopTotalSpaceGB || 0;
        const canEdit = !!(s.permissions && s.permissions.pcControl);
        html += settingsSection('Space saver on the PC', canEdit ? `
          <label class="settings-row"><div class="settings-row-text"><div>Move old files to the cloud when the PC is nearly full</div></div><input type="checkbox" id="set-offload-on" ${enabled ? 'checked' : ''}></label>
          <label class="settings-row"><div class="settings-row-text"><div>Start below (GB free)</div>${total ? `<div class="settings-row-sub">The PC disk has ${Math.round(total)} GB</div>` : ''}</div>
            <input type="number" class="modal-input settings-num" id="set-offload-gb" min="1" max="${Math.max(1, Math.floor(total || 9999))}" step="1" value="${threshold != null ? threshold : ''}"></label>
          ${(s.offloadTargets || []).length ? `<label class="settings-row"><div class="settings-row-text"><div>Move to</div></div>
            <select class="modal-input settings-select" id="set-offload-target">${s.offloadTargets.map(t => `<option value="${esc(t.id)}" ${t.name === targetName ? 'selected' : ''}>${esc(t.name)}</option>`).join('')}</select></label>` : ''}
          <button class="m3-filled-btn" data-act="settings-offload-save">Save</button>`
          : settingsRow('Moves old files to the cloud', onOff(!!enabled), threshold != null ? 'Starts below ' + threshold + ' GB free · ' + targetName : targetName) +
            '<div class="sheet-note">Turn on "Control the PC" on the PC to change this from the phone.</div>');
      }
      const away = (s && s.away) || st.away;
      if (away) {
        html += settingsSection('Away from home', away.allowed
          ? settingsRow('Tailscale', '<span class="settings-pill on">Allowed</span>', (away.dnsName || (away.ips || [])[0] || 'PC is not signed in to Tailscale') +
            (link.route === 'tailnet' ? ' · in use now' : '')) + '<div class="sheet-note">Install Tailscale on this phone and sign in with the same account as the PC. The app switches to it by itself when the home Wi-Fi is not there.</div>'
          : settingsRow('Tailscale', '<span class="settings-pill off">Off</span>', 'Turn on "Allow Tailscale" in Cloud-SSD on the PC → Mobile'));
      }
    } else {
      html += settingsSection(link.pcName || 'PC', `<div class="sheet-note">${esc(link.state === 'unpaired' ? 'Not paired with a PC yet.' : (link.pcName || 'The PC') + ' is not reachable right now; its settings show here when it is.')}</div>`);
    }

    if (isApp) {
      // Tailscale on this phone: what it needs to reach the PC away from home.
      const ts = appInfo.tailscale || {};
      html += settingsSection('Tailscale on this phone', settingsRow('Tailscale',
        `<button class="drive-btn browse" data-act="tailscale-open">${ts.installed ? 'Open' : 'Install'}</button>`,
        !ts.installed ? 'Install it and sign in with the same account as the PC.' : ts.vpn ? 'Connected (VPN on).' : 'Installed. Turn it on when you are away from home.'));
      // What the PC may see and do with this phone (Space & RAM on the PC).
      let share = {};
      try { share = JSON.parse(native.getSpaceSharing()); } catch (e) { }
      if ('share' in share) {
        html += settingsSection('This phone on the PC',
          `<label class="settings-row"><div class="settings-row-text"><div>Show this phone's storage and RAM on the PC</div><div class="settings-row-sub">Sent while this app is open; when off, the PC keeps only the last numbers</div></div>
            <input type="checkbox" data-act="space-share" data-key="share" ${share.share ? 'checked' : ''}></label>
          <label class="settings-row"><div class="settings-row-text"><div>Let the PC remove things on this phone</div><div class="settings-row-sub">Delete, move to the cloud, uninstall — Android still asks on the phone where it must</div></div>
            <input type="checkbox" data-act="space-share" data-key="control" ${share.control ? 'checked' : ''}></label>` +
          (share.usageAccess ? '' : `<button class="m3-tonal-btn" data-act="space-usage">Allow usage access (app sizes)</button>`));
      }
    }

    if (isApp && drive.available) {
      const accs = drive.accounts || [];
      html += settingsSection('Google Drive on this phone', (accs.length ? accs.map(a => settingsRow(a.email,
        `<button class="drive-btn unmount" data-act="drive-remove" data-email="${esc(a.email)}">Remove</button>`,
        a.needsReauth ? 'Sign in again' : a.totalBytes > 0 ? fmtBytes(a.freeBytes) + ' free of ' + fmtBytes(a.totalBytes) : 'Signed in')).join('')
        : '<div class="sheet-note">Sign in with the same Google accounts as on the PC to open your files when the PC is off.</div>') +
        `<label class="settings-row"><div class="settings-row-text"><div>Use Google Drive directly when the PC is off</div><div class="settings-row-sub">Very new changes made on the PC may take a moment to appear</div></div>
          <input type="checkbox" data-act="drive-direct" ${drive.direct ? 'checked' : ''}></label>
        <button class="m3-tonal-btn" data-act="drive-add">${accs.length ? 'Add another Google account' : 'Sign in to Google'}</button>`);
    }

    html += settingsSection('About', settingsRow('Phone app', esc(isApp ? (appInfo.version || '—') : 'Browser')) +
      settingsRow('Cloud-SSD on the PC', esc(st.version || (s && s.version) || '—')) +
      (s ? settingsRow('This phone\'s key', esc(s.auth === 'legacy' ? 'Old shared key' : 'Its own key')) : ''));
    $('settings-body').innerHTML = html;
  }
  async function saveOffload() {
    const params = { enabled: $('set-offload-on').checked ? 'true' : 'false' };
    const gb = $('set-offload-gb').value.trim();
    if (gb) params.thresholdGb = gb;
    const t = $('set-offload-target');
    if (t && t.value) params.targetMountId = t.value;
    try {
      const r = await api('/api/storage/offload-config', { method: 'POST', params, timeout: 15000 });
      toast(r.ok ? 'Space saver updated on the PC' : apiError(r));
      if (r.ok) { pollStatus(); openSettings(); }
    } catch (e) { toast('PC did not answer'); }
  }

  // ------------------------------------------------------------------ search
  const search = { q: '', timer: null, seq: 0, phoneReq: -1, cloud: [], phone: [], scope: ls.get('cloudssd_search_scope') === 'all' ? 'all' : 'folder' };
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
    const source = cloudSource();
    const scope = search.scope;
    document.querySelectorAll('#search-scope-row .transfer-chip').forEach(c => c.classList.toggle('active', c.dataset.scope === scope));
    if (source === 'none') {
      cloudBox.innerHTML = '<div class="recent-empty-card">PC is not connected, so cloud files cannot be searched.</div>';
      setText('search-cloud-count', '0');
      return;
    }
    const pl = placeOf(ssd.path) || ssdPlaces().find(p => p.cloud && p.mounted);
    if (!pl) { cloudBox.innerHTML = '<div class="recent-empty-card">No cloud drive is connected.</div>'; return; }
    const where = ssd.path && placeOf(ssd.path) ? ssd.path : pl.root;
    const all = scope === 'all';
    setText('search-cloud-where', all ? 'in all cloud drives' : 'in ' + (where.length > 28 ? '…' + where.slice(-26) : where));
    cloudBox.innerHTML = skeleton(4);
    let found = [], truncated = false, error = '';
    try {
      if (source === 'drive') {
        const r = await cloudOp('search', { path: all ? (ssdPlaces()[0] || pl).root : where, q, max: 150 });
        if (r.ok && r.data) {
          found = (r.data.entries || []).map(e => ({ name: e.name, path: e.id, size: e.size, modified: e.modified, isFolder: e.isDirectory, src: 'cloud' }));
        } else error = r.error || 'Google Drive did not answer';
      } else {
        const params = { path: where, search: q, folders: 1, depth: 6 };
        if (all) params.scope = 'all';
        const r = await api('/api/files', { params, timeout: 45000 });
        if (r.ok && r.data) {
          found = (r.data.folders || []).map(fo => ({ name: fo.name, path: fo.path, modified: fo.modified, isFolder: true, src: 'cloud' }))
            .concat((r.data.files || []).map(f => Object.assign(f, { src: 'cloud' })));
          truncated = !!r.data.truncated;
        } else error = apiError(r);
      }
    } catch (e) { error = 'PC did not answer.'; }
    if (seq !== search.seq) return;
    search.cloud = found;
    setText('search-cloud-count', String(found.length));
    const parentOf = (p) => p.slice(0, Math.max(p.lastIndexOf(sepOf(p)), isCloudId(p) ? p.indexOf(':') + 1 : 0));
    cloudBox.innerHTML = found.length ? found.map((f, i) => `
      <div class="explorer-item ${f.isFolder ? 'folder-item' : 'file-item'}" data-act="search-cloud" data-i="${i}">
        <div class="item-icon ${f.isFolder ? 'folder-icon' : 'kind-' + kindOf(f.name)}">${f.isFolder ? SVG.folder : iconFor(f.name)}</div>
        <div class="item-details"><div class="item-name">${esc(f.name)}</div><div class="item-meta">${f.isFolder ? 'Folder' : f.size > 0 ? fmtBytes(f.size) : '—'} · ${esc(parentOf(f.path))}</div></div>
      </div>`).join('') + (truncated ? '<div class="list-note">More matches exist; open a folder to search inside it.</div>' : '')
      : `<div class="recent-empty-card">${error ? esc(error) : all ? 'Nothing named like that on the cloud drives.' : 'Nothing named like that in this folder. Try "All cloud drives".'}</div>`;
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
      if (!await confirmDialog('Unpair this phone?', 'The PC forgets this phone. You will need the pairing code from the PC to connect again.', 'Unpair')) return;
      closeSheet('connection-modal');
      closeSheet('settings-modal');
      // Tell the PC first, so the key stops working there too (an older PC just forgets it on its own list).
      if (link.state === 'online' && pcHas('device-tokens')) {
        try { await api('/api/devices/remove', { method: 'POST', params: { id: 'self' }, timeout: 5000 }); } catch (e) { }
      }
      onUnpaired();
    },
    'open-settings': () => openSettings(),
    'theme': (el) => {
      if (el.dataset.theme === 'system') ls.del('cloudssd_theme'); else ls.set('cloudssd_theme', el.dataset.theme);
      applyTheme();
      renderSettings();
    },
    'settings-offload-save': () => saveOffload(),
    'drive-add': () => { try { native.driveAddAccount(); toast('Opening Google sign-in…'); } catch (e) { toast('Update the Cloud-SSD app'); } },
    'drive-remove': async (el) => {
      if (!await confirmDialog('Remove ' + el.dataset.email + '?', 'This phone signs out of that Google account. Nothing is deleted from Google Drive.', 'Remove')) return;
      try { native.driveRemoveAccount(el.dataset.email); } catch (e) { }
    },
    'drive-direct': (el) => {
      try { native.setDriveDirect(!!el.checked); } catch (e) { }
      drive.direct = !!el.checked;
      if (activeTab === 'ssd') openSsdTab();
    },
    'details-copy': () => {
      const f = detailsFile;
      if (!f) return;
      const text = f.path;
      if (isApp) { try { native.copyText(text); toast('Location copied'); return; } catch (e) { } }
      if (navigator.clipboard) navigator.clipboard.writeText(text).then(() => toast('Location copied'), () => toast(text));
      else toast(text);
    },
    'details-size': async (el) => {
      const f = detailsFile;
      if (!f) return;
      el.disabled = true;
      el.textContent = 'Counting…';
      if (cloudSource() === 'pc') {
        const id = isCloudId(f.path) ? f.path : (window.CloudSsdFs && window.CloudSsdFs.idOf(f.path));
        try {
          const r = await api('/api/fs/usage', { params: { path: id }, timeout: 60000 });
          el.textContent = r.ok && r.data ? `${r.data.bytesFormatted || fmtBytes(r.data.bytes)} · ${r.data.files} files${r.data.truncated ? ' (still counting stopped early)' : ''}` : apiError(r);
        } catch (e) { el.textContent = 'PC did not answer'; }
      } else el.textContent = 'Only with the PC';
    },
    'choice-pick': (el) => { const a = choices[+el.dataset.i]; closeSheet('choice-sheet'); if (a) a.fn(); },
    'save-open': (el) => { const s = saves[+el.dataset.i]; if (s) openSave(s); },
    'transfers-clear': async () => {
      if (!await confirmDialog('Clear the transfer history?', 'The list of finished transfers on ' + (link.pcName || 'the PC') + ' is emptied. Files are not touched.', 'Clear')) return;
      try {
        const r = await api('/api/transfers/clear', { method: 'POST' });
        if (r.ok) { xferHistory = []; renderHistory(); toast('History cleared'); } else toast(apiError(r));
      } catch (e) { toast('PC did not answer'); }
      // Failed transfers on this phone go too.
      Array.from(transfers.values()).filter(t => t.state === 'failed').forEach(t => cancelTransfer(t.id));
    },
    'xfer-retry': (el) => retryTransfer(+el.dataset.id),
    'search-scope': (el) => {
      search.scope = el.dataset.scope === 'all' ? 'all' : 'folder';
      ls.set('cloudssd_search_scope', search.scope);
      if (search.q) runSearch(search.q);
      else document.querySelectorAll('#search-scope-row .transfer-chip').forEach(c => c.classList.toggle('active', c === el));
    },
    'gallery-prev': () => galleryStep(-1),
    'gallery-next': () => galleryStep(1),
    'ssd-select': () => { if (ssd.selecting) endSsdSelect(); else { startSsdSelect(); toast('Tap files and folders to select them'); } },
    'ssd-select-all': () => {
      const all = ssd.view.folders.map((_, i) => 'd' + i).concat(ssd.view.files.map((_, i) => 'f' + i));
      const every = all.every(k => ssd.selected.has(k));
      ssd.selected = new Set(every ? [] : all);
      document.querySelectorAll('#ssd-items-container [data-key]').forEach(el => el.classList.toggle('selected', ssd.selected.has(el.dataset.key)));
      renderSsdSelectBar();
    },
    'ssd-select-cancel': () => endSsdSelect(),
    'ssd-bulk': (el) => ssdBulk(el.dataset.what),
    'pair-pick': (el) => { pair.selected = el.dataset.url; $('pair-address').value = ''; renderPairPcs(); },
    'pair-rescan': () => {
      const typed = normalizeServer($('pair-address').value);
      if (typed) { pair.selected = ''; submitPair(); } else if (isApp) startDiscovery('pair');
    },
    'pair-submit': () => submitPair(),
    'pair-close': () => hidePair(),

    // Home tiles open Space & RAM (space.js): this phone's storage or RAM, with the PC one tap away.
    'open-ram': () => {
      if (window.CloudSsdSpace) { window.CloudSsdSpace.open(isApp ? 'phone' : 'pc', 'ram'); return; }
      renderRam(); openSheet('ram-breakdown-modal');
    },
    'open-storage': () => {
      if (window.CloudSsdSpace) { window.CloudSsdSpace.open(isApp ? 'phone' : 'pc', 'overview'); return; }
      if (!isApp) { toast('Phone storage is shown in the Android app'); return; }
      renderStorage(); requestScan(); openSheet('storage-modal');
    },
    'open-storage-sheet': () => { if (isApp) { renderStorage(); requestScan(); openSheet('storage-modal'); } },
    'space-share': (el) => {
      const o = {};
      o[el.dataset.key] = !!el.checked;
      try { native.setSpaceSharing(JSON.stringify(o)); } catch (e) { toast('Update the Cloud-SSD app'); }
    },
    'tailscale-open': () => { try { native.openTailscale(); } catch (e) { toast('Update the Cloud-SSD app'); } },
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
      if (!t || !t.filePath || t.direction === 'offload' || link.state !== 'online') return;
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
      } else openFileSheet(f, phone.items.files);
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
    'ssd-refresh': () => {
      if (cloudSource() === 'drive') { if (ssd.path) { ssd.cache.delete(ssd.path); loadSsd(ssd.path, true); } else openSsdTab(); if (link.state !== 'paused') pollStatus(); }
      else if (link.state !== 'online') pollStatus(); else if (ssd.path) loadSsd(ssd.path, true); else openSsdTab();
    },
    'ssd-layout': () => { ssd.grid = !ssd.grid; ls.set('cloudssd_layout', ssd.grid ? 'grid' : 'list'); const c = ssd.cache.get(ssd.path); if (c) renderSsd(c); },
    'ssd-sort': () => {
      $('sort-options').innerHTML = SORTS.map(s => `<div class="m3-sheet-row ${s.key === ssd.sort ? 'chosen' : ''}" data-act="ssd-sort-pick" data-key="${s.key}">${s.key === ssd.sort ? SVG.check : '<svg viewBox="0 0 24 24"></svg>'}<div class="m3-sheet-row-text">${s.label}</div></div>`).join('');
      openSheet('sort-sheet');
    },
    'ssd-sort-pick': (el) => { ssd.sort = el.dataset.key; ls.set('cloudssd_sort', ssd.sort); closeSheet('sort-sheet'); const c = ssd.cache.get(ssd.path); if (c) renderSsd(c); },
    'ssd-show-all': () => { ssd.showAll = true; const c = ssd.cache.get(ssd.path); if (c) renderSsd(c); },
    'ssd-folder': (el) => {
      if (Date.now() - ssd.suppressClick < 700) return;
      if (ssd.selecting) { toggleSsdItem(el.dataset.key); return; }
      const fo = ssd.view.folders[+el.dataset.i]; if (fo) loadSsd(fo.path);
    },
    'ssd-file': (el) => {
      if (Date.now() - ssd.suppressClick < 700) return;
      if (ssd.selecting) { toggleSsdItem(el.dataset.key); return; }
      const f = ssd.view.files[+el.dataset.i];
      if (!f) return;
      // Photos open straight in the viewer, so they can be swiped through like a gallery.
      if (kindOf(f.name) === 'images') openViewer(f, ssd.view.files);
      else openFileSheet(f, ssd.view.files);
    },
    'ssd-more': (el) => { const f = ssd.view.files[+el.dataset.i]; if (f) openFileSheet(f, ssd.view.files); },
    'search-cloud': (el) => {
      const f = search.cloud[+el.dataset.i];
      if (!f) return;
      if (f.isFolder) { closeSearch(true); if (activeTab !== 'ssd') { ssd.path = f.path; switchTab('ssd'); } else loadSsd(f.path); return; }
      openFileSheet(f);
    },
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
    if (activeTab === 'ssd' && ssd.selecting) { endSsdSelect(); return true; }
    if (activeTab === 'ssd' && cloudSource() !== 'none' && ssdUp()) return true;
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
        case 'pair': {
          // The same link is also kept for the start of the page; take it once so a one-time code is not used twice.
          let o = data;
          try { const p = native.consumePairLink(); o = p ? JSON.parse(p) : null; } catch (e) { }
          if (o) applyPairLink(o).then(ok => { if (ok) toast('Paired with ' + (link.pcName || 'your PC')); });
          break;
        }
        case 'pdf': onPdf(data); break;
        case 'cloud': { const r = cloudReqs.get(data.reqId); if (r) { cloudReqs.delete(data.reqId); r(data); } break; }
        case 'drive':
          refreshDrive();
          if (data.ok === false) toast(data.error || 'Google sign-in did not finish');
          else if (data.email) toast('Signed in as ' + data.email);
          else if (data.removed) toast('Signed out of ' + data.removed);
          drive.roots = [];
          if (isOpen('settings-modal')) renderSettings();
          if (activeTab === 'ssd' && link.state !== 'online') openSsdTab();
          break;
        case 'mediaFolders': if (window.CloudSsdFs && window.CloudSsdFs.onMediaFolders) window.CloudSsdFs.onMediaFolders(data); break;
        case 'resume':
          if (data.storageAccess !== phone.access) {
            phone.access = data.storageAccess;
            renderPermBanner();
            requestScan();
            if (phone.view && activeTab === 'browser') phoneShow(phone.view);
          }
          refreshAppInfo();
          refreshDrive();
          applyTheme();
          relistTransfers();
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
    if (activeTab === 'home' && ticks % 3 === 0) refreshPcLive();
    if (isOpen('ram-breakdown-modal')) renderRam();
    if (isOpen('storage-modal')) { renderPhoneTiles(); if (ticks % 15 === 0) requestScan(); }
    if ((isOpen('drives-manager-modal') || isOpen('cloud-modal')) && ticks % 4 === 0 && !Object.keys(driveBusy).length) loadDrives();
    if (activeTab === 'gaming' && ticks % 2 === 0) pollHud();
    if (activeTab === 'home' && ticks % 20 === 0) refreshHistory();
    if (activeTab === 'browser' && !phone.view && ticks % 30 === 0) requestScan();
    // "Trying again in 12 s" counts down.
    for (const t of transfers.values()) { if (t.state === 'waiting') { renderTransfers(); break; } }
    // The PC is off: the Google Drive figures on Home come from the phone itself.
    if (link.state !== 'online' && isApp && ticks % 30 === 0) { refreshDrive(); renderStatusTiles(); }
  }

  // ------------------------------------------------------------------ start
  function boot() {
    document.body.dataset.tab = 'home';
    document.body.classList.toggle('in-app', isApp);
    refreshAppInfo();
    applyTheme();
    if (window.matchMedia) {
      const mq = window.matchMedia('(prefers-color-scheme: light)');
      const onChange = () => { if (themeMode() === 'system') applyTheme(); };
      if (mq.addEventListener) mq.addEventListener('change', onChange); else if (mq.addListener) mq.addListener(onChange);
    }

    // A leftover service worker from old versions served stale files: remove it.
    if ('serviceWorker' in navigator && /^https?:/.test(location.protocol)) {
      navigator.serviceWorker.getRegistrations().then(rs => rs.forEach(r => r.unregister())).catch(() => { });
    }

    // Opened from the PC's QR code in a browser: http://<pc>:8090/?pair=… (one-time code) or, from an older PC, ?token=…
    const params = new URLSearchParams(location.search);
    const urlToken = params.get('token');
    const urlPair = params.get('pair');
    let pairing = null;
    if (/^https?:/.test(location.protocol)) {
      if (urlPair || urlToken) {
        try { window.history.replaceState(null, '', location.pathname); } catch (e) { }
        if (urlToken) saveCreds(location.origin, urlToken);
        if (urlPair) {
          // A one-time code: this browser uses it now (the app pairs with the 6-digit code or finds the PC itself).
          pairing = applyPairLink({ host: location.host, pair: urlPair });
        } else if (!isApp && /Android/i.test(navigator.userAgent)) {
          // An older PC's link with its key: the same phone may have the app installed, so offer to hand it over.
          $('open-in-app-link').href = `intent://pair?host=${encodeURIComponent(location.host)}&token=${encodeURIComponent(urlToken)}#Intent;scheme=cloudssd;package=com.cloudssd.mobile;end`;
          show('browser-app-banner', true);
        }
      } else if (!link.server) {
        link.server = location.origin;
      }
    }
    if (isApp) {
      // Older versions kept the key in the page's storage: move it into the phone's encrypted store once.
      const oldToken = ls.get('cloudssd_token');
      try {
        const p = JSON.parse(native.getPairing() || '{}');
        if (oldToken && !p.token) native.setServerBase(link.server, oldToken);
        link.token = p.token || oldToken || '';
        if (!link.server && p.server) { link.server = p.server; link.lan = p.server; }
      } catch (e) { link.token = oldToken || ''; }
      ls.del('cloudssd_token');
      try {
        const pending = native.consumePairLink();
        if (pending) pairing = applyPairLink(JSON.parse(pending));
      } catch (e) { }
      try { phone.access = native.getStorageAccess(); } catch (e) { }
      refreshDrive();
      relistTransfers();
    }
    wireGallery();
    wireSsdLongPress();

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
    renderPcTiles();
    renderPermBanner();
    renderStorage();
    renderHistory();
    setText('cat-stat-starred', starred.length ? starred.length + ' files' : 'None yet');

    if (pairing) { /* the pairing link decides: it polls when it worked and shows the pairing screen when not */ }
    else if (link.server && link.token) pollStatus();
    else { link.state = 'unpaired'; renderLink(); renderStatusTiles(); showPair(''); }
    setInterval(tick, 1000);
  }

  // File management on the cloud drives (new folder, upload here, rename, move, delete) lives in fs.js.
  // Space & RAM (space.js): storage and RAM of this phone and of the PC, with what can be removed.
  if (window.CloudSsdSpace) {
    window.CloudSsdSpace.init({
      api, apiError, toast, esc, native, isApp, link, pcHas, openSheet, closeSheet, isOpen, confirmDialog, choiceSheet, actions,
      fmtBytes, fmtDate, timeAgo, info: () => appInfo
    });
  }
  if (window.CloudSsdFs) {
    window.CloudSsdFs.init({
      api, apiUrl, apiError, toast, esc, SVG, native, isApp, link, ssd, placeOf, loadSsd, openSheet, closeSheet, confirmDialog, actions,
      isCloudId, cloudSource, cloudOp, pcHas, choiceSheet, drive
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
