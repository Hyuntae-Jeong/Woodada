// Woodada 홈페이지: 언어 전환, 가짜 작업표시줄 위 고양이, 냥뽑기 스킨 미리보기.
// 고양이 그림은 앱과 같은 RunCat 5프레임이고, 눈 구멍 찾기·눈 색 규칙·스킨 값은 앱 코드(Sprites/*.cs)를 따랐다.
(() => {
  'use strict';

  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

  /* ───────── 언어 ───────── */
  const TITLES = {
    en: 'Woodada — a cat that gets the zoomies on your taskbar',
    ko: 'Woodada — 작업표시줄 위를 우다다 달리는 고양이',
  };
  let lang = document.documentElement.dataset.lang === 'ko' ? 'ko' : 'en';

  function applyLang(l) {
    lang = l;
    const root = document.documentElement;
    root.lang = l;
    root.dataset.lang = l;
    document.title = TITLES[l];
    try { localStorage.setItem('woodada-lang', l); } catch (e) { /* 저장 못 해도 화면은 바뀐다 */ }
    $$('[data-set-lang]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.setLang === l)));
    $$('[data-alt-' + l + ']').forEach(el => { el.alt = el.dataset['alt' + (l === 'ko' ? 'Ko' : 'En')]; });
    $$('[data-src-' + l + ']').forEach(el => { el.src = el.dataset['src' + (l === 'ko' ? 'Ko' : 'En')]; });
    $$('.swatch').forEach(b => b.setAttribute('aria-label', b.dataset[l]));
    tickClock();
  }
  $$('[data-set-lang]').forEach(b => b.addEventListener('click', () => applyLang(b.dataset.setLang)));

  /* ───────── 작업표시줄 시계 ───────── */
  function tickClock() {
    const now = new Date();
    const t = $('#clock-t'), d = $('#clock-d');
    if (!t) return;
    t.textContent = now.toLocaleTimeString(lang === 'ko' ? 'ko-KR' : 'en-US', { hour: 'numeric', minute: '2-digit' });
    const p = n => String(n).padStart(2, '0');
    d.textContent = `${now.getFullYear()}-${p(now.getMonth() + 1)}-${p(now.getDate())}`;
  }
  setInterval(tickClock, 15000);

  /* ───────── 최신 버전 표시 (실패하면 조용히 숨김) ───────── */
  fetch('https://api.github.com/repos/Hyuntae-Jeong/Woodada/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
    .then(r => (r.ok ? r.json() : null))
    .then(j => {
      if (!j || !j.tag_name) return;
      $$('.ver').forEach(a => { a.textContent = j.tag_name; });
      $$('.ver-wrap').forEach(w => { w.hidden = false; });
    })
    .catch(() => {});

  /* ───────── 색 ───────── */
  // 앱의 기본 색 10가지 (Rewards/RewardCatalog.cs)
  const COLORS = [
    ['#1E1E22', 'Black', '까망'], ['#ECE7DF', 'White', '하양'], ['#F4A261', 'Ginger', '치즈'],
    ['#6D8BA8', 'Tabby', '고등어'], ['#9A9AA0', 'Gray', '회색'], ['#C9825B', 'Calico', '삼색'],
    ['#7FB8A4', 'Mint', '민트'], ['#E6C55A', 'Lemon', '레몬'], ['#AE8FC7', 'Lavender', '라벤더'],
    ['#D9626B', 'Cherry', '체리'],
  ];
  const hexRgb = h => { const n = parseInt(h.slice(1), 16); return [n >> 16 & 255, n >> 8 & 255, n & 255]; };
  const rgbHex = c => '#' + c.map(v => Math.round(clamp(v, 0, 255)).toString(16).padStart(2, '0')).join('');
  const mix = (a, b, t) => { const A = hexRgb(a), B = hexRgb(b); return rgbHex(A.map((v, i) => v + (B[i] - v) * t)); };
  const lum = h => { const [r, g, b] = hexRgb(h); return (0.299 * r + 0.587 * g + 0.114 * b) / 255; };
  // 앱의 FrameSprite와 같은 규칙: 밝은 몸이면 어두운 눈, 아니면 흰 눈
  const eyeFor = c => (lum(c) > 0.7 ? '#2D2A30' : '#FFFFFF');

  /* ───────── 고양이 프레임 준비 ───────── */
  const mk = (w, h) => { const c = document.createElement('canvas'); c.width = Math.max(1, Math.ceil(w)); c.height = Math.max(1, Math.ceil(h)); return c; };
  const loadImg = src => new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = src; });

  // 프레임마다 {sil: 눈 구멍까지 채운 몸, eye: 눈 구멍} 흰색 마스크를 만든다. 다섯 장 모두 같은 틀로 잘라 발 위치가 흔들리지 않게 한다.
  async function loadMasks() {
    const imgs = await Promise.all([0, 1, 2, 3, 4].map(i => loadImg(`assets/cat/cat_running_${i}.png`)));
    const W = imgs[0].naturalWidth, H = imgs[0].naturalHeight;
    const alphas = imgs.map(img => {
      const c = mk(W, H), x = c.getContext('2d', { willReadFrequently: true });
      x.drawImage(img, 0, 0);
      const d = x.getImageData(0, 0, W, H).data, a = new Uint8Array(W * H);
      for (let i = 0; i < a.length; i++) a[i] = d[i * 4 + 3];
      return a;
    });
    let x0 = W, y0 = H, x1 = -1, y1 = -1;
    for (const a of alphas) for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      if (a[y * W + x] > 8) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
    }
    // 1px 여백을 둬서 바깥 채우기가 몸 둘레를 돌 수 있게 한다
    const cw = x1 - x0 + 3, ch = y1 - y0 + 3;
    return alphas.map(a => {
      const al = new Uint8Array(cw * ch);
      for (let y = 0; y <= y1 - y0; y++) for (let x = 0; x <= x1 - x0; x++) al[(y + 1) * cw + x + 1] = a[(y + y0) * W + x + x0];
      // 테두리에서 투명한 곳을 따라 채운 곳 = 바깥. 바깥에 닿지 않는 투명한 곳 = 눈 구멍.
      const out = new Uint8Array(cw * ch), st = [0];
      out[0] = 1;
      while (st.length) {
        const i = st.pop(), x = i % cw, y = (i / cw) | 0;
        const nb = [x > 0 ? i - 1 : -1, x < cw - 1 ? i + 1 : -1, y > 0 ? i - cw : -1, y < ch - 1 ? i + cw : -1];
        for (const j of nb) if (j >= 0 && !out[j] && al[j] < 128) { out[j] = 1; st.push(j); }
      }
      const hole = new Uint8Array(cw * ch);
      for (let i = 0; i < hole.length; i++) hole[i] = !out[i] && al[i] < 128 ? 1 : 0;
      const grown = hole.slice();
      for (let y = 0; y < ch; y++) for (let x = 0; x < cw; x++) {
        if (!hole[y * cw + x]) continue;
        for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
          const nx = x + dx, ny = y + dy;
          if (nx >= 0 && ny >= 0 && nx < cw && ny < ch) grown[ny * cw + nx] = 1;
        }
      }
      const sil = mk(cw, ch), eye = mk(cw, ch);
      const sx = sil.getContext('2d'), ex = eye.getContext('2d');
      const sd = sx.createImageData(cw, ch), ed = ex.createImageData(cw, ch);
      for (let i = 0; i < cw * ch; i++) {
        const k = i * 4;
        sd.data[k] = sd.data[k + 1] = sd.data[k + 2] = 255;
        sd.data[k + 3] = hole[i] ? 255 : al[i];
        ed.data[k] = ed.data[k + 1] = ed.data[k + 2] = 255;
        ed.data[k + 3] = grown[i] ? (hole[i] ? 255 : 255 - al[i]) : 0;
      }
      sx.putImageData(sd, 0, 0);
      ex.putImageData(ed, 0, 0);
      return { sil, eye, w: cw, h: ch };
    });
  }

  // 흰 마스크를 h(장치 픽셀) 높이로 줄여 색을 입힌다
  function paint(mask, color, h) {
    const s = h / mask.height, w = Math.max(1, Math.round(mask.width * s));
    const c = mk(w, h), x = c.getContext('2d');
    x.imageSmoothingQuality = 'high';
    x.drawImage(mask, 0, 0, w, h);
    x.globalCompositeOperation = 'source-in';
    x.fillStyle = color;
    x.fillRect(0, 0, w, h);
    x.globalCompositeOperation = 'source-over'; // 이 캔버스에 덧그리는 쪽(눈 등)이 몸을 지우지 않게 되돌린다
    return c;
  }

  const spriteCache = new Map();
  // 몸색 + 눈색까지 입힌 프레임 한 장
  function catFrame(masks, i, color, h, eyeColor) {
    const key = `${i}|${color}|${h}|${eyeColor || ''}`;
    let c = spriteCache.get(key);
    if (c) return c;
    const m = masks[i];
    c = paint(m.sil, color, h);
    const x = c.getContext('2d');
    x.drawImage(paint(m.eye, eyeColor || eyeFor(color), h), 0, 0);
    spriteCache.set(key, c);
    return c;
  }
  // 눈 없이 실루엣만 (잔상·글리치 갈라짐용)
  function silFrame(masks, i, color, h) {
    const key = `s${i}|${color}|${h}`;
    let c = spriteCache.get(key);
    if (!c) { c = paint(masks[i].sil, color, h); spriteCache.set(key, c); }
    return c;
  }

  // 앱과 비슷한 속도: 사용률 6% → 68px/s, 75% → 252px/s
  const speedFor = p => 50 + 270 * p;
  // 빨리 달릴수록 다리도 빨리 움직인다
  const fpsFor = v => 5 + v / 14;

  class Runner {
    constructor(x, dir) { this.x = x; this.dir = dir; this.frame = 0; }
    step(dt, v, minX, maxX) {
      this.x += this.dir * v * dt;
      if (this.x > maxX) { this.x = maxX; this.dir = -1; }
      if (this.x < minX) { this.x = minX; this.dir = 1; }
      this.frame = (this.frame + dt * fpsFor(v)) % 5;
    }
  }

  // 왼쪽으로 달릴 땐 좌우를 뒤집어 그린다. x, y, w, h는 CSS 픽셀.
  function drawFlipped(ctx, img, x, y, w, h, dir) {
    if (dir >= 0) { ctx.drawImage(img, x, y, w, h); return; }
    ctx.save();
    ctx.translate(x + w, y);
    ctx.scale(-1, 1);
    ctx.drawImage(img, 0, 0, w, h);
    ctx.restore();
  }

  function fitCanvas(canvas) {
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const r = canvas.getBoundingClientRect();
    const w = Math.round(r.width * dpr), h = Math.round(r.height * dpr);
    if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
    const ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    return { ctx, w: r.width, h: r.height, dpr };
  }

  // 화면에 보일 때만 그린다
  function onVisible(el, cb) {
    if (!('IntersectionObserver' in window)) { cb(true); return; }
    new IntersectionObserver(es => es.forEach(e => cb(e.isIntersecting)), { rootMargin: '80px' }).observe(el);
  }

  /* ───────── 사용자 활동 → 가짜 CPU 사용률 ───────── */
  let busy = 0;
  let last = null;
  const bump = v => { busy = clamp(busy + v, 0, 1); };
  addEventListener('pointermove', e => {
    if (last) bump(Math.hypot(e.clientX - last.x, e.clientY - last.y) * 0.0011);
    last = { x: e.clientX, y: e.clientY };
  }, { passive: true });
  let lastScroll = scrollY;
  addEventListener('scroll', () => { bump(Math.abs(scrollY - lastScroll) * 0.0009); lastScroll = scrollY; }, { passive: true });
  addEventListener('keydown', () => bump(0.05));
  addEventListener('touchmove', () => bump(0.02), { passive: true });

  /* ───────── 메인 장면 ───────── */
  function heroScene(masks) {
    const scene = $('#hero-scene');
    const canvas = $('.scene-canvas', scene);
    const taskbar = $('.taskbar', scene);
    const cpuRunner = new Runner(40, 1);
    const memRunner = new Runner(260, -1);
    let cpuColor = COLORS[0][0];
    let cpu = 0.05, mem = 0.58, t = 0;
    let visible = true, lastT = 0, uiT = 0;

    // 색 고르기
    const box = $('#swatches');
    COLORS.forEach(([hex, en, ko], i) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'swatch';
      b.style.background = hex;
      b.dataset.en = en; b.dataset.ko = ko;
      b.setAttribute('role', 'radio');
      b.setAttribute('aria-label', lang === 'ko' ? ko : en);
      b.setAttribute('aria-checked', String(i === 0));
      b.addEventListener('click', () => {
        cpuColor = hex;
        $$('.swatch', box).forEach(s => s.setAttribute('aria-checked', String(s === b)));
      });
      box.appendChild(b);
    });

    const cpuOut = $('#cpu-out'), memOut = $('#mem-out'), cpuBar = $('#cpu-bar'), memBar = $('#mem-bar'), tbCpu = $('#tb-cpu');

    function frame(now) {
      const dt = lastT ? Math.min(0.05, (now - lastT) / 1000) : 0;
      lastT = now;
      t += dt;
      busy *= Math.exp(-dt * 0.9);
      const target = 0.04 + 0.92 * busy + 0.015 * Math.sin(t * 2.3);
      cpu += (target - cpu) * Math.min(1, dt * 5);
      mem = 0.58 + 0.05 * Math.sin(t / 6) + 0.01 * Math.sin(t * 1.7);

      const { ctx, w, h, dpr } = fitCanvas(canvas);
      ctx.clearRect(0, 0, w, h);
      const base = h - taskbar.offsetHeight; // 고양이 발이 작업표시줄 윗변에 닿는다
      const catH = w < 600 ? 30 : 36;
      const devH = Math.round(catH * dpr);
      const aspect = masks[0].w / masks[0].h;
      const catW = catH * aspect;

      if (!reduceMotion) {
        cpuRunner.step(dt, speedFor(cpu), 4, w - catW - 4);
        memRunner.step(dt, speedFor(mem) * 0.9, 4, w - catW - 4);
      }
      const memImg = catFrame(masks, Math.floor(memRunner.frame), '#ECE7DF', devH);
      drawFlipped(ctx, memImg, memRunner.x, base - catH + 2, catW, catH, memRunner.dir);
      const cpuImg = catFrame(masks, Math.floor(cpuRunner.frame), cpuColor, devH);
      drawFlipped(ctx, cpuImg, cpuRunner.x, base - catH + 2, catW, catH, cpuRunner.dir);

      if (now - uiT > 100) {
        uiT = now;
        const c = Math.round(cpu * 100), m = Math.round(mem * 100);
        cpuOut.textContent = c + '%'; memOut.textContent = m + '%'; tbCpu.textContent = c + '%';
        cpuBar.style.width = c + '%'; memBar.style.width = m + '%';
      }
      if (visible && !document.hidden) requestAnimationFrame(frame);
      else lastT = 0;
    }

    const start = () => { lastT = 0; requestAnimationFrame(frame); };
    onVisible(scene, v => { const was = visible; visible = v; if (v && !was) start(); });
    document.addEventListener('visibilitychange', () => { if (!document.hidden && visible) start(); });
    start();
  }

  /* ───────── 냥뽑기 스킨 ───────── */

  // 작은 장면 배경: 어두운 바탕화면 + 작업표시줄
  function drawMiniScene(ctx, w, h, barH) {
    const g = ctx.createLinearGradient(0, 0, w, h);
    g.addColorStop(0, '#26294a'); g.addColorStop(1, '#1a1c2b');
    ctx.fillStyle = g; ctx.fillRect(0, 0, w, h);
    ctx.fillStyle = 'rgba(32,32,40,.96)'; ctx.fillRect(0, h - barH, w, barH);
    ctx.fillStyle = 'rgba(255,255,255,.07)'; ctx.fillRect(0, h - barH, w, 1);
    // 작업표시줄 아이콘 자리
    const cx = w / 2;
    ctx.fillStyle = 'rgba(255,255,255,.14)';
    for (let i = -2; i <= 2; i++) roundRect(ctx, cx + i * 22 - 7, h - barH / 2 - 7, 14, 14, 3);
  }
  function roundRect(ctx, x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y); ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r); ctx.closePath(); ctx.fill();
  }

  // 유령 (Sprites/GhostSkin.cs): 반투명 몸 45%, 외곽선 50%, 바깥 발광 55%, 키의 25% 떠서 ±10% 흔들림, 1.7초 주기
  const GHOST = '#DDEBFF';
  function ghostFrames(masks, devH) {
    const line = mix(GHOST, '#FFFFFF', 0.5);
    return masks.map(m => {
      const body = paint(m.sil, GHOST, devH);
      const pad = Math.ceil(devH * 0.35);
      const W = body.width + pad * 2, H = body.height + pad * 2;
      const c = mk(W, H), x = c.getContext('2d');
      // 발광: 몸 바깥에만
      x.save();
      x.shadowColor = 'rgba(221,235,255,.95)';
      x.shadowBlur = devH * 0.28;
      x.globalAlpha = 0.55;
      x.drawImage(body, pad, pad); x.drawImage(body, pad, pad);
      x.restore();
      x.globalCompositeOperation = 'destination-out';
      x.drawImage(body, pad, pad);
      x.globalCompositeOperation = 'source-over';
      // 외곽선: 사방으로 민 모양에서 몸을 뺀 테두리
      const ring = mk(W, H), rx = ring.getContext('2d'), lineImg = paint(m.sil, line, devH);
      const o = Math.max(1, Math.round(devH / 36));
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1], [1, 1], [-1, -1], [1, -1], [-1, 1]]) rx.drawImage(lineImg, pad + dx * o, pad + dy * o);
      rx.globalCompositeOperation = 'destination-out';
      rx.drawImage(body, pad, pad);
      x.globalAlpha = 0.5; x.drawImage(ring, 0, 0);
      x.globalAlpha = 0.45; x.drawImage(body, pad, pad);
      x.globalAlpha = 1; x.drawImage(paint(m.eye, eyeFor(GHOST), devH), pad, pad);
      return { c, pad };
    });
  }

  // 플래시 (Sprites/FlashSkin.cs, Rendering/SkinPainter.cs AddFlashTrail): 값과 공식을 앱과 똑같이 맞췄다
  const FLASH = {
    afterimages: 8, trailLength: 0.90, firstOpacity: 0.60, minStrength: 0.35, fullUsage: 40,
    heatStart: 50, heatFull: 60, heatSteps: 5, heatColor: '#D9626B',
    wispCount: 4, wispLength: 1.30, wispSegments: 14,
  };
  const FIRE = ['#FF4D2E', '#FF8A3D', '#FFC24A', '#FFE9A0'];
  const flashStrength = u => FLASH.minStrength + (1 - FLASH.minStrength) * clamp(u / FLASH.fullUsage, 0, 1);
  const speedShare = u => 0.35 + 0.65 * clamp(u / 100, 0, 1);
  // 꼬리 쪽 위치 t(0 = 몸, 1 = 끝)의 색. 몸에 붙은 쪽은 몸색과 섞는다.
  function trailColor(t, body) {
    t = clamp(t, 0, 1);
    const x = t * (FIRE.length - 1), i = Math.min(FIRE.length - 2, Math.floor(x));
    const fire = mix(FIRE[i], FIRE[i + 1], x - i);
    return t < 0.2 ? mix(body, fire, 0.5 + t * 2.5) : fire;
  }
  // 흰 몸 기준. 붉은 몸이면 앱은 주황으로 달아오르지만 여기선 흰 고양이만 쓴다.
  function flashBody(chosen, u) {
    let heat = clamp((u - FLASH.heatStart) / (FLASH.heatFull - FLASH.heatStart), 0, 1);
    heat = Math.round(heat * FLASH.heatSteps) / FLASH.heatSteps;
    return heat <= 0 ? chosen : mix(chosen, FLASH.heatColor, heat);
  }
  // 불꽃 줄기: 등 한가운데쯤에서 뒤로 뻗는 가는 줄. 끝으로 갈수록 가늘고 옅어지며 위로 살짝 들리고 일렁인다.
  function drawWisps(ctx, bx, by, w, h, dir, u, seconds, phase, body, dpr) {
    const k = flashStrength(u);
    const back = bx + (dir > 0 ? w * 0.35 : w * 0.65);
    const maxWidth = Math.max(1.5, h * dpr * 0.07) / dpr;
    const bottom = by + h;
    ctx.lineCap = 'round';
    for (let j = 0; j < FLASH.wispCount; j++) {
      const rnd = 0.5 + 0.5 * Math.sin(j * 12.9898 + phase);
      const len = w * FLASH.wispLength * k * speedShare(u) * (0.55 + 0.45 * rnd);
      const yFrac = 0.12 + 0.6 * j / (FLASH.wispCount - 1);
      const y0 = bottom - h * (1 - yFrac);
      const at = q => [back - dir * len * q, y0 + Math.sin(q * 4.5 - seconds * 14 + j * 1.7 + phase) * h * 0.07 * q - h * 0.08 * q];
      for (let s = 0; s < FLASH.wispSegments; s++) {
        const a = s / FLASH.wispSegments, b = (s + 1) / FLASH.wispSegments;
        const [x1, y1] = at(a), [x2, y2] = at(b);
        ctx.globalAlpha = 0.75 * k * (1 - a);
        ctx.strokeStyle = trailColor(a, body);
        ctx.lineWidth = Math.max(0.6 / dpr, (1 - a) * maxWidth);
        ctx.beginPath(); ctx.moveTo(x1, y1); ctx.lineTo(x2, y2); ctx.stroke();
      }
    }
    ctx.globalAlpha = 1;
  }

  // 글리치 (Sprites/GlitchSkin.cs): 평소엔 빨강·청록이 옅게 번지고, 가끔 갈라지며 가로 줄이 밀리고 노이즈가 튄다
  const GL_A = '#FF2E63', GL_B = '#22E4FF';

  function skinScene(canvas, masks) {
    const kind = canvas.dataset.skin;
    const runner = new Runner(30, 1);
    const trail = []; // 플래시 잔상용 지나온 자리
    let t = 0, lastT = 0, visible = false;
    let ghostCache = null, ghostH = 0;
    let glitch = { until: 0, next: 0.8, shape: null };

    function glitchShape(w, h) {
      const bands = [];
      for (let i = 0; i < 3; i++) bands.push({ top: Math.random() * 0.85, height: 0.06 + Math.random() * 0.12, shift: (Math.random() * 2 - 1) * 0.18 * w });
      const noise = [];
      for (let i = 0; i < 4; i++) noise.push({ x: Math.random() * 1.2 - 0.1, y: Math.random(), w: 0.08 + Math.random() * 0.22, h: 0.03 + Math.random() * 0.06, c: [GL_A, GL_B, '#FFFFFF'][i % 3] });
      return { split: 2.2 * (0.6 + Math.random() * 0.8), side: Math.random() < 0.5 ? 1 : -1, bands, noise };
    }

    function frame(now) {
      const dt = lastT ? Math.min(0.05, (now - lastT) / 1000) : 0;
      lastT = now;
      t += dt;
      const { ctx, w, h, dpr } = fitCanvas(canvas);
      const barH = 26, base = h - barH;
      const catH = 40, devH = Math.round(catH * dpr);
      const catW = catH * masks[0].w / masks[0].h;
      drawMiniScene(ctx, w, h, barH);

      if (kind === 'ghost') {
        if (!ghostCache || ghostH !== devH) { ghostCache = ghostFrames(masks, devH); ghostH = devH; }
        if (!reduceMotion) runner.step(dt, speedFor(0.25), 8, w - catW - 8);
        const g = ghostCache[Math.floor(runner.frame)];
        const lift = catH * (0.25 + 0.10 * Math.sin(t * 2 * Math.PI / 1.7));
        const pad = g.pad / dpr;
        drawFlipped(ctx, g.c, runner.x - pad, base - catH + 2 - lift - pad, g.c.width / dpr, g.c.height / dpr, runner.dir);
      }

      if (kind === 'flash') {
        // 속도가 천천히 오르내려서 꼬리 길이와 달아오름이 바뀌는 걸 보여 준다
        const p = 0.3 + 0.65 * (0.5 + 0.5 * Math.sin(t * 0.7 - 1.2));
        const v = speedFor(p) * 1.15;
        if (!reduceMotion) runner.step(dt, v, 8, w - catW - 8);
        trail.unshift({ x: runner.x, f: Math.floor(runner.frame), dir: runner.dir });
        if (trail.length > 120) trail.length = 120;
        // 지나온 길을 따라 d만큼 뒤에 있던 자리 (앱의 RunnerInstance.Behind)
        const behind = d => {
          let acc = 0;
          for (let i = 1; i < trail.length; i++) {
            acc += Math.abs(trail[i].x - trail[i - 1].x);
            if (acc >= d) return trail[i];
          }
          return null;
        };
        const u = p * 100, y = base - catH + 2;
        // 불꽃 잔상과 대비가 나도록 하얀 고양이로 보여 준다
        const body = flashBody('#ECE7DF', u);
        // 앱과 같은 순서: 불꽃 줄기 → 잔상(꼬리 끝부터 몸 쪽으로) → 몸
        drawWisps(ctx, runner.x, y, catW, catH, runner.dir, u, t, 0.7, body, dpr);
        const tail = catW * FLASH.trailLength * flashStrength(u) * speedShare(u);
        for (let i = FLASH.afterimages; i >= 1; i--) {
          const q = i / FLASH.afterimages;
          const s = behind(tail * q);
          if (!s) continue;
          ctx.globalAlpha = FLASH.firstOpacity * flashStrength(u) * Math.pow(1 - (i - 1) / FLASH.afterimages, 1.3);
          drawFlipped(ctx, silFrame(masks, s.f, trailColor(q, body), devH), s.x, y, catW, catH, s.dir);
        }
        ctx.globalAlpha = 1;
        drawFlipped(ctx, catFrame(masks, Math.floor(runner.frame), body, devH), runner.x, y, catW, catH, runner.dir);
      }

      if (kind === 'glitch') {
        const p = 0.45;
        if (!reduceMotion) runner.step(dt, speedFor(p), 8, w - catW - 8);
        // 1초에 1.6번쯤 터진다 (앱은 CPU 사용률에 따라 0.4~3번)
        if (t >= glitch.next && t > glitch.until) {
          glitch.until = t + 0.22 * (0.6 + Math.random() * 0.8);
          glitch.next = glitch.until + (-Math.log(1 - Math.random()) / 1.6);
          glitch.shape = glitchShape(catW, catH);
        }
        const bursting = t < glitch.until;
        if (bursting && Math.random() < 0.6) glitch.shape = glitchShape(catW, catH);
        const f = Math.floor(runner.frame), y = base - catH + 2;

        // 한 장에 합쳐 그린 뒤 가로 줄을 밀어낸다
        const pad = 20;
        const off = mk((catW + pad * 2) * dpr, (catH + pad * 2) * dpr), ox = off.getContext('2d');
        ox.setTransform(dpr, 0, 0, dpr, 0, 0);
        const sp = bursting ? glitch.shape.split * glitch.shape.side : 1;
        ox.globalAlpha = bursting ? 0.85 : 0.35;
        drawFlipped(ox, silFrame(masks, f, GL_A, devH), pad - sp, pad, catW, catH, runner.dir);
        drawFlipped(ox, silFrame(masks, f, GL_B, devH), pad + sp, pad, catW, catH, runner.dir);
        ox.globalAlpha = 1;
        drawFlipped(ox, catFrame(masks, f, '#AE8FC7', devH), pad, pad, catW, catH, runner.dir);

        if (bursting) {
          const out = mk(off.width, off.height), qx = out.getContext('2d');
          qx.drawImage(off, 0, 0);
          for (const b of glitch.shape.bands) {
            const sy = Math.round((pad + b.top * catH) * dpr), sh = Math.max(1, Math.round(b.height * catH * dpr));
            qx.clearRect(0, sy, out.width, sh);
            qx.drawImage(off, 0, sy, off.width, sh, Math.round(b.shift * dpr), sy, off.width, sh);
          }
          for (const n of glitch.shape.noise) {
            qx.fillStyle = n.c; qx.globalAlpha = 0.8;
            qx.fillRect((pad + n.x * catW) * dpr, (pad + n.y * catH) * dpr, n.w * catW * dpr, n.h * catH * dpr);
          }
          ctx.drawImage(out, runner.x - pad, y - pad, catW + pad * 2, catH + pad * 2);
        } else {
          ctx.drawImage(off, runner.x - pad, y - pad, catW + pad * 2, catH + pad * 2);
        }
      }

      if (visible && !document.hidden) requestAnimationFrame(frame);
      else lastT = 0;
    }

    const start = () => { lastT = 0; requestAnimationFrame(frame); };
    onVisible(canvas, v => { const was = visible; visible = v; if (v && !was) start(); });
    document.addEventListener('visibilitychange', () => { if (!document.hidden && visible) start(); });
  }

  /* ───────── 다운로드 클릭 통계 ───────── */
  // 다운로드 링크를 누르면 GA에 app_download 이벤트를 보낸다. GA가 꺼진 곳(유럽·로컬)에서는 아무것도 안 한다.
  // platform: windows / mac_arm64 / mac_x64, link_location: 누른 위치(hero, install, cta …)
  function platformOf(href) {
    if (/\.exe$/i.test(href)) return 'windows';
    if (/arm64\.dmg$/i.test(href)) return 'mac_arm64';
    if (/x64\.dmg$/i.test(href)) return 'mac_x64';
    return 'other';
  }
  document.addEventListener('click', e => {
    const a = e.target.closest('a[href*="/releases/latest/download/"]');
    if (!a || typeof window.gtag !== 'function') return;
    const sec = a.closest('section');
    window.gtag('event', 'app_download', {
      platform: platformOf(a.getAttribute('href')),
      link_location: sec ? (sec.id || sec.classList[0] || 'page') : 'page',
    });
  });

  /* ───────── 시작 ───────── */
  applyLang(lang);
  loadMasks().then(masks => {
    heroScene(masks);
    $$('.skin-canvas').forEach(c => skinScene(c, masks));
  }).catch(err => console.error('고양이 그림을 불러오지 못했어요', err));
})();
