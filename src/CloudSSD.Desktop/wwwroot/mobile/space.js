// Cloud-SSD mobile — "Space & RAM": where the storage and the RAM of this phone and of the PC go, live, with a ⋮
// menu on every line that says what an action frees before it is done. The phone's numbers come from the app
// (AndroidBridge.spaceGet / spaceAction), the PC's from /api/space/*. When the PC is off or does not share, its last
// numbers are shown with their time and without the menus. Loaded before app.js, which calls CloudSsdSpace.init().
(function () {
  'use strict';

  let app = null;
  const $ = (id) => document.getElementById(id);
  const st = {
    device: 'phone', tab: 'overview', seq: 0, reqs: new Map(),
    folder: { phone: '', pc: '' }, timer: null, busy: false,
    pc: null,          // last PC data {at, live, summary, folder, apps, ram, cleanups, large, duplicates}
    phone: {}          // last phone data
  };

  const ICON = {
    more: '<svg viewBox="0 0 24 24"><path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z"/></svg>',
    trash: '<svg viewBox="0 0 24 24"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></svg>',
    cloud: '<svg viewBox="0 0 24 24"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM14 13v4h-4v-4H7l5-5 5 5h-3z"/></svg>',
    open: '<svg viewBox="0 0 24 24"><path d="M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z"/></svg>',
    folder: '<svg viewBox="0 0 24 24"><path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/></svg>',
    app: '<svg viewBox="0 0 24 24"><path d="M4 8h4V4H4v4zm6 12h4v-4h-4v4zm-6 0h4v-4H4v4zm0-6h4v-4H4v4zm6 0h4v-4h-4v4zm6-10v4h4V4h-4zm-6 4h4V4h-4v4zm6 6h4v-4h-4v4zm0 6h4v-4h-4v4z"/></svg>',
    stop: '<svg viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm4 14H8V8h8v8z"/></svg>',
    clean: '<svg viewBox="0 0 24 24"><path d="M19.36 2.72l1.42 1.42-5.72 5.71c1.07 1.54 1.22 3.39.32 4.59L9.06 8.12c1.2-.9 3.05-.75 4.59.32l5.71-5.72zM5.93 17.57c-2.01-2.01-3.24-4.41-3.58-6.65l4.88-2.09 7.44 7.44-2.09 4.88c-2.24-.34-4.64-1.57-6.65-3.58z"/></svg>'
  };

  const fmt = (b) => app.fmtBytes(b > 0 ? b : 0);
  const when = (t) => t ? app.timeAgo(t) : 'never';

  // ---- data ------------------------------------------------------------------------------------------------------

  function phoneGet(what, arg) {
    return new Promise(resolve => {
      const id = ++st.seq;
      st.reqs.set(id, resolve);
      try { app.native.spaceGet(id, what, arg || ''); } catch (e) { st.reqs.delete(id); resolve({ ok: false, error: 'Update the Cloud-SSD app' }); }
      setTimeout(() => { if (st.reqs.has(id)) { st.reqs.delete(id); resolve({ ok: false, error: 'The phone took too long' }); } }, 60000);
    });
  }

  function savePc() {
    try { localStorage.setItem('cloudssd_space_pc', JSON.stringify(Object.assign({}, st.pc, { live: false }))); } catch (e) { }
  }
  function loadPc() {
    if (st.pc) return;
    try { st.pc = JSON.parse(localStorage.getItem('cloudssd_space_pc') || 'null'); } catch (e) { st.pc = null; }
    if (st.pc) st.pc.live = false;
  }

  /** PC data; when the PC does not answer (off, away, switch off) the last saved numbers stay, marked as not live. */
  async function pcGet(what, params) {
    loadPc();
    st.pc = st.pc || {};
    const usable = app.link.state === 'online' && app.pcHas('space');
    if (usable) {
      try {
        const r = await app.api('/api/space/' + what, { params: params || {}, timeout: 30000 });
        if (r.ok) {
          st.pc[what] = r.data;
          st.pc.at = Date.now();
          st.pc.live = true;
          savePc();
          return r.data;
        }
        st.pc.live = false;
        st.pc.why = r.status === 403 ? 'Space & RAM is switched off on the PC.' : app.apiError(r);
      } catch (e) { st.pc.live = false; st.pc.why = 'PC did not answer.'; }
    } else {
      st.pc.live = false;
      st.pc.why = app.link.state === 'online' ? 'Update Cloud-SSD on the PC to see this.' : (app.link.pcName || 'The PC') + ' is not connected.';
    }
    return st.pc[what] || null;
  }

  async function get(what, arg) {
    if (st.device === 'pc') {
      const params = what === 'folder' ? { path: arg } : {};
      const data = await pcGet(what, params);
      if (what === 'folder' && data) st.pc.folders = Object.assign(st.pc.folders || {}, { [data.path]: data });
      if (what === 'folder' && !data && st.pc && st.pc.folders) return st.pc.folders[arg] || null;
      return data;
    }
    const r = await phoneGet(what, arg);
    if (!r.ok) { app.toast(r.error || 'Could not read the phone'); return st.phone[what] || null; }
    st.phone[what] = r.data;
    return r.data;
  }

  function live() { return st.device === 'phone' || (st.pc && st.pc.live); }

  // ---- screen ----------------------------------------------------------------------------------------------------

  function open(device, tab) {
    st.device = device || (app.isApp ? 'phone' : 'pc');
    st.tab = tab || 'overview';
    render(true);
    app.openSheet('space-modal');
    clearInterval(st.timer);
    let ticks = 0;
    st.timer = setInterval(() => {
      if (!app.isOpen('space-modal')) { clearInterval(st.timer); return; }
      ticks++;
      // RAM moves by the second; the storage overview costs more to read, so it follows every 10 s.
      if (!document.hidden && !st.busy && (st.tab === 'ram' || (st.tab === 'overview' && ticks % 4 === 0))) render(false);
    }, 2500);
  }

  async function render(full) {
    const body = $('space-body');
    if (!body) return;
    if (full) body.innerHTML = '<div class="skeleton-row"><div class="skeleton-icon"></div><div class="skeleton-lines"><div></div><div></div></div></div>'.repeat(5);
    renderHead();
    st.busy = true;
    try {
      if (st.tab === 'overview') await renderOverview(body);
      else if (st.tab === 'folders') await renderFolder(body, st.folder[st.device]);
      else if (st.tab === 'apps') await renderApps(body);
      else if (st.tab === 'ram') await renderRam(body);
      else if (st.tab === 'clean') await renderClean(body);
    } finally { st.busy = false; }
    renderHead();
  }

  function renderHead() {
    const pcName = app.link.pcName || 'PC';
    const chips = (app.isApp ? [['phone', 'This phone']] : []).concat([['pc', pcName]]);
    $('space-devices').innerHTML = chips.map(([k, l]) => `<button class="transfer-chip ${st.device === k ? 'active' : ''}" data-act="space-device" data-device="${k}">${app.esc(l)}</button>`).join('');
    const tabs = [['overview', 'Overview'], ['folders', 'Folders'], ['apps', 'Apps'], ['ram', 'RAM'], ['clean', 'Clean up']];
    $('space-tabs').innerHTML = tabs.map(([k, l]) => `<button class="transfer-chip ${st.tab === k ? 'active' : ''}" data-act="space-tab" data-tab="${k}">${l}</button>`).join('');
    let status;
    if (st.device === 'phone') status = 'Live from this phone';
    else if (st.pc && st.pc.live) status = 'Live from ' + pcName;
    else status = (st.pc && st.pc.at ? 'Last seen ' + when(st.pc.at) + '. ' : 'Nothing from the PC yet. ') + ((st.pc && st.pc.why) || '');
    const tag = $('space-live');
    tag.className = 'conn-status-tag ' + (live() ? 'connected' : 'offline');
    tag.textContent = status;
  }

  function bar(fraction) {
    return `<div class="usage-bar"><div style="width:${Math.max(0, Math.min(100, fraction * 100)).toFixed(1)}%"></div></div>`;
  }
  function row(o) {
    // o: {icon, name, sub, size, fraction, act, attrs, more}
    return `<div class="usage-row ${o.act ? 'tappable' : ''}" ${o.act ? `data-act="${o.act}" ${o.attrs || ''}` : ''}>
      <span class="space-row-icon">${o.icon || ''}</span>
      <div class="usage-main">
        <div class="usage-name">${app.esc(o.name)}</div>
        ${o.sub ? `<div class="usage-sub">${app.esc(o.sub)}</div>` : ''}
        ${o.fraction != null ? bar(o.fraction) : ''}
      </div>
      <div class="usage-val">${app.esc(o.size || '')}</div>
      ${o.more ? `<button class="item-more-btn space-more" data-act="space-more" data-key="${app.esc(o.more)}">${ICON.more}</button>` : ''}
    </div>`;
  }

  // Rows whose ⋮ menu can be opened: key → {title, items}
  let menus = new Map();
  function menuKey(title, items) {
    const k = 'm' + menus.size;
    menus.set(k, { title, items });
    return k;
  }

  async function renderOverview(body) {
    const s = await get('summary');
    menus = new Map();
    if (!s) { body.innerHTML = empty('Nothing to show yet.'); return; }
    const volumes = s.volumes || [];
    const total = volumes.reduce((a, v) => a + v.total, 0), free = volumes.reduce((a, v) => a + v.free, 0);
    const disk = volumes[0] ? volumes[0].total : total;
    let html = `<div class="space-hero">
      <div><div class="ram-hero-big">${fmt(total - free)}</div><div class="ram-hero-cap">used</div></div>
      <div><div class="ram-hero-big">${fmt(free)}</div><div class="ram-hero-cap">free</div></div>
      <div><div class="ram-hero-big">${fmt(s.ram ? s.ram.total - s.ram.available : 0)}</div><div class="ram-hero-cap">RAM in use</div></div>
    </div>`;
    if (!s.ready) html += `<div class="sheet-note">${s.scannedAt ? 'Reading the storage again (' + Math.round((s.progress || 0) * 100) + '%). Numbers are from ' + when(Date.parse(s.scannedAt)) + '.' : 'Reading the storage for the first time…'}</div>`;
    html += '<div class="sheet-subtitle">Disks</div><div class="usage-rows">' + volumes.map(v => row({
      icon: ICON.folder, name: v.label, sub: fmt(v.free) + ' free of ' + fmt(v.total), size: fmt(v.total - v.free),
      fraction: v.total > 0 ? (v.total - v.free) / v.total : 0, act: 'space-folder', attrs: `data-path="${app.esc(v.root)}"`
    })).join('') + '</div>';
    html += '<div class="sheet-subtitle">What takes the space</div><div class="usage-rows">' + (s.groups || []).filter(g => g.bytes > 0)
      .sort((a, b) => b.bytes - a.bytes).map(g => row({
        icon: g.path ? ICON.folder : ICON.app, name: g.label, sub: g.note, size: fmt(g.bytes), fraction: disk > 0 ? g.bytes / disk : 0,
        act: g.path ? 'space-folder' : '', attrs: g.path ? `data-path="${app.esc(g.path)}"` : '',
        more: groupMenu(g)
      })).join('') + '</div>';
    html += '<div class="sheet-note">Tap a line to see what is inside. ⋮ says how much an action frees before it is done.</div>';
    body.innerHTML = html;
  }

  function groupMenu(g) {
    if (!live()) return '';
    const items = [];
    for (const a of g.actions || []) {
      if (a === 'empty-bin') items.push(act(ICON.trash, 'Empty the Recycle Bin', 'Frees ' + fmt(g.bytes) + '. Cannot be undone.', 'empty-bin', 'bin', true));
      if (a === 'clean') items.push(act(ICON.clean, 'Clean old temporary files', 'Apps make them again when needed', 'clean', g.key, true));
    }
    return items.length ? menuKey(g.label, items) : '';
  }

  async function renderFolder(body, path) {
    const f = await get('folder', path || '');
    menus = new Map();
    if (!f) { body.innerHTML = empty(st.device === 'pc' && !live() ? 'This folder is not in the PC\'s last numbers.' : 'This folder cannot be read.'); return; }
    st.folder[st.device] = f.path;
    // C:\ › Users › name on the PC; Internal storage › DCIM › Camera on the phone.
    const list = [];
    if (f.path.includes('\\')) {
      let acc = '';
      f.path.split('\\').filter(Boolean).forEach((p, i) => { acc = i === 0 ? p + '\\' : acc.replace(/\\$/, '') + '\\' + p; list.push([i === 0 ? p + '\\' : p, acc]); });
    } else {
      const s = st.phone.summary;
      const root = (s && s.volumes && s.volumes[0] && s.volumes[0].root) || '/storage/emulated/0';
      let acc = root;
      list.push(['Internal storage', root]);
      if (f.path.startsWith(root + '/')) f.path.slice(root.length + 1).split('/').filter(Boolean).forEach(p => { acc += '/' + p; list.push([p, acc]); });
    }
    const crumbs = list.map(([name, path]) => `<span class="crumb" data-act="space-folder" data-path="${app.esc(path)}">${app.esc(name)}</span>`);
    const biggest = Math.max(1, ...(f.items || []).map(i => i.bytes));
    let html = `<div class="space-crumbs">${f.parent ? `<button class="m3-tonal-btn" data-act="space-folder" data-path="${app.esc(f.parent)}">↑ Up</button>` : ''}<div class="breadcrumbs">${crumbs.join('<span class="crumb-sep">›</span>')}</div></div>`;
    html += `<div class="sheet-note">${fmt(f.bytes)} in ${(f.items || []).length} items${f.note ? ' · ' + app.esc(f.note) : ''}</div>`;
    html += '<div class="usage-rows">' + (f.items || []).map(i => row({
      icon: i.isDir ? ICON.folder : ICON.open, name: i.name,
      sub: (i.isDir ? (i.files || 0).toLocaleString() + ' files' : '') + (i.modified ? (i.isDir ? ' · ' : '') + app.fmtDate(i.modified) : '') + (i.note ? ' · ' + i.note : '') + (i.protected ? ' · needed by the system' : ''),
      size: fmt(i.bytes), fraction: i.bytes / biggest, act: i.isDir ? 'space-folder' : '', attrs: i.isDir ? `data-path="${app.esc(i.path)}"` : '',
      more: itemMenu(i)
    })).join('') + '</div>';
    body.innerHTML = html;
  }

  function itemMenu(i) {
    const items = [];
    if (i.isDir) items.push({ icon: ICON.folder, title: 'Show what is inside', sub: '', fn: () => { st.tab = 'folders'; st.folder[st.device] = i.path; render(true); } });
    if (live()) {
      for (const a of i.actions || []) {
        if (a === 'open') items.push(act(ICON.open, st.device === 'pc' ? 'Show in File Explorer on the PC' : 'Open', '', 'open', i.path, false));
        if (a === 'cloud') items.push(act(ICON.cloud, 'Move to the cloud drive', 'Frees ' + fmt(i.bytes) + (st.device === 'pc' ? '; a shortcut stays on the PC' : '; removed from the phone once uploaded'), 'cloud', i.path, true));
        if (a === 'recycle') items.push(act(ICON.trash, st.device === 'pc' ? 'Put in the Recycle Bin' : 'Move to the trash', fmt(i.bytes) + ' · can be restored; space comes free when emptied', 'recycle', i.path, true));
        if (a === 'delete') items.push(act(ICON.trash, 'Delete for good', 'Frees ' + fmt(i.bytes) + '. Cannot be undone.', 'delete', i.path, true, true));
      }
    }
    items.push({ icon: ICON.open, title: 'Copy location', sub: i.path, fn: () => { try { if (app.isApp) app.native.copyText(i.path); else navigator.clipboard.writeText(i.path); app.toast('Location copied'); } catch (e) { app.toast(i.path); } } });
    return menuKey(i.name, items);
  }

  async function renderApps(body) {
    const apps = await get('apps');
    menus = new Map();
    let html = '';
    if (st.device === 'phone' && !(app.info().usageAccess)) html += usageBanner('App sizes need "Usage access" (Android asks once).');
    if (!apps || !apps.length) { body.innerHTML = html + empty('No apps to show.'); return; }
    const biggest = Math.max(1, ...apps.map(a => a.bytes));
    html += '<div class="usage-rows">' + apps.map(a => row({
      icon: ICON.app, name: a.name, sub: [a.publisher, a.version, a.installed ? 'installed ' + app.fmtDate(a.installed) : ''].filter(Boolean).join(' · '),
      size: a.bytes > 0 ? fmt(a.bytes) : '—', fraction: a.bytes > 0 ? a.bytes / biggest : null, more: appMenu(a)
    })).join('') + '</div>';
    html += `<div class="sheet-note">${st.device === 'pc' ? 'Sizes are what each installer reported. Uninstall opens the program\'s own uninstaller on the PC; finish it there.' : 'Uninstall and App info open Android\'s own screens; Android asks before anything is removed.'}</div>`;
    body.innerHTML = html;
  }

  function appMenu(a) {
    if (!live()) return '';
    const items = [];
    for (const k of a.actions || []) {
      if (k === 'uninstall') items.push(act(ICON.trash, 'Uninstall', a.bytes > 0 ? 'Frees about ' + fmt(a.bytes) : 'Removes the app', 'uninstall', a.id, true, true));
      if (k === 'app-info') items.push(act(ICON.app, 'App info', 'Clear cache, clear data, force stop', 'app-info', a.id, false));
    }
    return items.length ? menuKey(a.name, items) : '';
  }

  async function renderRam(body) {
    const r = await get('ram');
    menus = new Map();
    if (!r || !r.ram) { body.innerHTML = empty('RAM cannot be read.'); return; }
    const total = r.ram.total, used = total - r.ram.available;
    const share = total > 0 ? used / total : 0;
    // A short live history per device for the little chart (one point per refresh).
    const hist = (st.ramHist = st.ramHist || {})[st.device] = (st.ramHist[st.device] || []).concat([share]).slice(-60);
    const tone = share > 0.85 ? 'bad' : share > 0.7 ? 'warn' : 'ok';
    const ring = 2 * Math.PI * 52;
    const pts = hist.map((v, i) => `${(i / Math.max(1, hist.length - 1) * 300).toFixed(1)},${(60 - v * 56).toFixed(1)}`).join(' ');
    let html = `<div class="ram-gauge-wrap">
      <svg class="ram-gauge ${tone}" viewBox="0 0 120 120"><circle cx="60" cy="60" r="52" class="track"/>
        <circle cx="60" cy="60" r="52" class="fill" stroke-dasharray="${(ring * share).toFixed(1)} ${ring.toFixed(1)}" transform="rotate(-90 60 60)"/>
        <text x="60" y="58" text-anchor="middle" class="pct">${Math.round(share * 100)}%</text><text x="60" y="78" text-anchor="middle" class="cap">in use</text></svg>
      <div class="ram-gauge-text">
        <div><strong>${fmt(used)}</strong> in use</div>
        <div><strong>${fmt(r.ram.available)}</strong> available</div>
        <div><strong>${fmt(total)}</strong> total</div>
        ${r.ram.swapTotal > 0 ? `<div>Extended RAM <strong>${fmt(r.ram.swapUsed)}</strong> of ${fmt(r.ram.swapTotal)}</div>` : ''}
        ${r.ram.low ? '<div class="ram-low">Android says memory is low</div>' : ''}
      </div></div>
      <svg class="ram-chart" viewBox="0 0 300 60" preserveAspectRatio="none"><polyline points="${pts}"/></svg>
      <div class="sheet-note">Live · last ${hist.length > 1 ? Math.round(hist.length * 2.5 / 60 * 10) / 10 + ' min' : 'moments'}</div>`;
    html += '<div class="usage-rows">' + (r.ram.groups || []).map(g => row({ name: g.label, sub: g.note, size: fmt(g.bytes), fraction: total > 0 ? g.bytes / total : 0 })).join('') + '</div>';
    const apps = r.apps || [];
    if (st.device === 'phone') {
      html += `<div class="space-banner"><div>Android lets only its own screens show how much RAM each app uses. "Running services" lists every running app with its RAM, live.</div>
        <button class="m3-filled-btn" data-act="space-running">RAM of each app</button></div>`;
      html += '<div class="sheet-subtitle">Apps used in the last hour</div>';
      if (!app.info().usageAccess) html += usageBanner('With "Usage access" this list shows the apps used lately, each with Force stop.');
    } else html += '<div class="sheet-subtitle">Apps using RAM</div>';
    html += apps.length ? '<div class="usage-rows">' + apps.map(a => row({
      icon: ICON.app, name: a.title || a.name, sub: a.processes > 1 ? a.processes + ' processes' : '', size: a.bytes > 0 ? fmt(a.bytes) : '',
      fraction: a.bytes > 0 && total > 0 ? a.bytes / total : null, more: ramMenu(a)
    })).join('') + '</div>' : '';
    body.innerHTML = html;
  }

  function ramMenu(a) {
    if (!live()) return '';
    const items = [];
    for (const k of a.actions || []) {
      if (k === 'close') items.push(act(ICON.stop, 'Close', 'Frees about ' + fmt(a.bytes) + ' of RAM; it can still ask to save', 'close', a.id, false));
      if (k === 'kill') items.push(act(ICON.stop, 'End task now', 'Unsaved work in it is lost', 'kill', a.id, true, true));
      if (k === 'app-info') items.push(act(ICON.app, 'App info (Force stop)', 'Android\'s screen for this app', 'app-info', a.id, false));
    }
    return items.length ? menuKey(a.title || a.name, items) : '';
  }

  async function renderClean(body) {
    const cleanups = await get('cleanups');
    const large = await get('large');
    menus = new Map();
    let html = '<div class="sheet-subtitle">Clean in one go</div><div class="usage-rows">' + (cleanups || []).map(c => {
      const can = live() && (c.safe || c.id === 'bin') && (c.bytes > 0 || c.id === 'system' || c.id === 'windows-cleanup');
      return row({
        icon: ICON.clean, name: c.label, sub: c.description + (c.count > 0 ? ' · ' + c.count.toLocaleString() + ' files' : ''), size: c.bytes > 0 ? fmt(c.bytes) : '',
        more: can ? menuKey(c.label, [act(ICON.clean, c.id === 'system' || c.id === 'windows-cleanup' ? 'Open it' : 'Clean now', c.bytes > 0 ? 'Frees ' + fmt(c.bytes) : '',
          c.id === 'bin' ? 'empty-bin' : 'clean', c.id, c.id !== 'system' && c.id !== 'windows-cleanup', c.id === 'bin' || c.id === 'backed-up')]) : ''
      });
    }).join('') + '</div>';
    html += '<div class="sheet-subtitle">Largest files</div>';
    const biggest = Math.max(1, ...(large || []).map(i => i.bytes));
    html += (large && large.length) ? '<div class="usage-rows">' + large.map(i => row({
      icon: ICON.open, name: i.name, sub: i.path.slice(0, Math.max(i.path.lastIndexOf('/'), i.path.lastIndexOf('\\'))) + (i.modified ? ' · ' + app.fmtDate(i.modified) : ''),
      size: fmt(i.bytes), fraction: i.bytes / biggest, more: itemMenu(i)
    })).join('') + '</div>' : empty('No very large files.');
    if (st.device === 'pc' && live()) {
      html += '<div class="sheet-subtitle">Files that exist twice</div><div id="space-dupes">' + empty('Looking…') + '</div>';
      setTimeout(loadDuplicates, 0);
    }
    body.innerHTML = html;
  }

  async function loadDuplicates() {
    const d = await pcGet('duplicates');
    const box = $('space-dupes');
    if (!box || !d) return;
    if (!d.ready) { box.innerHTML = empty('Looking for files that exist twice… (a minute; open this again)'); return; }
    if (!d.groups.length) { box.innerHTML = empty('No duplicate files in your folders.'); return; }
    const waste = d.groups.reduce((a, g) => a + g[0].bytes * (g.length - 1), 0);
    box.innerHTML = `<div class="sheet-note">${d.groups.length} files exist more than once; the extra copies take ${fmt(waste)}.</div><div class="usage-rows">` +
      d.groups.slice(0, 40).map(g => g.slice(1).map(i => row({ icon: ICON.open, name: i.name, sub: 'Copy of ' + g[0].path, size: fmt(i.bytes), more: itemMenu(i) })).join('')).join('') + '</div>';
  }

  function usageBanner(text) {
    return `<div class="space-banner"><div>${app.esc(text)}</div><button class="m3-filled-btn" data-act="space-usage">Allow usage access</button></div>`;
  }
  function empty(text) { return `<div class="recent-empty-card">${app.esc(text)}</div>`; }

  // ---- doing it --------------------------------------------------------------------------------------------------

  /** A menu entry for a real action: asks first when it removes something, then says what it freed. */
  function act(icon, title, sub, kind, target, confirm, danger) {
    return {
      icon, title, sub, danger,
      fn: async () => {
        if (confirm && !await app.confirmDialog(title + '?', (sub || '') + (danger ? ' This cannot be undone.' : ''), title)) return;
        const r = await run(kind, target);
        app.toast((r.message || (r.ok ? 'Done' : 'Could not do that')) + (r.freed > 0 ? ' Freed ' + fmt(r.freed) + '.' : ''));
        render(false);
      }
    };
  }

  function run(kind, target) {
    if (st.device === 'phone') {
      return new Promise(resolve => {
        const id = ++st.seq;
        st.reqs.set(id, resolve);
        try { app.native.spaceAction(id, kind, target); } catch (e) { st.reqs.delete(id); resolve({ ok: false, message: 'Update the Cloud-SSD app' }); }
      });
    }
    return app.api('/api/space/action', { method: 'POST', params: { kind, target }, timeout: 600000 })
      .then(r => ({ ok: r.ok, message: r.data && r.data.message || app.apiError(r), freed: r.data && r.data.freedBytes || 0 }))
      .catch(() => ({ ok: false, message: 'PC did not answer' }));
  }

  // ---- wiring ----------------------------------------------------------------------------------------------------

  function buildDom() {
    const box = document.createElement('div');
    box.innerHTML = `
      <div id="space-modal" class="modal-backdrop sheet" data-act="sheet-close">
        <div class="connection-modal-sheet">
          <div class="m3-drag-handle"></div>
          <div class="conn-modal-header">
            <div class="conn-modal-icon-wrap storage-tint">${ICON.clean}</div>
            <div class="conn-modal-header-info">
              <div class="conn-modal-title">Space &amp; RAM</div>
              <div class="conn-modal-sub">Where storage and memory go, and what to remove</div>
              <div class="conn-status-tag connected" id="space-live">Live</div>
            </div>
            <button class="conn-modal-close" data-act="sheet-close-btn"><svg viewBox="0 0 24 24"><path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/></svg></button>
          </div>
          <div class="transfer-chip-row space-chips" id="space-devices"></div>
          <div class="transfer-chip-row space-chips" id="space-tabs"></div>
          <div id="space-body"></div>
        </div>
      </div>`;
    while (box.firstElementChild) document.body.appendChild(box.firstElementChild);
  }

  function init(helpers) {
    app = helpers;
    buildDom();
    Object.assign(app.actions, {
      'open-space': (el) => open(el.dataset.device, el.dataset.tab),
      'space-device': (el) => { st.device = el.dataset.device; render(true); },
      'space-tab': (el) => { st.tab = el.dataset.tab; render(true); },
      'space-folder': (el) => { st.tab = 'folders'; st.folder[st.device] = el.dataset.path; render(true); },
      'space-more': (el, e) => {
        if (e) e.stopPropagation();
        const m = menus.get(el.dataset.key);
        if (m) app.choiceSheet(m.title, m.items);
      },
      'space-usage': () => { try { app.native.openUsageAccess(); } catch (e) { app.toast('Update the Cloud-SSD app'); } },
      'space-running': () => { try { app.native.openRunningServices(); } catch (e) { app.toast('Open Settings → Developer options → Running services'); } }
    });
    const onNative = window.onNative;
    window.onNative = function (event, data) {
      if (event === 'space' || event === 'spaceResult') {
        const r = st.reqs.get(data.reqId);
        if (r) { st.reqs.delete(data.reqId); r(event === 'space' ? data : data); }
        return;
      }
      if (onNative) onNative(event, data);
      if (event === 'resume' && app.isOpen && app.isOpen('space-modal')) render(false);
    };
  }

  window.CloudSsdSpace = { init, open };
})();
