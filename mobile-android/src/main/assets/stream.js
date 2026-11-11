// Cloud-SSD mobile — "Play on phone": a card at the top of the Gaming tab. Play opens the app's full-screen game screen
// (StreamActivity), which starts the game on the PC and streams picture and sound here. The Artemis link is the old way
// to play, kept as a backup until our own stream plays as well. Only in the Android app (the page also runs in a browser).
(function () {
  'use strict';

  const native = window.AndroidBridge || null;
  if (!native || typeof native.startGameStream !== 'function') return;

  const css = `
    .play-card { margin: 12px 16px 4px; padding: 16px; border-radius: 18px; background: var(--card, #1b1f2a); border: 1px solid var(--border, rgba(255,255,255,.08)); }
    .play-card .play-top { display: flex; align-items: center; gap: 12px; }
    .play-card .play-icon { width: 44px; height: 44px; border-radius: 12px; display: grid; place-items: center; background: rgba(255,140,0,.15); flex: none; }
    .play-card .play-icon svg { width: 26px; height: 26px; fill: #ff8c00; }
    .play-card .play-name { font-weight: 600; font-size: 16px; }
    .play-card .play-sub { font-size: 12px; opacity: .7; margin-top: 2px; }
    .play-card .play-btn { width: 100%; margin-top: 14px; padding: 13px; border: 0; border-radius: 12px; font-size: 16px; font-weight: 600; color: #fff; background: #ff8c00; }
    .play-card .play-btn:disabled { opacity: .5; }
    .play-card .play-backup { display: block; margin: 10px auto 0; background: none; border: 0; color: inherit; opacity: .7; font-size: 13px; text-decoration: underline; }
  `;

  function server() {
    try {
      const p = JSON.parse(native.getPairing() || '{}');
      return p.server && p.token ? p : null;
    } catch (e) { return null; }
  }

  async function refresh(card) {
    const sub = card.querySelector('.play-sub');
    const btn = card.querySelector('.play-btn');
    const p = server();
    if (!p) { sub.textContent = 'Pehle is phone ko PC se jodo (Home tab).'; btn.disabled = true; return; }
    try {
      const r = await fetch(`${p.server}/api/game/status?token=${encodeURIComponent(p.token)}`, { cache: 'no-store' });
      const j = await r.json();
      if (!r.ok) { sub.textContent = j.error || 'PC par "Play on phone" band hai.'; btn.disabled = true; return; }
      card.querySelector('.play-name').textContent = j.game || 'Game';
      sub.textContent = j.playing ? 'Abhi koi aur phone khel raha hai.'
        : j.emulatorRunning ? 'PC par khula hai · ghar ke Wi-Fi par khelo'
        : 'Play dabao: PC par khud khul jayega';
      btn.disabled = !!j.playing;
    } catch (e) {
      sub.textContent = 'PC nahi mila. Ghar ke Wi-Fi par Cloud-SSD PC par khula ho.';
      btn.disabled = true;
    }
  }

  function build() {
    const tab = document.getElementById('tab-gaming');
    if (!tab || document.getElementById('play-card')) return;
    const style = document.createElement('style');
    style.textContent = css;
    document.head.appendChild(style);
    const card = document.createElement('div');
    card.id = 'play-card';
    card.className = 'play-card';
    card.innerHTML = `
      <div class="play-top">
        <div class="play-icon"><svg viewBox="0 0 24 24"><path d="M21 6H3c-1.1 0-2 .9-2 2v8c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-10 7H8v3H6v-3H3v-2h3V8h2v3h3v2zm4.5 2c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zm4-3c-.83 0-1.5-.67-1.5-1.5S18.67 9 19.5 9s1.5.67 1.5 1.5-.67 1.5-1.5 1.5z"/></svg></div>
        <div><div class="play-name">Free Fire</div><div class="play-sub">PC se pooch raha hai…</div></div>
      </div>
      <button class="play-btn" type="button">▶ Play on phone</button>
      <button class="play-backup" type="button">Backup: Artemis se kholo</button>`;
    tab.insertBefore(card, tab.firstChild);
    card.querySelector('.play-btn').addEventListener('click', () => native.startGameStream());
    card.querySelector('.play-backup').addEventListener('click', () => {
      if (!native.openArtemis || !native.openArtemis()) alert('Artemis is phone par installed nahi hai.');
    });
    refresh(card);
    // Refresh whenever the Gaming tab is opened.
    document.addEventListener('click', (e) => {
      const t = e.target.closest && e.target.closest('[data-tab="gaming"]');
      if (t) refresh(card);
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', build);
  else build();
})();
