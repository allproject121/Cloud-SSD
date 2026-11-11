// Cloud-SSD mobile — using a cloud drive like normal storage from the Cloud SSD tab:
// new folder, upload here, rename, move, delete (one item or several). Everything goes through the PC's /api/fs/
// routes, or, when the PC is off and this phone is signed in to Google, straight to Google Drive through the app.
// Loaded before app.js, which hands over what this file needs through CloudSsdFs.init().
(function () {
  'use strict';

  let app = null;          // helpers and state from app.js
  let rows = [];           // actions of the sheet that is open
  let moving = null;       // { items: [{ id, name }] } — what was picked with "Move to…"
  let promptResolve = null;
  let uploadFolder = '';   // folder "Upload here" was started in, to refresh it when a file arrives
  let mediaFolders = null; // phone photo folders for the backup settings

  const ICON = {
    newFolder: '<svg viewBox="0 0 24 24"><path d="M20 6h-8l-2-2H4c-1.11 0-1.99.89-1.99 2L2 18c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V8c0-1.11-.89-2-2-2zm-1 8h-3v3h-2v-3h-3v-2h3V9h2v3h3v2z"/></svg>',
    rename: '<svg viewBox="0 0 24 24"><path d="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04a1 1 0 0 0 0-1.41l-2.34-2.34a1 1 0 0 0-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z"/></svg>',
    move: '<svg viewBox="0 0 24 24"><path d="M20 6h-8l-2-2H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-6 12v-3h-4v-4h4V8l5 5-5 5z"/></svg>',
    trash: '<svg viewBox="0 0 24 24"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></svg>',
    gear: '<svg viewBox="0 0 24 24"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.49.49 0 0 0 .12-.61l-1.92-3.32a.488.488 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54a.484.484 0 0 0-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>'
  };

  const $ = (id) => document.getElementById(id);

  // ---- which folder on which drive ---------------------------------------------------------------------------

  /** The connected cloud drive a path belongs to, or null (PC disks and the "From phone" folder are not managed here). */
  function place(path) {
    const pl = app.placeOf(path);
    return pl && pl.cloud && pl.id && pl.mounted ? pl : null;
  }
  /** "Z:\Photos\a.jpg" → "cloudssd:Photos/a.jpg": the PC names cloud items by drive, not by drive letter. */
  function idOf(path) {
    if (app.isCloudId(path)) return String(path);
    const pl = place(path);
    if (!pl) return null;
    return pl.id + ':' + String(path).slice(pl.root.length).split('\\').filter(Boolean).join('/');
  }
  function childId(folderId, name) { return folderId + (folderId.endsWith(':') ? '' : '/') + name; }
  /** Windows cannot store these characters, or a name that ends in a dot or space. */
  function cleanName(name) { return String(name).replace(/[\\/:*?"<>|\u0000-\u001f]/g, '_').replace(/[. ]+$/, ''); }

  /** One change on the cloud. Resolves to the answer, or null after telling the user why not (unless quiet). */
  async function call(route, params, quiet) {
    const source = app.cloudSource();
    const say = (m) => { if (!quiet) app.toast(m); return null; };
    if (source === 'none') return say('PC is not connected');
    if (source === 'drive') {
      const r = await app.cloudOp(route, Object.assign({}, params, params.dir ? { dir: true } : {}));
      if (r.ok) return r.data || {};
      return say(r.error || 'Google Drive did not answer');
    }
    try {
      const r = await app.api('/api/fs/' + route, { method: 'POST', params, timeout: 300000 });
      if (r.ok) return r.data || {};
      // A PC app from before file management answers every /api/fs/ route with this.
      const oldPc = r.status === 404 && r.data && r.data.error === 'API endpoint not found';
      return say(oldPc ? 'Update Cloud-SSD on the PC to manage files from the phone' : app.apiError(r));
    } catch (e) {
      return say('PC did not answer');
    }
  }
  function refresh() { if (app.ssd.path) { app.ssd.cache.delete(app.ssd.path); app.loadSsd(app.ssd.path, true); } }

  // ---- small dialogs -----------------------------------------------------------------------------------------

  function sheet(title, list) {
    rows = list;
    $('fs-sheet-title').textContent = title;
    $('fs-sheet-list').innerHTML = list.map((a, i) => `
      <div class="m3-sheet-row" data-act="fs-row" data-i="${i}">
        ${a.icon}
        <div class="m3-sheet-row-text"><div>${app.esc(a.title)}</div>${a.sub ? `<div class="m3-sheet-row-sub">${app.esc(a.sub)}</div>` : ''}</div>
      </div>`).join('');
    app.openSheet('fs-sheet');
  }
  /** Asks for a name; resolves to the trimmed text, or null when cancelled. */
  function ask(title, value, okLabel) {
    if (promptResolve) promptResolve(null);
    $('fs-prompt-title').textContent = title;
    $('fs-prompt-yes').textContent = okLabel || 'OK';
    const input = $('fs-prompt-input');
    input.value = value || '';
    app.openSheet('fs-prompt');
    setTimeout(() => {
      input.focus();
      const dot = input.value.lastIndexOf('.');
      input.setSelectionRange(0, dot > 0 ? dot : input.value.length);
    }, 120);
    return new Promise(resolve => { promptResolve = resolve; });
  }
  function answer(value) {
    const r = promptResolve;
    promptResolve = null;
    app.closeSheet('fs-prompt');
    if (r) r(value);
  }

  // ---- actions -----------------------------------------------------------------------------------------------

  async function newFolder() {
    const folder = idOf(app.ssd.path);
    const name = await ask('New folder', '', 'Create');
    if (!name) return;
    const made = await call('create', { parent: folder, name: cleanName(name), dir: 1 });
    if (made) { app.toast('Folder created: ' + (made.name || name)); refresh(); }
  }

  function uploadHere() {
    const folder = idOf(app.ssd.path);
    uploadFolder = app.ssd.path;
    if (app.isApp) {
      try { app.native.setUploadTarget(folder); app.native.pickAndUploadFiles('files'); }
      catch (e) { app.toast('Update the Cloud-SSD app to upload into a cloud folder'); }
    } else {
      $('fs-uploader').click();
    }
  }
  /** Phone browser without the app: send the chosen files straight to the folder. */
  async function uploadInBrowser(files, folder) {
    let done = 0;
    for (const file of files) {
      app.toast(`Uploading ${file.name} (${done + 1} of ${files.length})…`);
      try {
        const res = await fetch(app.apiUrl('/api/fs/write', { path: childId(folder, cleanName(file.name)), overwrite: 0 }), { method: 'POST', body: file });
        if (res.ok) done++;
        else { let msg = 'PC answered HTTP ' + res.status; try { msg = (await res.json()).error || msg; } catch (e) { } app.toast(msg); }
      } catch (e) { app.toast('PC not reachable'); break; }
    }
    if (done) { app.toast(done === 1 ? 'Uploaded 1 file' : `Uploaded ${done} files`); refresh(); }
  }

  async function rename(item, id) {
    const name = await ask('Rename', item.name, 'Rename');
    if (!name || name === item.name) return;
    const r = await call('rename', { path: id, name: cleanName(name) });
    if (r) { app.toast('Renamed to ' + (r.name || name)); refresh(); }
  }

  async function remove(item, id) {
    const ok = await app.confirmDialog(item.isFolder ? 'Delete this folder?' : 'Delete this file?',
      `"${item.name}"${item.isFolder ? ' and everything in it' : ''} will be moved to the trash of the cloud account.`, 'Delete');
    if (!ok) return;
    app.toast('Deleting ' + item.name + '…');
    const r = await call('delete', { path: id });
    if (r) { app.toast('Deleted ' + item.name); refresh(); }
  }

  /** Several files and folders at once; resolves to true when it ran. */
  async function bulkDelete(items) {
    const list = items.map(i => ({ name: i.name, id: idOf(i.path), isFolder: i.isFolder })).filter(i => i.id);
    if (!list.length) return false;
    const folders = list.filter(i => i.isFolder).length;
    const ok = await app.confirmDialog(`Delete ${list.length} ${list.length === 1 ? 'item' : 'items'}?`,
      `${list.length - folders} files${folders ? ` and ${folders} folders (with everything in them)` : ''} go to the trash of the cloud account.`, 'Delete');
    if (!ok) return false;
    let done = 0, failed = 0;
    for (const it of list) {
      app.toast(`Deleting ${done + failed + 1} of ${list.length}…`);
      if (await call('delete', { path: it.id }, true)) done++; else failed++;
    }
    app.toast(failed ? `Deleted ${done}, ${failed} could not be deleted` : `Deleted ${done} ${done === 1 ? 'item' : 'items'}`);
    refresh();
    return true;
  }

  /** startMove(item, id) for one item, or startMove([items]) for several. */
  function startMove(item, id) {
    const items = Array.isArray(item) ? item.map(i => ({ id: idOf(i.path), name: i.name })).filter(i => i.id) : [{ id, name: item.name }];
    if (!items.length) return;
    moving = { items };
    renderMoveBar();
    app.toast('Open the folder it should go to, then tap "Move here"');
  }
  async function moveHere() {
    if (!moving) return;
    const to = idOf(app.ssd.path);
    if (!to) { app.toast('Open a folder on a cloud drive first'); return; }
    const items = moving.items;
    let done = 0;
    const left = [];
    for (const it of items) {
      if (to === it.id || to.startsWith(it.id + '/')) { app.toast('A folder cannot go inside itself'); left.push(it); continue; }
      app.toast(items.length === 1 ? 'Moving ' + it.name + '…' : `Moving ${done + 1} of ${items.length}…`);
      if (await call('move', { path: it.id, to }, items.length > 1)) done++; else left.push(it);
    }
    // What did not move stays in the bar, so another folder can be tried.
    moving = left.length ? { items: left } : null;
    renderMoveBar();
    if (done) app.toast(items.length === 1 ? 'Moved ' + items[0].name : `Moved ${done} of ${items.length}`);
    refresh();
  }
  function renderMoveBar() {
    const bar = $('fs-move-bar');
    bar.style.display = moving ? 'flex' : 'none';
    if (moving) $('fs-move-text').textContent = moving.items.length === 1 ? 'Moving "' + moving.items[0].name + '"' : `Moving ${moving.items.length} items`;
  }

  /** Rename / move / delete for one cloud file or folder; empty for anything that is not on a cloud drive. */
  function itemActions(item) {
    const id = app ? idOf(item.path) : null;
    if (!id || item.readOnly) return [];
    return [
      { icon: ICON.rename, title: 'Rename', sub: 'Change the name on the cloud drive', fn: () => rename(item, id) },
      { icon: ICON.move, title: 'Move to…', sub: 'Then open the folder it should go to', fn: () => startMove(item, id) },
      { icon: ICON.trash, title: 'Delete', sub: 'Goes to the trash of the cloud account', fn: () => remove(item, id) }
    ];
  }

  // ---- camera backup (Android app only) ----------------------------------------------------------------------

  function backupStatus() {
    try { return JSON.parse(app.native.getCameraBackup()); } catch (e) { return null; }
  }
  function ago(ms) {
    const min = Math.round((Date.now() - ms) / 60000);
    if (min < 1) return 'just now';
    if (min < 60) return min + ' min ago';
    if (min < 48 * 60) return Math.round(min / 60) + ' h ago';
    return Math.round(min / 1440) + ' days ago';
  }
  function renderBackup() {
    const s = backupStatus();
    if (!s || !$('backup-card')) return;
    const o = s.options || {};
    $('backup-state-pill').textContent = s.running ? 'Backing up…' : s.enabled ? 'On' : 'Off';
    $('backup-last-pill').textContent = s.error ? s.error
      : !s.enabled ? 'Photos stay only on this phone'
      : s.waiting > 0 ? s.waiting + (o.directToDrive && app.drive.accounts.length ? ' waiting for the internet' : ' waiting for the PC')
      : s.lastRun ? 'Up to date · checked ' + ago(s.lastRun) + (s.via === 'drive' ? ' · via Google' : '') : 'Not run yet';
    $('backup-total-pill').textContent = s.total > 0 ? s.total + ' saved to the cloud' : '';
    $('backup-total-pill').style.display = s.total > 0 ? '' : 'none';
    $('backup-toggle-btn').textContent = s.enabled ? 'Turn off' : 'Turn on';
    $('backup-now-btn').style.display = s.enabled ? '' : 'none';
    $('backup-now-btn').disabled = !!s.running;
    const folders = (s.folders || []).length;
    $('backup-sub').textContent = (folders ? folders + (folders === 1 ? ' folder' : ' folders') : 'The camera folder') + ' → "Phone Backup/' + (s.device || 'this phone') + '"' +
      (o.requireCharging ? ' · only while charging' : '') + (o.network === 'any' ? ' · also on mobile data' : ' · on Wi-Fi');
  }

  function openBackupSettings() {
    const s = backupStatus() || {};
    const o = s.options || {};
    const googleOk = app.drive.available && app.drive.accounts.length > 0;
    $('backup-settings-body').innerHTML = `
      <div class="sheet-subtitle">Folders</div>
      <div id="backup-folder-list" class="backup-folder-list"><div class="sheet-note">Reading the phone's photo folders…</div></div>
      <div class="sheet-subtitle">When</div>
      <label class="settings-row"><div class="settings-row-text"><div>Only while charging</div><div class="settings-row-sub">"Back up now" always runs</div></div><input type="checkbox" id="bk-charging" ${o.requireCharging ? 'checked' : ''}></label>
      <label class="settings-row"><div class="settings-row-text"><div>Not when the battery is low</div></div><input type="checkbox" id="bk-battery" ${o.requireBatteryNotLow !== false ? 'checked' : ''}></label>
      <label class="settings-row"><div class="settings-row-text"><div>Also on mobile data</div><div class="settings-row-sub">Through Tailscale or Google Drive; uses your data plan</div></div><input type="checkbox" id="bk-mobile" ${o.network === 'any' ? 'checked' : ''}></label>
      ${app.drive.available ? `<label class="settings-row"><div class="settings-row-text"><div>Straight to Google Drive when the PC is off</div><div class="settings-row-sub">${googleOk ? 'Same "Phone Backup" folder, nothing is copied twice' : 'Sign in to Google in Settings first'}</div></div><input type="checkbox" id="bk-drive" ${o.directToDrive !== false ? 'checked' : ''}></label>` : ''}
      <div class="sheet-subtitle">Folder name on the cloud</div>
      <input type="text" class="modal-input" id="bk-device" maxlength="60" value="${app.esc(s.device || '')}" autocomplete="off">
      <div class="sheet-note">Photos go to "Phone Backup/&lt;this name&gt;/&lt;folder&gt;/&lt;month&gt;".</div>
      <button class="m3-filled-btn" data-act="backup-settings-save">Save</button>`;
    mediaFolders = null;
    app.openSheet('backup-settings');
    try { app.native.listMediaFolders(); } catch (e) { $('backup-folder-list').innerHTML = '<div class="sheet-note">Update the Cloud-SSD app to choose folders.</div>'; }
  }
  function onMediaFolders(d) {
    mediaFolders = (d && d.folders) || [];
    const box = $('backup-folder-list');
    if (!box) return;
    box.innerHTML = mediaFolders.length ? mediaFolders.sort((a, b) => b.count - a.count).map((f, i) => `
      <label class="settings-row"><div class="settings-row-text"><div>${app.esc(f.name)}</div><div class="settings-row-sub">${f.count} items · ${app.esc(String(f.path || '').replace(/^\/storage\/emulated\/0\//, ''))}</div></div>
        <input type="checkbox" data-folder="${i}" ${f.selected ? 'checked' : ''}></label>`).join('')
      : '<div class="sheet-note">No photo or video folders found (allow access to photos first).</div>';
  }
  function saveBackupSettings() {
    const opts = {
      requireCharging: $('bk-charging').checked,
      requireBatteryNotLow: $('bk-battery').checked,
      network: $('bk-mobile').checked ? 'any' : 'wifi'
    };
    if ($('bk-drive')) opts.directToDrive = $('bk-drive').checked;
    const name = $('bk-device').value.trim();
    if (name) opts.deviceName = name;
    try {
      if (mediaFolders) {
        const chosen = mediaFolders.filter((f, i) => { const cb = document.querySelector(`#backup-folder-list [data-folder="${i}"]`); return cb && cb.checked; })
          .map(f => ({ bucketId: f.bucketId, name: f.name }));
        if (!chosen.length) { app.toast('Choose at least one folder'); return; }
        app.native.setBackupFolders(JSON.stringify(chosen));
      }
      app.native.setBackupOptions(JSON.stringify(opts));
      app.toast('Backup settings saved');
    } catch (e) { app.toast('Update the Cloud-SSD app'); }
    app.closeSheet('backup-settings');
    renderBackup();
  }

  function buildBackupCard() {
    if (!app.isApp || !app.native.getCameraBackup) return; // a browser, or an older app
    const saver = document.querySelector('.saver-card');
    if (!saver) return;
    const card = document.createElement('div');
    card.className = 'pc-connection-banner-card saver-card';
    card.id = 'backup-card';
    card.style.marginTop = '12px';
    card.innerHTML = `
      <div class="pc-conn-left">
        <div class="pc-conn-icon-halo saver-halo">
          <svg viewBox="0 0 24 24"><path d="M9 2L7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8.2A3.2 3.2 0 1 0 12 15.2 3.2 3.2 0 0 0 12 8.8z"/></svg>
        </div>
        <div class="pc-conn-info">
          <div class="pc-conn-title">Camera backup</div>
          <div class="pc-conn-sub" id="backup-sub">New photos and videos go to "Phone Backup" on the cloud drive</div>
          <div class="pc-conn-pills-row">
            <span class="pc-conn-pill speed" id="backup-state-pill">—</span>
            <span class="pc-conn-pill" id="backup-last-pill">—</span>
            <span class="pc-conn-pill ping" id="backup-total-pill"></span>
          </div>
        </div>
      </div>
      <div class="pc-conn-actions">
        <button class="pc-conn-toggle-btn saver-btn" data-act="backup-now" id="backup-now-btn">Back up now</button>
        <button class="pc-conn-toggle-btn saver-btn" data-act="backup-toggle" id="backup-toggle-btn">Turn on</button>
        ${app.native.setBackupOptions ? `<button class="pc-conn-toggle-btn saver-btn icon" data-act="backup-settings" title="Backup settings">${ICON.gear}</button>` : ''}
      </div>`;
    saver.parentNode.insertBefore(card, saver.nextSibling);

    Object.assign(app.actions, {
      'backup-toggle': async () => {
        const s = backupStatus() || {};
        if (s.enabled) { app.native.setCameraBackup(false); app.toast('Camera backup is off'); renderBackup(); return; }
        const ok = await app.confirmDialog('Turn on camera backup?',
          'Every photo and video in the chosen folders (the camera folder unless you pick others with ⚙) is copied to "Phone Backup" on the cloud drive, starting now. New ones follow by themselves.', 'Turn on');
        if (!ok) return;
        if (!app.native.setCameraBackup(true)) { app.toast('Allow access to photos first (Phone tab)'); return; }
        app.toast(app.cloudSource() !== 'none' ? 'Camera backup started' : 'Camera backup is on — it starts when the PC is reachable');
        renderBackup();
      },
      'backup-now': () => {
        if (app.cloudSource() === 'none') { app.toast('PC is not connected'); return; }
        app.native.backupNow();
        app.toast('Checking the photo folders…');
        setTimeout(renderBackup, 1500);
      },
      'backup-settings': () => openBackupSettings(),
      'backup-settings-save': () => saveBackupSettings()
    });
    renderBackup();
    setInterval(() => { if (!document.hidden) renderBackup(); }, 4000);
  }

  // ---- wiring ------------------------------------------------------------------------------------------------

  function buildDom() {
    const style = document.createElement('style');
    style.textContent = `
      #fs-move-bar { position: fixed; left: 12px; right: 12px; bottom: calc(84px + env(safe-area-inset-bottom, 0px)); z-index: 210; display: none;
        align-items: center; gap: 8px; padding: 10px 12px 10px 16px; border-radius: 18px; background: var(--md-sys-color-primary-container);
        border: 1px solid rgb(var(--ink) / 0.14); box-shadow: var(--shadow); color: var(--md-sys-color-on-primary-container); font-size: 14px; }
      #fs-move-text { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      #fs-move-bar button { border: 0; border-radius: 16px; height: 32px; padding: 0 14px; font-size: 13px; font-weight: 600; }
      #fs-move-bar .ghost { background: transparent; color: var(--md-sys-color-on-primary-container); }
      #fs-move-bar .go { background: var(--md-sys-color-primary); color: var(--on-primary); }
      #fs-prompt { z-index: 1300; }
      #fs-prompt .modal-input { margin: 14px 0 18px; }
      .folder-item .item-more-btn { margin-left: auto; }
      #backup-card .pc-conn-actions { display: flex; flex-direction: column; gap: 8px; }
      #backup-card .pc-conn-toggle-btn { white-space: nowrap; }
      #backup-card .pc-conn-toggle-btn.icon { display: flex; align-items: center; justify-content: center; padding: 0; height: 32px; }
      #backup-card .pc-conn-toggle-btn.icon svg { width: 18px; height: 18px; fill: currentColor; }
      #backup-settings-body .modal-input { width: 100%; box-sizing: border-box; margin: 4px 0 6px; }
      #backup-settings-body .m3-filled-btn { width: 100%; margin-top: 14px; }`;
    document.head.appendChild(style);

    const box = document.createElement('div');
    box.innerHTML = `
      <div id="fs-sheet" class="m3-bottom-sheet-backdrop" data-act="sheet-close">
        <div class="m3-bottom-sheet">
          <div class="m3-drag-handle"></div>
          <div class="sheet-subtitle" id="fs-sheet-title"></div>
          <div class="m3-sheet-list" id="fs-sheet-list"></div>
        </div>
      </div>
      <div id="fs-prompt" class="modal-backdrop">
        <div class="modal-card">
          <div class="modal-title" id="fs-prompt-title"></div>
          <input type="text" id="fs-prompt-input" class="modal-input" autocomplete="off" maxlength="200" enterkeyhint="done">
          <div class="confirm-actions">
            <button class="m3-tonal-btn" data-act="fs-prompt-no">Cancel</button>
            <button class="m3-filled-btn" data-act="fs-prompt-yes" id="fs-prompt-yes">OK</button>
          </div>
        </div>
      </div>
      <div id="backup-settings" class="m3-bottom-sheet-backdrop" data-act="sheet-close">
        <div class="m3-bottom-sheet">
          <div class="m3-drag-handle"></div>
          <div class="sheet-subtitle">Camera backup settings</div>
          <div id="backup-settings-body"></div>
        </div>
      </div>
      <input type="file" id="fs-uploader" style="display: none;" multiple>`;
    while (box.firstElementChild) document.body.appendChild(box.firstElementChild);

    // Inside the Cloud SSD tab, so the bar is only there while that tab is.
    const bar = document.createElement('div');
    bar.id = 'fs-move-bar';
    bar.innerHTML = '<div id="fs-move-text"></div><button class="ghost" data-act="fs-move-cancel">Cancel</button><button class="go" data-act="fs-move-here">Move here</button>';
    ($('tab-ssd') || document.body).appendChild(bar);

    $('fs-prompt-input').addEventListener('keydown', (e) => { if (e.key === 'Enter') answer($('fs-prompt-input').value.trim()); });
    $('fs-uploader').addEventListener('change', (e) => {
      const files = Array.from(e.target.files || []);
      const folder = idOf(uploadFolder);
      e.target.value = '';
      if (files.length && folder) uploadInBrowser(files, folder);
    });
  }

  function init(helpers) {
    app = helpers;
    buildDom();
    buildBackupCard();
    Object.assign(app.actions, {
      // "+" in the Cloud SSD toolbar: what can be added to the folder that is open.
      'fs-menu': () => {
        if (app.cloudSource() === 'none') { app.toast('PC is not connected'); return; }
        if (!idOf(app.ssd.path)) { app.toast('Open a cloud drive first — PC disks cannot be changed from the phone'); return; }
        sheet('Add to this folder', [
          { icon: ICON.newFolder, title: 'New folder', sub: 'Create a folder here', fn: newFolder },
          { icon: app.SVG.upload, title: 'Upload files here', sub: 'Photos, videos or any file from this phone', fn: uploadHere }
        ]);
      },
      'fs-folder-more': (el) => {
        const fo = app.ssd.view.folders[+el.dataset.i];
        if (!fo) return;
        const item = { name: fo.name, path: fo.path, isFolder: true };
        sheet(fo.name, [{ icon: app.SVG.folder, title: 'Open', sub: '', fn: () => app.loadSsd(fo.path) }].concat(itemActions(item)));
      },
      'fs-row': (el) => { const a = rows[+el.dataset.i]; app.closeSheet('fs-sheet'); if (a) a.fn(); },
      'fs-prompt-yes': () => answer($('fs-prompt-input').value.trim()),
      'fs-prompt-no': () => answer(null),
      'fs-move-here': moveHere,
      'fs-move-cancel': () => { moving = null; renderMoveBar(); }
    });

    // A file sent with "Upload here" has arrived: show it.
    const onNative = window.onNative;
    window.onNative = function (event, data) {
      if (onNative) onNative(event, data);
      if (event === 'transfer' && data && data.type === 'upload' && data.state === 'done' && uploadFolder && uploadFolder === app.ssd.path) refresh();
    };
  }

  window.CloudSsdFs = {
    init,
    idOf: (path) => app ? idOf(path) : null,
    /** True when the folder is on a connected cloud drive, so its items get a "more" button. */
    canManage: (path) => !!app && !!idOf(path) && app.cloudSource() !== 'none',
    fileActions: (f) => itemActions({ name: f.name, path: f.path, isFolder: false, readOnly: f.readOnly || f.exported }),
    startMove,
    bulkDelete,
    onMediaFolders
  };
})();
