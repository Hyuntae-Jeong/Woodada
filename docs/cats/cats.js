// Woodada 고양이 도감·프로필 페이지: 언어 전환, 네온 무대 위를 달리는 스킨, CPU 막대에 따라 움직이는 능력치.
// 페이지 HTML은 app/tools/cat_pages.py가 만든다. 고양이 그림은 앱과 같은 RunCat 5프레임이고,
// 스킨 값과 공식은 앱 코드(Sprites/Skins/*.cs)와 docs/site.js를 따랐다. 앱의 스킨 값을 바꾸면 여기도 맞춘다.
(() => {
  'use strict';

  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const ROOT = document.body.dataset.root || '';

  /* ───────── 언어 ───────── */
  const html = document.documentElement;
  function applyLang(l) {
    html.lang = l;
    html.dataset.lang = l;
    document.title = html.getAttribute('data-title-' + l) || document.title;
    try { localStorage.setItem('woodada-lang', l); } catch (e) { /* 저장 못 해도 화면은 바뀐다 */ }
    $$('[data-set-lang]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.setLang === l)));
  }
  $$('[data-set-lang]').forEach(b => b.addEventListener('click', () => applyLang(b.dataset.setLang)));
  applyLang(html.dataset.lang === 'ko' ? 'ko' : 'en');

  /* ───────── 색 ───────── */
  const hexRgb = h => { const n = parseInt(h.slice(1), 16); return [n >> 16 & 255, n >> 8 & 255, n & 255]; };
  const rgbHex = c => '#' + c.map(v => Math.round(clamp(v, 0, 255)).toString(16).padStart(2, '0')).join('');
  const mix = (a, b, t) => { const A = hexRgb(a), B = hexRgb(b); return rgbHex(A.map((v, i) => v + (B[i] - v) * t)); };
  const lum = h => { const [r, g, b] = hexRgb(h); return (0.299 * r + 0.587 * g + 0.114 * b) / 255; };
  // 앱의 FrameSprite와 같은 규칙: 밝은 몸이면 어두운 눈, 아니면 흰 눈
  const eyeFor = c => (lum(c) > 0.7 ? '#2D2A30' : '#FFFFFF');
  const fract = x => x - Math.floor(x);
  const hash = i => fract(Math.sin(i * 127.1 + 311.7) * 43758.5453);
  const rand = (a, b) => a + Math.random() * (b - a);

  /* ───────── 고양이 프레임 준비 ───────── */
  const mk = (w, h) => { const c = document.createElement('canvas'); c.width = Math.max(1, Math.ceil(w)); c.height = Math.max(1, Math.ceil(h)); return c; };
  const loadImg = src => new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = src; });

  // 프레임마다 {sil: 눈 구멍까지 채운 몸, eye: 눈 구멍} 흰색 마스크를 만든다. 다섯 장 모두 같은 틀로 잘라 발 위치가 흔들리지 않게 한다.
  async function loadMasks() {
    const imgs = await Promise.all([0, 1, 2, 3, 4].map(i => loadImg(`${ROOT}assets/cat/cat_running_${i}.png`)));
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
    x.globalCompositeOperation = 'source-over';
    return c;
  }
  const spriteCache = new Map();
  function remember(key, make) {
    let c = spriteCache.get(key);
    if (c) return c;
    if (spriteCache.size > 800) spriteCache.clear(); // 불꽃 색이 프레임마다 달라 끝없이 늘지 않게
    c = make();
    spriteCache.set(key, c);
    return c;
  }
  // 몸색 + 눈색까지 입힌 프레임 한 장
  const catFrame = (masks, i, color, h) => remember(`${i}|${color}|${h}`, () => {
    const c = paint(masks[i].sil, color, h);
    c.getContext('2d').drawImage(paint(masks[i].eye, eyeFor(color), h), 0, 0);
    return c;
  });
  // 눈 없이 실루엣만 (잔상·글리치 갈라짐용)
  const silFrame = (masks, i, color, h) => remember(`s${i}|${color}|${h}`, () => paint(masks[i].sil, color, h));

  // 앱과 비슷한 속도: 사용률 p(0~1) → 키 40px 고양이 기준 px/s. 빨리 달릴수록 다리도 빨리 움직인다.
  const speedFor = p => 50 + 270 * p;
  const fpsFor = v => 5 + v / 14;

  class Runner {
    constructor(x, dir) { this.x = x; this.dir = dir; this.frame = 0; }
    step(dt, v, fps, minX, maxX) {
      this.x += this.dir * v * dt;
      if (this.x > maxX) { this.x = maxX; this.dir = -1; }
      if (this.x < minX) { this.x = minX; this.dir = 1; }
      this.frame = (this.frame + dt * fps) % 5;
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

  /* ───────── 네온 무대 배경 ───────── */
  // 밤하늘 + 수평선 + 앞으로 흘러오는 격자 바닥. 격자는 사용률이 높을수록 빨리 흐른다.
  function drawArcade(ctx, w, h, floorH, t, scroll) {
    const hz = h - floorH;
    const sky = ctx.createLinearGradient(0, 0, 0, hz);
    sky.addColorStop(0, '#07051a'); sky.addColorStop(1, '#3a0f52');
    ctx.fillStyle = sky; ctx.fillRect(0, 0, w, hz);
    ctx.fillStyle = '#fff';
    for (let i = 0; i < 46; i++) {
      ctx.globalAlpha = 0.25 + 0.6 * hash(i + 7) * (0.6 + 0.4 * Math.sin(t * 2 + i));
      ctx.fillRect(Math.round(hash(i) * w), Math.round(hash(i + 99) * hz * 0.85), 2, 2);
    }
    ctx.globalAlpha = 1;
    ctx.fillStyle = '#0a0418'; ctx.fillRect(0, hz, w, floorH);
    ctx.save();
    ctx.beginPath(); ctx.rect(0, hz, w, floorH); ctx.clip();
    ctx.strokeStyle = 'rgba(255,62,165,.55)'; ctx.lineWidth = 1.5;
    const off = fract(scroll);
    for (let k = 0; k < 9; k++) {
      const y = hz + Math.pow((k + off) / 9, 2.2) * floorH;
      ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke();
    }
    for (let i = -14; i <= 14; i++) {
      ctx.beginPath(); ctx.moveTo(w / 2 + i * w * 0.045, hz); ctx.lineTo(w / 2 + i * w * 0.32, h); ctx.stroke();
    }
    ctx.restore();
    ctx.save();
    ctx.shadowColor = '#ff3ea5'; ctx.shadowBlur = 14;
    ctx.fillStyle = '#ff6ec0'; ctx.fillRect(0, hz - 1, w, 2);
    ctx.restore();
  }

  /* ───────── 스페셜 스킨 ───────── */
  // ── 스페셜 스킨 시작 ── Copyright (c) 2026 Hyuntae Jeong. All rights reserved.
  // 여기서 "스페셜 스킨 끝"까지는 MIT License 적용 대상이 아닙니다. 사용 조건: app/src/Woodada/Sprites/Skins/LICENSE
  //
  // 스킨마다 { make(masks) → 무대 위 한 프레임 그리는 함수, stats(u, s) → 능력치 } 를 둔다.
  // u: CPU 사용률(0~100). 능력치는 {키: [0~1 막대 값, 오른쪽 글자]}이고 키는 cat_pages.py의 stats와 같다.
  const pct = v => Math.round(v * 100) + '%';
  const spd = u => speedFor(u / 100) / speedFor(1);

  // 유령 (Sprites/Skins/GhostSkin.cs): 반투명 몸 45%, 외곽선 50%, 바깥 발광 55%, 키의 25% 떠서 ±10% 흔들림, 1.7초 주기
  const GHOST = { body: '#DDEBFF', bodyOpacity: 0.45, outlineOpacity: 0.50, glowOpacity: 0.55, float: 0.25, bob: 0.10, period: 1.7 };
  const ghostLift = t => GHOST.float + GHOST.bob * Math.sin(t * 2 * Math.PI / GHOST.period);
  function ghostFrames(masks, devH) {
    const line = mix(GHOST.body, '#FFFFFF', 0.5);
    return masks.map(m => {
      const body = paint(m.sil, GHOST.body, devH);
      const pad = Math.ceil(devH * 0.35);
      const W = body.width + pad * 2, H = body.height + pad * 2;
      const c = mk(W, H), x = c.getContext('2d');
      // 발광: 몸 바깥에만
      x.save();
      x.shadowColor = 'rgba(221,235,255,.95)';
      x.shadowBlur = devH * 0.28;
      x.globalAlpha = GHOST.glowOpacity;
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
      x.globalAlpha = GHOST.outlineOpacity; x.drawImage(ring, 0, 0);
      x.globalAlpha = GHOST.bodyOpacity; x.drawImage(body, pad, pad);
      x.globalAlpha = 1; x.drawImage(paint(m.eye, eyeFor(GHOST.body), devH), pad, pad);
      return { c, pad };
    });
  }

  // 플래시 (Sprites/Skins/FlashSkin.cs, Rendering/SkinPainter.cs AddFlashTrail): 값과 공식을 앱과 똑같이 맞췄다
  const FLASH = {
    afterimages: 8, trailLength: 0.90, firstOpacity: 0.60, minStrength: 0.35, fullUsage: 40,
    heatStart: 50, heatFull: 60, heatSteps: 5, heatColor: '#D9626B',
    wispCount: 4, wispLength: 1.30, wispSegments: 14,
  };
  const FIRE = ['#FF4D2E', '#FF8A3D', '#FFC24A', '#FFE9A0'];
  const FLASH_BODY = '#ECE7DF'; // 불꽃과 대비가 나도록 하얀 고양이로 보여 준다
  const flashStrength = u => FLASH.minStrength + (1 - FLASH.minStrength) * clamp(u / FLASH.fullUsage, 0, 1);
  const speedShare = u => 0.35 + 0.65 * clamp(u / 100, 0, 1);
  const heatOf = u => Math.round(clamp((u - FLASH.heatStart) / (FLASH.heatFull - FLASH.heatStart), 0, 1) * FLASH.heatSteps) / FLASH.heatSteps;
  const flashTail = u => FLASH.trailLength * flashStrength(u) * speedShare(u);
  // 꼬리 쪽 위치 t(0 = 몸, 1 = 끝)의 색. 몸에 붙은 쪽은 몸색과 섞는다.
  function trailColor(t, body) {
    t = clamp(t, 0, 1);
    const x = t * (FIRE.length - 1), i = Math.min(FIRE.length - 2, Math.floor(x));
    const fire = mix(FIRE[i], FIRE[i + 1], x - i);
    return t < 0.2 ? mix(body, fire, 0.5 + t * 2.5) : fire;
  }
  const flashBody = (chosen, u) => { const h = heatOf(u); return h <= 0 ? chosen : mix(chosen, FLASH.heatColor, h); };
  // 불꽃 줄기: 등 한가운데쯤에서 뒤로 뻗는 가는 줄. 끝으로 갈수록 가늘고 옅어지며 위로 살짝 들리고 일렁인다.
  function drawWisps(ctx, bx, by, w, h, dir, u, seconds, phase, body, dpr) {
    const k = flashStrength(u);
    const back = bx + (dir > 0 ? w * 0.35 : w * 0.65);
    const maxWidth = Math.max(1.5, h * dpr * 0.07) / dpr;
    const bottom = by + h;
    ctx.lineCap = 'round';
    for (let j = 0; j < FLASH.wispCount; j++) {
      const r = 0.5 + 0.5 * Math.sin(j * 12.9898 + phase);
      const len = w * FLASH.wispLength * k * speedShare(u) * (0.55 + 0.45 * r);
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

  // 글리치 (Sprites/Skins/GlitchSkin.cs): 평소엔 빨강·청록이 옅게 번지고, 가끔 갈라지며 가로 줄이 밀리고 노이즈가 튄다.
  // CPU 0%면 1초에 0.4번, 100%면 3번 터지고, 사용률이 높을수록(power) 더 세게 갈라진다.
  const GLITCH = {
    rateIdle: 0.4, rateFull: 3.0, burst: 0.22, reroll: 0.6, split: 2.0, bands: 3, bandShift: 0.18, noise: 4,
    idleFringe: 0.20, splitOpacity: 0.85, noiseOpacity: 0.90,
    a: '#FF2E4D', b: '#22E6F0', body: '#AE8FC7', // 라벤더 몸에는 앱도 빨강/청록 조합을 쓴다
  };
  const glitchRate = u => GLITCH.rateIdle + (GLITCH.rateFull - GLITCH.rateIdle) * clamp(u / 100, 0, 1);
  const glitchPower = u => 0.45 + 0.55 * clamp(u / 100, 0, 1);
  function glitchShape(u) {
    const power = glitchPower(u);
    const bands = [];
    for (let i = 0; i < GLITCH.bands; i++) {
      const height = rand(0.06, 0.18);
      bands.push({ top: rand(0, 1 - height), height, shift: rand(-1, 1) * GLITCH.bandShift * power });
    }
    const noise = [];
    for (let i = 0, n = Math.round(GLITCH.noise * power); i < n; i++) {
      noise.push({ x: rand(-0.3, 1.2), y: rand(0, 1), w: rand(0.06, 0.3), h: rand(0.03, 0.08), c: [GLITCH.a, GLITCH.b, '#FFFFFF'][Math.floor(Math.random() * 3)] });
    }
    return { split: Math.max(1, GLITCH.split * power * rand(0.6, 1.4)), side: Math.random() < 0.5 ? 1 : -1, bands, noise };
  }

  // 털실 (Sprites/Skins/YarnSkin.cs): 털실 뭉치를 툭툭 차며 달린다. 빨리 달릴수록 세게 차서 통통 튀며 멀리 굴러가고,
  // 털실이 뒤로 빠지면 뒷발로 머리 위로 넘긴다. 두 마리가 같이 입으면 털실 하나로 패스한다. 값과 공식은 앱과 같다.
  const YARN = {
    ball: 0.40, kickSoft: 1.5, kickHard: 2.4, hopSoft: 0.04, hopHard: 0.65, bounce: 0.35, lobHeight: 1.4,
    thread: 1.1, strands: 3, yieldBodies: 6, gravity: 30, cooldown: 0.15, segs: 10,
    // 굴러가는 거리(몸 너비). 앱 작업표시줄은 0.5~3.2, 무대는 폭이 몸 6~8개라 앱 설정 창 미리보기(YarnTuning.Preview) 값을 쓴다.
    softGap: 0.5, hardGap: 3.2, stageSoftGap: 0.5, stageHardGap: 2.0,
    colors: ['#E0474C', '#F28AB2', '#4C8BF5', '#F2C14E', '#5DBB63'], // 빨강·분홍·파랑·노랑·초록
  };
  const lerp = (a, b, k) => a + (b - a) * k;
  const yarnJitter = (a, b) => a + Math.random() * (b - a);
  // 차는 힘: 앱은 달리는 속도로 정하는데, 그 값은 사용률^0.7과 같다(0 살포시 ~ 1 세게).
  const yarnPower = u => Math.pow(clamp(u / 100, 0, 1), 0.7);
  const yarnKick = p => lerp(YARN.kickSoft, YARN.kickHard, p);
  const yarnHop = p => lerp(YARN.hopSoft, YARN.hopHard, p);
  const yarnGap = p => lerp(YARN.softGap, YARN.hardGap, p);
  const stageGap = p => lerp(YARN.stageSoftGap, YARN.stageHardGap, p);
  function hsl(hex) {
    const [r, g, b] = hexRgb(hex).map(v => v / 255), mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2, d = mx - mn;
    if (!d) return { h: 0, s: 0, l };
    const s = d / (1 - Math.abs(2 * l - 1));
    const h = mx === r ? ((g - b) / d + 6) % 6 : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return { h: h * 60, s, l };
  }
  // 털실 색: 입은 고양이들 색과 색상환에서 가장 먼 것. 모두 무채색이면 빨강.
  function yarnColor(bodies) {
    const hues = bodies.map(hsl).filter(c => c.s >= 0.25 && c.l >= 0.15 && c.l <= 0.85).map(c => c.h);
    if (!hues.length) return YARN.colors[0];
    const dist = (a, b) => { const d = Math.abs(a - b) % 360; return d > 180 ? 360 - d : d; };
    let best = YARN.colors[0], far = -1;
    for (const y of YARN.colors) {
      const d = Math.min(...hues.map(h => dist(h, hsl(y).h)));
      if (d > far) { far = d; best = y; }
    }
    return best;
  }

  // 털실 하나와 그걸 차는 고양이들(앱의 YarnPlay). 고양이는 { x: 몸 왼쪽, dir, w, h, v: px/s, p: 차는 힘 } 이고,
  // 프레임마다 같은 객체의 값을 고쳐 넘긴다. 털실 좌표는 x(가운데)와 바닥에서 뜬 높이 h, 실 꼬리 ry도 바닥에서 위로 잰다.
  function makeYarn() {
    const b = {
      x: 0, h: 0, vx: 0, vh: 0, decel: 0, angle: 0, squash: 0, power: 0, lob: false, last: null, cool: 0, owner: null,
      r: 2, bh: 1, bw: 1, time: 0, spark: NaN, sparkX: 0, sparkDir: 1, sparkP: 0, rx: [], ry: [], px: [], py: [], ready: false,
    };
    let acc = 0;
    const center = c => c.x + c.w / 2;
    const decelFor = (w, v, p) => Math.max(w * 0.03, Math.pow((yarnKick(p) - 1) * v, 2) / (2 * stageGap(p) * w));
    function respawn(cat, drop) {
      const w = cat.w, p = cat.p;
      b.x = center(cat) + cat.dir * (w / 2 + b.r + stageGap(p) * w * 0.5);
      b.h = drop ? cat.h * 0.9 : 0;
      b.vx = cat.dir * cat.v; b.vh = 0; b.decel = decelFor(w, cat.v, p);
      b.squash = 0; b.power = p; b.lob = false; b.last = cat; b.cool = 0; b.spark = NaN;
      for (let i = 0; i <= YARN.segs; i++) { b.rx[i] = b.px[i] = b.x; b.ry[i] = b.py[i] = 0; }
    }
    function touched(cat, p, x) {
      b.power = p; b.last = cat; b.cool = YARN.cooldown;
      b.spark = b.time; b.sparkX = x; b.sparkDir = cat.dir; b.sparkP = p;
    }
    function kick(cat, front) {
      const g = YARN.gravity * b.bh, p = cat.p;
      b.vx = cat.dir * yarnKick(p) * cat.v * yarnJitter(0.9, 1.1);
      b.decel = decelFor(cat.w, cat.v, p);
      b.vh = yarnHop(p) * Math.sqrt(2 * g * b.bh * 0.6) * yarnJitter(0.7, 1.15);
      b.squash = 0.25 + 0.75 * p; // 살포시 차면 거의 안 찌그러지고, 세게 차면 확 찌그러진다
      touched(cat, p, front);
    }
    // 뒷발로 넘기기: 머리 위로 띄워 앞으로 보낸다. 날아가는 동안 고양이가 달린 거리까지 셈해서 앞에 떨어지게.
    function lob(cat) {
      const g = YARN.gravity * b.bh, p = cat.p;
      const vh = Math.sqrt(2 * g * b.bh * YARN.lobHeight * lerp(0.6, 1.25, p)), flight = 2 * vh / g;
      const land = center(cat) + cat.dir * (cat.v * flight + cat.w / 2 + b.r + stageGap(p) * cat.w * 0.6);
      b.vx = (land - b.x) / flight; b.vh = vh; b.lob = true;
      b.decel = decelFor(cat.w, cat.v, p);
      b.squash = 0.25 + 0.75 * p;
      touched(cat, p, b.x);
    }
    // 누가 차고, 넘기고, 맡기는지
    function play(cats) {
      for (const c of cats) {
        if (b.owner && c !== b.owner) continue;
        const front = c.dir > 0 ? c.x + c.w : c.x;
        if (c.dir * (b.x - center(c)) > 0 && c.dir * (b.x - front) < b.r * 0.4 && b.h < b.bh * 0.45 && c.dir * b.vx < c.v * 1.05) {
          kick(c, front);
          return;
        }
      }
      if (b.h >= b.bh * 0.2) return;
      // 털실이 내 뒤로 빠졌으면 넘긴다. 같이 쓰는 털실이면, 털실을 앞에 두고 다가오는 고양이가 가까이 있을 때는 맡긴다.
      let behind = null, coming = false;
      for (const c of cats) {
        if (b.owner && c !== b.owner) continue;
        const ahead = c.dir * (b.x - center(c));
        if (ahead <= 0 && -ahead < c.w * 2.5 && (!behind || Math.abs(center(c) - b.x) < Math.abs(center(behind) - b.x))) behind = c;
        if (ahead > 0 && ahead < c.w * YARN.yieldBodies) coming = true;
      }
      if (behind) { if (!coming) lob(behind); }
      else if (b.owner && b.owner.dir * (center(b.owner) - b.x) >= b.owner.w * 2.5) respawn(b.owner, true);
    }
    // 실 꼬리: 털실 가운데에 매달린 짧은 밧줄(버렛 적분). 바닥에 닿은 마디는 끌리며 느려진다.
    function stepRope(dt) {
      const air = Math.pow(0.98, dt * 30), drag = Math.pow(0.55, dt * 30);
      const seg = Math.max(0.5, YARN.thread * b.bw / YARN.segs), g = YARN.gravity * b.bh * 0.6;
      const x = b.rx, y = b.ry, px = b.px, py = b.py;
      x[0] = b.x; y[0] = b.h + b.r;
      for (let i = 1; i <= YARN.segs; i++) {
        const vx = (x[i] - px[i]) * (y[i] <= 0.1 ? drag : air), vy = (y[i] - py[i]) * air;
        px[i] = x[i]; py[i] = y[i];
        x[i] += vx; y[i] += vy - g * dt * dt;
      }
      for (let k = 0; k < 6; k++) {
        for (let i = 1; i <= YARN.segs; i++) {
          const dx = x[i] - x[i - 1], dy = y[i] - y[i - 1], d = Math.max(1e-6, Math.hypot(dx, dy)), diff = (d - seg) / d;
          if (i === 1) { x[i] -= dx * diff; y[i] -= dy * diff; }
          else { x[i - 1] += dx * diff * 0.5; y[i - 1] += dy * diff * 0.5; x[i] -= dx * diff * 0.5; y[i] -= dy * diff * 0.5; }
        }
        for (let i = 1; i <= YARN.segs; i++) if (y[i] < 0) y[i] = 0;
      }
    }
    function stepBall(dt, cats, left, right) {
      const g = YARN.gravity * b.bh;
      b.cool = Math.max(0, b.cool - dt);
      b.vh -= g * dt; b.h += b.vh * dt;
      if (b.h <= 0) {
        b.h = 0;
        // 넘긴 털실이 처음 땅에 닿으면 찬 것처럼 고양이보다 조금 빠른 속도만 남긴다
        if (b.lob && b.vh < 0 && b.last) b.vx = Math.sign(b.vx) * Math.min(Math.abs(b.vx), yarnKick(b.power) * b.last.v);
        if (b.vh < -Math.sqrt(2 * g * b.bh * 0.04)) { b.vh = -b.vh * YARN.bounce; b.squash = Math.max(b.squash, 0.7); }
        else b.vh = 0;
        if (b.vh === 0) b.lob = false;
      }
      if (b.h === 0) { const m = Math.abs(b.vx) - b.decel * dt; b.vx = m > 0 ? Math.sign(b.vx) * m : 0; }
      b.x += b.vx * dt;
      if (b.x < left + b.r) { b.x = left + b.r; b.vx = Math.abs(b.vx) * 0.5; }
      if (b.x > right - b.r) { b.x = right - b.r; b.vx = -Math.abs(b.vx) * 0.5; }
      b.angle += b.vx * dt / b.r;
      b.squash *= Math.exp(-dt * 14);
      if (!b.lob && b.cool <= 0) play(cats);
      stepRope(dt);
    }
    // 고양이들이 움직인 바로 뒤에 부른다. left·right는 털실이 굴러갈 수 있는 끝.
    b.step = (dt, cats, left, right) => {
      if (!cats.length) return;
      const owner = cats.length === 1 ? cats[0] : null;
      b.bh = cats.reduce((s, c) => s + c.h, 0) / cats.length;
      b.bw = cats.reduce((s, c) => s + c.w, 0) / cats.length;
      b.r = Math.max(2, b.bh * YARN.ball / 2);
      if (!b.ready || b.owner !== owner || b.x < left - b.r || b.x > right + b.r) { b.ready = true; b.owner = owner; respawn(owner || cats[0], false); }
      // 앱처럼 1/60초씩 나눠 진행해서 화면 fps와 상관없이 같은 움직임이 나오게 한다
      acc = Math.min(acc + dt, 0.25);
      while (acc >= 1 / 60) { acc -= 1 / 60; b.time += 1 / 60; stepBall(1 / 60, cats, left, right); }
    };
    return b;
  }

  // 멈춘 털실 한 장(공유 이미지·작은 그림): 고양이 앞에서 막 차여 떠오른 털실과 바닥에 늘어진 실 꼬리.
  function stillYarn(catX, catW, catH, p) {
    const r = catH * YARN.ball / 2, x = catX + catW + r * 1.7, h = catH * 0.32 * p;
    const b = { x, h, vx: 0, r, bh: catH, bw: catW, angle: 0.7, squash: 0, time: 0.05, spark: p > 0.2 ? 0 : NaN, sparkX: catX + catW, sparkDir: 1, sparkP: p, rx: [], ry: [] };
    const seg = YARN.thread * catW / YARN.segs;
    let rx = x, ry = h + r;
    for (let i = 0; i <= YARN.segs; i++) {
      b.rx[i] = rx; b.ry[i] = ry;
      const down = Math.min(ry, seg * 0.8);
      ry -= down;
      rx -= Math.sqrt(Math.max(0, seg * seg - down * down)) * (0.9 + 0.2 * Math.sin(i * 1.7));
    }
    return b;
  }

  // 몸 뒤에 깔리는 것: 실 꼬리와 바닥 그림자. floor는 바닥 줄의 y(CSS 픽셀).
  function drawYarnBack(ctx, b, color, floor, dpr) {
    const lw = Math.max(1 / dpr, b.bh / 28);
    ctx.save();
    ctx.strokeStyle = mix(color, '#000000', 0.12); ctx.lineWidth = lw; ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    ctx.beginPath();
    for (let i = 0; i <= YARN.segs; i++) ctx[i ? 'lineTo' : 'moveTo'](b.rx[i], floor - b.ry[i] - lw / 2);
    ctx.stroke();
    // 높이 뜰수록 작고 옅은 그림자
    const up = Math.min(1, b.h / (b.bh * 1.5));
    ctx.fillStyle = `rgba(0,0,0,${0.35 * (1 - up * 0.7)})`;
    ctx.beginPath(); ctx.ellipse(b.x, floor, b.r * (1 - up * 0.4), Math.max(0.8 / dpr, b.r * 0.22), 0, 0, Math.PI * 2); ctx.fill();
    ctx.restore();
  }

  // 몸 앞에 그리는 것: 바람 줄, 털실, 찰 때 발끝에서 튄 선.
  function drawYarnFront(ctx, b, color, floor, dpr) {
    const r = b.r, cx = b.x, cy = floor - b.h - r, body = b.bh;
    ctx.save();
    ctx.lineCap = 'round';
    // 바람 줄: 세게 차서 빨리 굴러갈 때만 뒤로 끌린다
    const fast = Math.min(1, (Math.abs(b.vx) / Math.max(1, b.bw) - 5) / 8);
    if (fast > 0) {
      const dir = Math.sign(b.vx), x0 = cx - dir * r * 1.3;
      ctx.strokeStyle = '#FFFFFF'; ctx.globalAlpha = 0.55 * fast; ctx.lineWidth = Math.max(1 / dpr, body / 32);
      for (const [dy, len] of [[-0.45, 0.8], [0, 1.2], [0.45, 0.7]]) {
        ctx.beginPath(); ctx.moveTo(x0, cy + dy * r); ctx.lineTo(x0 - dir * r * 2.2 * len * fast, cy + dy * r); ctx.stroke();
      }
      ctx.globalAlpha = 1;
    }
    // 털실. 차이거나 바닥에 떨어진 순간 바닥 쪽을 축으로 옆으로 퍼졌다가 돌아온다.
    const sq = b.squash * 0.22, bottom = floor - b.h, dark = mix(color, '#000000', 0.38);
    ctx.save();
    ctx.translate(cx, bottom); ctx.scale(1 + sq, 1 - sq); ctx.translate(-cx, -bottom);
    ctx.fillStyle = color;
    ctx.beginPath(); ctx.arc(cx, cy, r, 0, Math.PI * 2); ctx.fill();
    // 감긴 실 줄: 구르는 만큼 돌아간다
    ctx.strokeStyle = dark; ctx.lineWidth = Math.max(1 / dpr, r / 5);
    for (let i = 0; i < YARN.strands; i++) {
      ctx.beginPath(); ctx.ellipse(cx, cy, r * 0.9, r * (0.28 + 0.5 * (i % 2)), b.angle + i * Math.PI / YARN.strands, 0, Math.PI * 2); ctx.stroke();
    }
    // 하이라이트와 테두리
    ctx.fillStyle = mix(color, '#FFFFFF', 0.45); ctx.globalAlpha = 0.7;
    ctx.beginPath(); ctx.arc(cx - r * 0.35, cy - r * 0.38, r * 0.28, 0, Math.PI * 2); ctx.fill();
    ctx.globalAlpha = 1;
    const outline = Math.max(1 / dpr, r / 6);
    ctx.lineWidth = outline;
    ctx.beginPath(); ctx.arc(cx, cy, r - outline / 2, 0, Math.PI * 2); ctx.stroke();
    ctx.restore();
    // 찰 때 튀는 짧은 선. 살포시 차면 옅은 선 하나, 세게 차면 길고 많은 선이 오래 남는다.
    const p = b.sparkP, age = (b.time - b.spark) / lerp(0.12, 0.22, p);
    if (age >= 0 && age < 1) {
      const len = body * 0.18 * lerp(0.45, 1.5, p) * (0.5 + age), off = body * 0.1 + body * 0.15 * age * lerp(0.6, 1.4, p);
      ctx.strokeStyle = '#FFFFFF'; ctx.globalAlpha = (1 - age) * lerp(0.45, 1, p); ctx.lineWidth = Math.max(1 / dpr, body / 28 * lerp(0.8, 1.4, p));
      const angles = p < 0.3 ? [-0.35] : p < 0.6 ? [-0.9, -0.35, 0.2] : [-1.15, -0.75, -0.35, 0.05, 0.4];
      for (const a of angles) {
        const dx = Math.cos(a) * b.sparkDir, dy = Math.sin(a), sx = b.sparkX + dx * off, sy = floor - r + dy * off;
        ctx.beginPath(); ctx.moveTo(sx, sy); ctx.lineTo(sx + dx * len, sy + dy * len); ctx.stroke();
      }
    }
    ctx.restore();
  }

  // 털실냥을 보여 줄 때 고양이 색: 앱 도감·뽑기처럼 하얀 고양이(무채색이라 털실은 빨강)
  const YARN_BODY = '#ECE7DF', YARN_MATE = '#9A9AA0';
  function drawYarnStill(ctx, masks, x, y, catW, catH, devH, dpr, p) {
    const b = stillYarn(x, catW, catH, p), color = yarnColor([YARN_BODY]);
    drawYarnBack(ctx, b, color, y + catH, dpr);
    ctx.drawImage(catFrame(masks, 1, YARN_BODY, devH), x, y, catW, catH);
    drawYarnFront(ctx, b, color, y + catH, dpr);
  }

  // 다꾸 (Sprites/Skins/DakuSkin.cs): 흰 테두리를 두른 스티커 고양이가 지나간 자리에 가끔 스티커를 붙인다.
  // 큰 스티커 13종(모양 8 + 고양이 자세 5)과 그 둘레에 작은 스티커 0~3장, 색은 붙일 때마다 아무 파스텔색.
  // 빨리 달릴수록 자주 붙이고 큰 스티커끼리는 몸 3개만큼 띄운다. 남았다가 귀퉁이부터 들려 떨어진다. 값과 공식은 앱과 같다.
  // 앱 작업표시줄은 12~3초마다 붙이고 1분 남는데, 무대는 폭이 좁아 앱 설정 창 미리보기(DakuTuning.Preview) 값을 쓴다.
  const DAKU = {
    outline: 0.07, shadow: 0.30, mainMin: 0.45, mainMax: 0.85, smallMin: 0.22, smallMax: 0.40, smallCount: 3, spread: 0.30,
    tilt: 20, gap: 3, pop: 0.22, peel: 1.1, fold: 0.65, drop: 0.55,
    appSlow: 12, appFast: 3, slow: 5, fast: 1.5, life: 20, first: 0.8,
  };
  const DAKU_BODY = '#FFB0DA'; // 앱 뽑기 무대·도감처럼 솜사탕 분홍 몸(흰 테두리가 잘 보인다)
  const DAKU_MAIN = 13, DAKU_SMALL = 8, DAKU_CAT = 8;
  const DAKU_LIVE = { n: 0, max: 1, size: 0.65 }; // 무대에 지금 붙은 큰 스티커 수, 들어갈 수 있는 수, 마지막 크기(능력치용)
  const dRand = (a, b) => a + Math.random() * (b - a);
  const dakuPower = u => Math.pow(clamp(u / 100, 0, 1), 0.7); // 앱은 달리는 속도로 정하는데, 그 값은 사용률^0.7과 같다
  function dakuHsl(h, s, l) {
    h = ((h % 360) + 360) % 360;
    const c = (1 - Math.abs(2 * l - 1)) * s, x = c * (1 - Math.abs((h / 60) % 2 - 1)), m = l - c / 2;
    const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
    return '#' + [r, g, b].map(v => Math.round(clamp((v + m) * 255, 0, 255)).toString(16).padStart(2, '0')).join('');
  }
  const dakuPastel = h => dakuHsl(h, dRand(0.62, 0.85), dRand(0.76, 0.84));
  function dakuTint(c, color) {
    const t = mk(c.width, c.height), x = t.getContext('2d');
    x.drawImage(c, 0, 0); x.globalCompositeOperation = 'source-in'; x.fillStyle = color; x.fillRect(0, 0, t.width, t.height);
    return t;
  }
  // 그림(둘레에 여백이 있는 캔버스)을 스티커로: 동그란 붓으로 민 흰 테두리 + 아래로 살짝 뜬 그림자. 떨어질 때 쓸 뒷면도 만든다.
  function dakuStickerize(art, b) {
    const W = art.width, H = art.height, white = mk(W, H), wx = white.getContext('2d');
    for (let dy = -b; dy <= b; dy++) for (let dx = -b; dx <= b; dx++) if (dx * dx + dy * dy <= b * b + b * 0.5) wx.drawImage(art, dx, dy);
    wx.globalCompositeOperation = 'source-in'; wx.fillStyle = '#fff'; wx.fillRect(0, 0, W, H);
    const out = mk(W, H), o = out.getContext('2d');
    o.save();
    o.filter = `blur(${Math.max(0.5, b * 0.6)}px)`; o.globalAlpha = DAKU.shadow;
    o.drawImage(dakuTint(white, '#000'), 0, Math.max(1, Math.round(b * 0.7)));
    o.restore();
    o.drawImage(white, 0, 0); o.drawImage(art, 0, 0);
    out.back = dakuTint(white, '#f3f1ee'); out.shade = dakuTint(white, '#000');
    return out;
  }
  // 스티커 모양 (가운데 0,0 · 키 s, 장치 픽셀). 앱 DakuSkin.Shapes와 같다.
  function dakuShape(x, kind, s, color, hue) {
    const dark = mix(color, '#000000', 0.25), P = Math.PI;
    x.lineJoin = 'round'; x.lineCap = 'round'; x.lineWidth = Math.max(1, s / 16); x.strokeStyle = dark; x.fillStyle = color;
    const fs = () => { x.fill(); x.stroke(); };
    const star = r0 => { x.beginPath(); for (let i = 0; i < 10; i++) { const r = i % 2 ? r0 * 0.48 : r0, a = -P / 2 + i * P / 5; x.lineTo(Math.cos(a) * r, Math.sin(a) * r + s * 0.04); } x.closePath(); fs(); };
    const heart = k => { x.beginPath(); x.moveTo(0, k * 0.42); x.bezierCurveTo(-k * 0.62, k * 0.02, -k * 0.5, -k * 0.5, 0, -k * 0.2); x.bezierCurveTo(k * 0.5, -k * 0.5, k * 0.62, k * 0.02, 0, k * 0.42); fs(); };
    switch (kind) {
      case 0: star(s * 0.52); break;
      case 1: heart(s); break;
      case 2:
        for (let i = 0; i < 5; i++) { const a = -P / 2 + i * P * 2 / 5; x.beginPath(); x.arc(Math.cos(a) * s * 0.26, Math.sin(a) * s * 0.26, s * 0.21, 0, P * 2); fs(); }
        x.beginPath(); x.arc(0, 0, s * 0.15, 0, P * 2); x.fillStyle = '#fff2a6'; fs(); break;
      case 3:
        x.beginPath(); x.arc(-s * 0.26, s * 0.08, s * 0.2, P * 0.5, P * 1.5); x.arc(-s * 0.04, -s * 0.08, s * 0.26, P * 1.05, P * 1.85);
        x.arc(s * 0.27, s * 0.06, s * 0.2, P * 1.4, P * 0.5); x.closePath(); fs(); break;
      case 4:
        for (const d of [-1, 1]) {
          x.beginPath(); x.moveTo(0, 0); x.quadraticCurveTo(d * s * 0.3, -s * 0.42, d * s * 0.52, -s * 0.22);
          x.quadraticCurveTo(d * s * 0.6, s * 0.02, d * s * 0.5, s * 0.2); x.quadraticCurveTo(d * s * 0.3, s * 0.3, 0, 0); fs();
          x.beginPath(); x.moveTo(d * s * 0.05, s * 0.06); x.lineTo(d * s * 0.26, s * 0.46); x.lineTo(d * s * 0.12, s * 0.42); x.lineTo(d * s * 0.02, s * 0.5); x.closePath(); fs();
        }
        x.beginPath(); x.arc(0, 0, s * 0.11, 0, P * 2); fs(); break;
      case 5:
        x.beginPath(); x.moveTo(0, s * 0.48); x.quadraticCurveTo(-s * 0.5, s * 0.1, -s * 0.36, -s * 0.18);
        x.quadraticCurveTo(0, -s * 0.34, s * 0.36, -s * 0.18); x.quadraticCurveTo(s * 0.5, s * 0.1, 0, s * 0.48); fs();
        x.fillStyle = '#ffffff';
        for (const [px, py] of [[-0.16, -0.04], [0.14, -0.06], [0, 0.1], [-0.12, 0.22], [0.12, 0.2], [0, 0.34]]) { x.beginPath(); x.ellipse(px * s, py * s, Math.max(0.6, s * 0.035), Math.max(0.8, s * 0.05), 0, 0, P * 2); x.fill(); }
        x.fillStyle = '#9fd99b'; x.strokeStyle = '#5f9e5b'; x.beginPath();
        for (let i = 0; i < 10; i++) { const r = i % 2 ? s * 0.08 : s * 0.24, a = P + i * P / 9; x.lineTo(Math.cos(a) * r, -s * 0.24 + Math.sin(a) * r * 0.55); }
        x.closePath(); fs(); break;
      case 6:
        x.beginPath(); x.ellipse(0, s * 0.18, s * 0.27, s * 0.21, 0, 0, P * 2); fs();
        for (const [px, py, a] of [[-0.33, -0.08, -0.4], [-0.12, -0.3, -0.12], [0.12, -0.3, 0.12], [0.33, -0.08, 0.4]]) { x.beginPath(); x.ellipse(px * s, py * s, s * 0.11, s * 0.14, a, 0, P * 2); fs(); }
        break;
      case 7: {
        const outer = s * 0.56, th = s * 0.1, cy = s * 0.26;
        x.lineCap = 'butt';
        for (let i = 0; i < 4; i++) { x.beginPath(); x.arc(0, cy, outer - th * (i + 0.5), P, P * 2); x.strokeStyle = dakuHsl(hue + i * 55, 0.74, 0.8); x.lineWidth = th + 0.6; x.stroke(); }
        x.lineCap = 'round'; x.strokeStyle = dark; x.lineWidth = Math.max(1, s / 16);
        x.beginPath(); x.arc(0, cy, outer, P, P * 2); x.stroke();
        x.beginPath(); x.arc(0, cy, outer - th * 4, P, P * 2); x.stroke();
        x.beginPath(); x.moveTo(-outer, cy); x.lineTo(-outer + th * 4, cy); x.moveTo(outer, cy); x.lineTo(outer - th * 4, cy); x.stroke();
        break;
      }
      case DAKU_MAIN: { // 반짝이
        const R = s * 0.52, r = s * 0.12;
        x.beginPath(); x.moveTo(0, -R);
        for (let i = 0; i < 4; i++) { const a = -P / 2 + i * P / 2, b = a + P / 4, c = a + P / 2; x.quadraticCurveTo(Math.cos(b) * r, Math.sin(b) * r, Math.cos(c) * R, Math.sin(c) * R); }
        x.closePath(); fs(); break;
      }
      case DAKU_MAIN + 1: x.beginPath(); x.arc(0, 0, s * 0.42, 0, P * 2); fs(); break;
      case DAKU_MAIN + 2: heart(s * 1.1); break;
      case DAKU_MAIN + 3: star(s * 0.52); break;
      case DAKU_MAIN + 4:
        x.beginPath(); x.ellipse(-s * 0.14, s * 0.3, s * 0.18, s * 0.13, -0.4, 0, P * 2); fs();
        x.lineWidth = Math.max(1, s * 0.09); x.beginPath(); x.moveTo(s * 0.03, s * 0.28); x.lineTo(s * 0.03, -s * 0.44);
        x.quadraticCurveTo(s * 0.22, -s * 0.3, s * 0.32, -s * 0.12); x.strokeStyle = color; x.stroke(); break;
      case DAKU_MAIN + 5: {
        const R = s * 0.46;
        x.beginPath(); x.arc(0, 0, R, P * 0.35, P * 1.65); x.arc(R * 0.42, -R * 0.08, R * 0.8, P * 1.45, P * 0.55, true); x.closePath(); fs(); break;
      }
      case DAKU_MAIN + 6:
        x.beginPath(); x.moveTo(0, -s * 0.48); x.bezierCurveTo(s * 0.18, -s * 0.2, s * 0.36, 0, s * 0.36, s * 0.16);
        x.arc(0, s * 0.16, s * 0.36, 0, P); x.bezierCurveTo(-s * 0.36, 0, -s * 0.18, -s * 0.2, 0, -s * 0.48); fs(); break;
      case DAKU_MAIN + 7:
        for (let i = 0; i < 4; i++) { const a = P / 4 + i * P / 2; x.beginPath(); x.arc(Math.cos(a) * s * 0.19, Math.sin(a) * s * 0.19 - s * 0.04, s * 0.19, 0, P * 2); fs(); }
        x.beginPath(); x.moveTo(0, s * 0.1); x.quadraticCurveTo(s * 0.06, s * 0.32, s * 0.2, s * 0.46); x.stroke(); break;
    }
  }
  // 스티커 한 장을 그린다(장치 픽셀). k: {kind, s(CSS px), bodyH(CSS px), tilt, color, flip, hue, frame}
  function dakuSticker(masks, k, dpr) {
    if (k.img && k.dpr === dpr) return k.img;
    const S = Math.max(2, Math.round(k.s * dpr)), body = k.bodyH * dpr;
    const b = Math.max(1, Math.round(Math.max(1, Math.round(body * DAKU.outline)) * clamp(S / (body * 0.6), 0.5, 1)));
    const pad = Math.ceil(S * 0.9 + b * 3 + 4), art = mk(pad * 2, pad * 2), x = art.getContext('2d');
    x.translate(pad, pad); x.rotate(k.tilt);
    if (k.kind >= DAKU_CAT && k.kind < DAKU_MAIN) {
      // 고양이 자세 스티커: 파스텔 몸 + 눈. 키를 s로 맞추면 고양이는 옆으로 길어서 조금 줄인다.
      const h = Math.max(2, Math.round(S * 0.85)), f = catFrame(masks, k.kind - DAKU_CAT, k.color, h), w = f.width;
      if (k.flip) x.scale(-1, 1);
      x.drawImage(f, -w / 2, -h / 2);
    } else dakuShape(x, k.kind, S, k.color, k.hue);
    k.img = dakuStickerize(art, b); k.dpr = dpr; k.b = b;
    return k.img;
  }
  // 몸: 고른 색 + 눈에 흰 테두리·그림자. 프레임 다섯 장을 만들어 둔다.
  function dakuFrames(masks, color, devH) {
    const b = Math.max(1, Math.round(devH * DAKU.outline)), pad = b + Math.max(1, Math.round(b * 0.7)) + Math.ceil(b * 1.2) + 2;
    return masks.map((_, i) => {
      const f = catFrame(masks, i, color, devH), art = mk(f.width + pad * 2, f.height + pad * 2);
      art.getContext('2d').drawImage(f, pad, pad);
      return { c: dakuStickerize(art, b), pad };
    });
  }
  // 붙은 스티커 한 장. 붙는 순간 톡 눌리고, 떨어질 때 귀퉁이부터 접혀 넘어간 뒤 툭 떨어진다.
  function drawDakuSticker(ctx, masks, k, t, floor, dpr) {
    if (t < k.popAt) return;
    const img = dakuSticker(masks, k, dpr), W = img.width / dpr, H = img.height / dpr, cx = k.x, cy = floor - k.y;
    ctx.save();
    ctx.translate(cx, cy);
    if (k.goneAt !== undefined && t >= k.goneAt) {
      const q = t - k.goneAt, fold = DAKU.peel * DAKU.fold, drop = DAKU.peel * DAKU.drop;
      if (k.peelAngle === undefined) k.peelAngle = dRand(0, Math.PI * 2);
      const ux = Math.cos(k.peelAngle), uy = Math.sin(k.peelAngle), R = k.s / 2 + (k.b + 1) / dpr;
      const e = Math.min(1, q / fold), d = R * (1 - 1.55 * e * e * (3 - 2 * e));
      if (q > fold) {
        const g = Math.min(1, (q - fold) / drop), body = k.bodyH;
        ctx.translate(ux * body * 0.15 * g, -body * 0.25 * g + body * 1.6 * g * g);
        ctx.rotate(k.spin * 2.2 * g); ctx.scale(1 - 0.25 * g, 1 - 0.25 * g);
        ctx.globalAlpha = 1 - g;
      }
      const half = (c, from, to) => {
        const vx = -uy, vy = ux, far = Math.max(W, H) * 3;
        c.beginPath();
        c.moveTo(ux * from + vx * far, uy * from + vy * far); c.lineTo(ux * from - vx * far, uy * from - vy * far);
        c.lineTo(ux * to - vx * far, uy * to - vy * far); c.lineTo(ux * to + vx * far, uy * to + vy * far);
        c.closePath(); c.clip();
      };
      const far = Math.max(W, H) * 3;
      ctx.save(); half(ctx, d - far, d); ctx.drawImage(img, -W / 2, -H / 2, W, H); ctx.restore();
      ctx.save(); half(ctx, d - far, d);
      ctx.transform(1 - 2 * ux * ux, -2 * ux * uy, -2 * ux * uy, 1 - 2 * uy * uy, 2 * d * ux, 2 * d * uy);
      half(ctx, d, d + far);
      ctx.drawImage(img.back, -W / 2, -H / 2, W, H);
      const band = W / 16;
      for (let i = 0; i < 4; i++) {
        ctx.save(); half(ctx, d + band * i, d + band * (i + 1));
        ctx.globalAlpha *= 0.18 * (1 - (i + 0.5) / 4); ctx.drawImage(img.shade, -W / 2, -H / 2, W, H);
        ctx.restore();
      }
      ctx.restore();
    } else {
      const p = Math.min(1, (t - k.popAt) / DAKU.pop);
      const sx = p < 0.5 ? 1.25 - 0.13 * (p / 0.5) : 1.12 - 0.12 * ((p - 0.5) / 0.5);
      const sy = p < 0.5 ? 1.25 - 0.4 * (p / 0.5) : 0.85 + 0.15 * ((p - 0.5) / 0.5);
      ctx.globalAlpha = Math.min(1, p * 4); ctx.scale(sx, sy);
      ctx.drawImage(img, -W / 2, -H / 2, W, H);
    }
    ctx.restore();
  }
  // 무대 하나의 스티커 판. step(dt, cat{x 몸 왼쪽, dir, w, h, p}, left, right)로 진행하고 draw로 그린다.
  function makeDaku() {
    const list = [];
    let t = 0, next = DAKU.first, lastH = 0;
    const vanish = DAKU.peel * (DAKU.fold + DAKU.drop);
    function sticker(cat, kind, s) {
      return { kind, s, bodyH: cat.h, tilt: dRand(-DAKU.tilt, DAKU.tilt) * Math.PI / 180, color: dakuPastel(dRand(0, 360)),
        flip: Math.random() < 0.5, hue: dRand(0, 360), spin: dRand(-1, 1), born: t };
    }
    function place(cat, left, right) {
      const s = cat.h * dRand(DAKU.mainMin, DAKU.mainMax), x = cat.x + cat.w / 2 - cat.dir * cat.w * 0.7;
      if (x < left + s * 0.6 || x > right - s * 0.6) return false;
      if (list.some(k => k.main && Math.abs(k.x - x) < DAKU.gap * cat.w)) return false;
      const main = Object.assign(sticker(cat, Math.floor(Math.random() * DAKU_MAIN), s), { x, y: dRand(s * 0.5, Math.max(s * 0.5, cat.h - s * 0.5)), popAt: t, main: true });
      list.push(main);
      DAKU_LIVE.size = s / cat.h;
      // 작은 스티커는 큰 스티커 둘레에만 0~3장, 하나씩 톡톡 뒤따라 붙는다
      const want = Math.floor(Math.random() * (DAKU.smallCount + 1));
      for (let tries = 0, n = 0; tries < want * 30 && n < want; tries++) {
        const ss = cat.h * dRand(DAKU.smallMin, DAKU.smallMax), r = ss / 2, mr = s / 2;
        const a = dRand(0, Math.PI * 2), d = lerp(mr * 0.6, mr + r + cat.h * DAKU.spread, Math.sqrt(Math.random()));
        const sx = main.x + Math.cos(a) * d * 1.6, sy = main.y - Math.sin(a) * d;
        if (sx < left + r || sx > right - r || sy < r || sy > cat.h - r) continue;
        if (list.some(k => k !== main && Math.hypot(k.x - sx, k.y - sy) < k.s / 2 + r + cat.h * 0.12)) continue;
        n++;
        list.push(Object.assign(sticker(cat, DAKU_MAIN + Math.floor(Math.random() * DAKU_SMALL), ss), { x: sx, y: sy, born: main.born, popAt: t + 0.09 * n, order: n }));
      }
      return true;
    }
    return {
      list,
      get t() { return t; },
      step(dt, cat, left, right) {
        t += dt;
        // 크기가 바뀌면(창 크기) 앱처럼 우수수 뗀다
        if (lastH && Math.abs(lastH - cat.h) > 0.5) for (const k of list) if (k.goneAt === undefined) k.goneAt = t + dRand(0, 0.5);
        lastH = cat.h;
        if (t >= next) next = t + (place(cat, left, right) ? lerp(DAKU.slow, DAKU.fast, cat.p) * dRand(0.7, 1.3) : 0.5);
        for (const k of list) if (k.goneAt === undefined && t - k.born >= DAKU.life) k.goneAt = t + (k.main ? 0 : 0.12 * k.order);
        for (let i = list.length - 1; i >= 0; i--) if (list[i].goneAt !== undefined && t - list[i].goneAt >= vanish) list.splice(i, 1);
        DAKU_LIVE.n = list.filter(k => k.main && k.goneAt === undefined).length;
        DAKU_LIVE.max = Math.floor((right - left) / (DAKU.gap * cat.w)) + 1;
      },
      draw(ctx, masks, floor, dpr) { for (const k of list) drawDakuSticker(ctx, masks, k, t, floor, dpr); },
    };
  }
  // 멈춘 한 장(공유 이미지·썸네일): 몸 뒤로 정해 둔 스티커들. x: 몸 왼쪽, y: 몸 위, 오른쪽을 보고 달린다.
  const DAKU_STILL = [
    { kind: 0, dx: -1.15, y: 0.55, s: 0.62, tilt: -0.25, color: '#a9d8ff' },
    { kind: DAKU_MAIN, dx: -0.72, y: 0.92, s: 0.28, tilt: 0.2, color: '#fff3a0' },
    { kind: 10, dx: -2.35, y: 0.42, s: 0.8, tilt: 0.18, color: '#c8f5c0', flip: true },
    { kind: DAKU_MAIN + 1, dx: -1.75, y: 0.86, s: 0.24, tilt: 0, color: '#ffc7a8' },
    { kind: 1, dx: -3.55, y: 0.5, s: 0.66, tilt: -0.12, color: '#ffb0d0' },
    { kind: DAKU_MAIN + 7, dx: -3.05, y: 0.22, s: 0.3, tilt: 0.3, color: '#d9c8ff' },
  ];
  function drawDakuStill(ctx, masks, x, y, catW, catH, devH, dpr, count = DAKU_STILL.length) {
    const floor = y + catH;
    for (const d of DAKU_STILL.slice(0, count)) {
      const k = d.k || (d.k = { kind: d.kind, s: catH * d.s, bodyH: catH, tilt: d.tilt, color: d.color, flip: !!d.flip, hue: 200, popAt: -1, spin: 0 });
      if (k.bodyH !== catH) Object.assign(k, { s: catH * d.s, bodyH: catH, img: null });
      k.x = x + catW / 2 + d.dx * catW; k.y = d.y * catH;
      drawDakuSticker(ctx, masks, k, 1, floor, dpr);
    }
    if (!DAKU_STILL.frames || DAKU_STILL.devH !== devH) { DAKU_STILL.frames = dakuFrames(masks, DAKU_BODY, devH); DAKU_STILL.devH = devH; }
    const f = DAKU_STILL.frames[1], pad = f.pad / dpr;
    ctx.drawImage(f.c, x - pad, y - pad, f.c.width / dpr, f.c.height / dpr);
  }

  const SKINS = {
    ghost: {
      make(masks) {
        let frames = null, fh = 0;
        return (ctx, g) => {
          if (!frames || fh !== g.devH) { frames = ghostFrames(masks, g.devH); fh = g.devH; }
          const f = frames[Math.floor(g.runner.frame)], pad = f.pad / g.dpr;
          const lift = g.catH * ghostLift(g.t);
          drawFlipped(ctx, f.c, g.runner.x - pad, g.y - lift - pad, f.c.width / g.dpr, f.c.height / g.dpr, g.runner.dir);
        };
      },
      stats: (u, s) => {
        const lift = ghostLift(s.t);
        return {
          spd: [spd(u), pct(spd(u))],
          float: [lift / (GHOST.float + GHOST.bob), '×' + lift.toFixed(2)],
          sheer: [1 - GHOST.bodyOpacity, pct(1 - GHOST.bodyOpacity)],
          glow: [GHOST.glowOpacity, pct(GHOST.glowOpacity)],
        };
      },
    },

    flash: {
      make(masks) {
        return (ctx, g) => {
          const { u, runner, trail, catW, catH, devH, y, dpr } = g;
          const body = flashBody(FLASH_BODY, u);
          // 지나온 길을 따라 d만큼 뒤에 있던 자리 (앱의 RunnerInstance.Behind)
          const behind = d => {
            let acc = 0;
            for (let i = 1; i < trail.length; i++) {
              acc += Math.abs(trail[i].x - trail[i - 1].x);
              if (acc >= d) return trail[i];
            }
            return null;
          };
          // 앱과 같은 순서: 불꽃 줄기 → 잔상(꼬리 끝부터 몸 쪽으로) → 몸
          drawWisps(ctx, runner.x, y, catW, catH, runner.dir, u, g.t, 0.7, body, dpr);
          const tail = catW * flashTail(u);
          for (let i = FLASH.afterimages; i >= 1; i--) {
            const q = i / FLASH.afterimages, s = behind(tail * q);
            if (!s) continue;
            ctx.globalAlpha = FLASH.firstOpacity * flashStrength(u) * Math.pow(1 - (i - 1) / FLASH.afterimages, 1.3);
            drawFlipped(ctx, silFrame(masks, s.f, trailColor(q, body), devH), s.x, y, catW, catH, s.dir);
          }
          ctx.globalAlpha = 1;
          drawFlipped(ctx, catFrame(masks, Math.floor(runner.frame), body, devH), runner.x, y, catW, catH, runner.dir);
        };
      },
      stats: u => {
        const n = Math.round(heatOf(u) * FLASH.heatSteps);
        return {
          spd: [spd(u), pct(spd(u))],
          tail: [flashTail(u) / FLASH.trailLength, '×' + flashTail(u).toFixed(2)],
          fire: [flashStrength(u), pct(flashStrength(u))],
          heat: [heatOf(u), `${n}/${FLASH.heatSteps}`],
        };
      },
    },

    glitch: {
      make(masks) {
        const st = { until: 0, shape: null };
        return (ctx, g) => {
          const { u, runner, catW, catH, devH, y, dpr, t, dt } = g;
          let bursting = t < st.until;
          if (!bursting && !reduceMotion) {
            st.shape = null;
            if (Math.random() < glitchRate(u) * dt) { st.until = t + GLITCH.burst * rand(0.6, 1.4); bursting = true; }
          }
          if (bursting && (!st.shape || Math.random() < GLITCH.reroll)) st.shape = glitchShape(u);
          drawGlitch(ctx, masks, bursting ? st.shape : null, g);
        };
      },
      stats: u => {
        const rate = glitchRate(u), power = glitchPower(u), n = Math.round(GLITCH.noise * power);
        return {
          spd: [spd(u), pct(spd(u))],
          rate: [rate / GLITCH.rateFull, rate.toFixed(1) + '/s'],
          power: [power, pct(power)],
          noise: [n / GLITCH.noise, `${n}/${GLITCH.noise}`],
        };
      },
    },

    yarn: {
      pair: true, // 1P·2P를 한 번에 그린다. 두 마리면 털실 하나를 같이 쓰고, 한 마리면 제 털실을 찬다.
      make(masks) {
        const play = makeYarn(), players = [{}, {}];
        return (ctx, g) => {
          const { cats, catW, catH, devH, y, dpr, dt, w } = g;
          // 2P(메모리 고양이)는 회색. 둘 다 무채색이라 털실은 빨강.
          const color = yarnColor(cats.length > 1 ? [YARN_BODY, YARN_MATE] : [YARN_BODY]);
          const on = cats.map((c, i) => Object.assign(players[i], { x: c.runner.x, dir: c.runner.dir, w: catW, h: catH, v: c.v, p: yarnPower(c.u) }));
          play.step(reduceMotion ? 0 : dt, on, 0, w);
          const floor = y + catH;
          drawYarnBack(ctx, play, color, floor, dpr);
          for (let i = cats.length - 1; i >= 0; i--) {
            const r = cats[i].runner;
            drawFlipped(ctx, catFrame(masks, Math.floor(r.frame), i ? YARN_MATE : YARN_BODY, devH), r.x, y, catW, catH, r.dir);
          }
          drawYarnFront(ctx, play, color, floor, dpr);
        };
      },
      // 능력치는 앱 작업표시줄 값으로 센다(굴러가는 거리 몸 0.5~3.2개)
      stats: u => {
        const p = yarnPower(u), roll = yarnGap(p), hop = yarnHop(p) / YARN.hopHard;
        return {
          spd: [spd(u), pct(spd(u))],
          kick: [p, pct(p)],
          roll: [roll / YARN.hardGap, '×' + roll.toFixed(1)],
          hop: [hop, pct(hop)],
        };
      },
    },

    daku: {
      make(masks) {
        const board = makeDaku();
        let frames = null, fh = 0;
        return (ctx, g) => {
          const { u, runner, catW, catH, devH, y, dpr, dt, w } = g;
          if (!frames || fh !== devH) { frames = dakuFrames(masks, DAKU_BODY, devH); fh = devH; }
          board.step(reduceMotion ? 0 : dt, { x: runner.x, dir: runner.dir, w: catW, h: catH, p: dakuPower(u) }, 0, w);
          board.draw(ctx, masks, y + catH, dpr); // 스티커는 몸 밑에 깔린다
          const f = frames[Math.floor(runner.frame)], pad = f.pad / dpr;
          drawFlipped(ctx, f.c, runner.x - pad, y - pad, f.c.width / dpr, f.c.height / dpr, runner.dir);
        };
      },
      // 붙이는 간격은 앱 작업표시줄 값(12~3초)으로 센다. 꾸밈은 지금 무대에 붙은 큰 스티커 수, 크기는 마지막에 붙인 큰 스티커.
      stats: u => {
        const p = dakuPower(u), every = lerp(DAKU.appSlow, DAKU.appFast, p);
        return {
          spd: [spd(u), pct(spd(u))],
          stick: [p, every.toFixed(1) + 's'],
          deco: [DAKU_LIVE.n / Math.max(1, DAKU_LIVE.max), `${DAKU_LIVE.n}/${DAKU_LIVE.max}`],
          size: [DAKU_LIVE.size / DAKU.mainMax, pct(DAKU_LIVE.size)],
        };
      },
    },
  };

  // 글리치 한 프레임. shape가 null이면 평소 모습(옅은 번짐), 있으면 터진 모습.
  function drawGlitch(ctx, masks, shape, g) {
    const { runner, catW, catH, devH, y, dpr } = g;
    // 앱의 갈라짐 거리는 화면 단위라서, 무대에서 크게 그린 만큼 키운다
    const unit = catH / 30;
    const f = Math.floor(runner.frame);
    const pad = catW * 0.45;
    const off = mk((catW + pad * 2) * dpr, (catH + pad * 2) * dpr), ox = off.getContext('2d');
    ox.setTransform(dpr, 0, 0, dpr, 0, 0);
    const sp = (shape ? shape.split * shape.side : 1) * unit;
    ox.globalAlpha = shape ? GLITCH.splitOpacity : GLITCH.idleFringe;
    drawFlipped(ox, silFrame(masks, f, GLITCH.a, devH), pad - sp, pad, catW, catH, runner.dir);
    drawFlipped(ox, silFrame(masks, f, GLITCH.b, devH), pad + sp, pad, catW, catH, runner.dir);
    ox.globalAlpha = 1;
    drawFlipped(ox, catFrame(masks, f, GLITCH.body, devH), pad, pad, catW, catH, runner.dir);
    let img = off;
    if (shape) {
      img = mk(off.width, off.height);
      const qx = img.getContext('2d');
      qx.drawImage(off, 0, 0);
      for (const b of shape.bands) {
        const sy = Math.round((pad + b.top * catH) * dpr), sh = Math.max(1, Math.round(b.height * catH * dpr));
        qx.clearRect(0, sy, img.width, sh);
        qx.drawImage(off, 0, sy, off.width, sh, Math.round(b.shift * catW * dpr), sy, off.width, sh);
      }
      qx.globalAlpha = GLITCH.noiseOpacity;
      for (const n of shape.noise) {
        qx.fillStyle = n.c;
        qx.fillRect((pad + n.x * catW) * dpr, (pad + n.y * catH) * dpr, n.w * catW * dpr, n.h * catH * dpr);
      }
    }
    ctx.drawImage(img, runner.x - pad, y - pad, catW + pad * 2, catH + pad * 2);
  }

  // 공유 미리보기 이미지(app/tools/cat_og.py)용 멈춘 한 장. 앱과 같은 효과를 가장 잘 보이는 순간으로 그린다.
  // x: 몸 왼쪽, base: 발이 닿는 높이, catH: 키(CSS 픽셀), 오른쪽을 보고 달린다.
  const POSTER = {
    ghost: { u: 50, t: 0.2 },
    flash: { u: 48 }, // 불꽃이 가장 세지만 아직 달아오르기 전(하얀 몸이 잘 보인다)
    yarn: { u: 75 }, // 세게 차서 털실이 막 떠오른 순간
    daku: { u: 60 }, // 지나온 자리에 큰 스티커 셋과 작은 스티커 셋
    glitch: {
      u: 80,
      shape: {
        split: 2.4, side: 1,
        bands: [{ top: 0.16, height: 0.12, shift: 0.13 }, { top: 0.48, height: 0.09, shift: -0.11 }, { top: 0.74, height: 0.13, shift: 0.07 }],
        noise: [
          { x: -0.26, y: 0.3, w: 0.22, h: 0.05, c: GLITCH.a }, { x: 1.04, y: 0.55, w: 0.17, h: 0.045, c: GLITCH.b },
          { x: 0.28, y: 0.04, w: 0.12, h: 0.04, c: '#FFFFFF' }, { x: 0.78, y: 0.92, w: 0.2, h: 0.05, c: GLITCH.a },
        ],
      },
    },
  };
  function drawPoster(ctx, kind, masks, { x, base, catH, dpr = 1, count }) {
    const p = POSTER[kind];
    const devH = Math.round(catH * dpr), catW = catH * masks[0].w / masks[0].h;
    const y = base - catH + Math.max(2, catH * 0.05);
    const runner = { x, dir: 1, frame: 1 };
    // 플래시 꼬리는 지나온 길이 필요하다: 뒤로 3px마다 한 자리, 다리 모양도 조금씩 다르게
    const trail = Array.from({ length: 240 }, (_, i) => ({ x: x - i * 3, f: ((1 - Math.floor(i / 8)) % 5 + 5) % 5, dir: 1 }));
    const g = { u: p.u, t: p.t || 0.35, dt: 0, runner, trail, catW, catH, devH, dpr, y };
    if (kind === 'glitch') drawGlitch(ctx, masks, p.shape, g);
    else if (kind === 'yarn') drawYarnStill(ctx, masks, x, y, catW, catH, devH, dpr, yarnPower(p.u));
    else if (kind === 'daku') drawDakuStill(ctx, masks, x, y, catW, catH, devH, dpr, count); // count: 붙인 스티커 수(도감 그림은 자리가 좁다)
    else SKINS[kind].make(masks)(ctx, g);
  }

  // 목록·카드의 작은 그림 (멈춘 한 장)
  function drawThumb(canvas, kind, masks) {
    const { ctx, w, h, dpr } = fitCanvas(canvas);
    if (w < 2) return;
    ctx.clearRect(0, 0, w, h);
    const aspect = masks[0].w / masks[0].h;
    const catH = Math.min(h * 0.6, (w * 0.62) / aspect), devH = Math.round(catH * dpr), catW = catH * aspect;
    const x = (w - catW) / 2;
    let y = h - catH - h * 0.14;
    if (kind === 'ghost') {
      y -= catH * GHOST.float;
      ctx.save();
      ctx.shadowColor = 'rgba(221,235,255,.9)'; ctx.shadowBlur = catH * 0.3; ctx.globalAlpha = 0.6;
      ctx.drawImage(catFrame(masks, 1, GHOST.body, devH), x, y, catW, catH);
      ctx.restore();
    } else if (kind === 'flash') {
      for (let i = 3; i >= 1; i--) {
        ctx.globalAlpha = 0.5 * (1 - (i - 1) / 3);
        ctx.drawImage(silFrame(masks, 1, trailColor(i / 3, FLASH_BODY), devH), x - i * catW * 0.22, y, catW, catH);
      }
      ctx.globalAlpha = 1;
      ctx.drawImage(catFrame(masks, 1, FLASH_BODY, devH), x, y, catW, catH);
    } else if (kind === 'yarn') {
      // 털실이 앞에 오니 고양이를 조금 왼쪽에 둔다
      drawYarnStill(ctx, masks, x - catW * 0.22, y, catW, catH, devH, dpr, 0.55);
    } else if (kind === 'daku') {
      // 스티커가 뒤에 붙으니 고양이를 조금 오른쪽에 둔다
      drawDakuStill(ctx, masks, x + catW * 0.3, y, catW, catH, devH, dpr, 2);
    } else if (kind === 'glitch') {
      const s = Math.max(1.5, catH / 22);
      ctx.globalAlpha = 0.8;
      ctx.drawImage(silFrame(masks, 1, GLITCH.a, devH), x - s, y, catW, catH);
      ctx.drawImage(silFrame(masks, 1, GLITCH.b, devH), x + s, y, catW, catH);
      ctx.globalAlpha = 1;
      ctx.drawImage(catFrame(masks, 1, GLITCH.body, devH), x, y, catW, catH);
    } else {
      // 아직 공개하지 않은 고양이: 실루엣과 물음표
      ctx.drawImage(silFrame(masks, 1, '#3a3a46', devH), x, y, catW, catH);
      ctx.fillStyle = '#c9c4bb';
      ctx.font = `800 ${Math.round(catH * 0.45)}px system-ui, sans-serif`;
      ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
      ctx.fillText('?', x + catW * 0.5, y + catH * 0.45);
    }
  }

  // ── 스페셜 스킨 끝 ──

  /* ───────── 무대 ───────── */
  // 1P는 CPU 막대대로, 2P(메모리 고양이)는 반대쪽에서 늘 이 사용률로 달린다. 둘 다 같은 스킨을 입는다.
  // 2P 단추는 두 마리가 주고받는 고양이(cat_pages.py의 duo) 페이지에만 있다. 나머지는 늘 한 마리.
  const MATE_USAGE = 45;
  function makeStage(canvas, skin, masks) {
    const pairDraw = skin.pair ? skin.make(masks) : null;
    const cats = [0, 1].map(() => ({ runner: new Runner(40, 1), trail: [], draw: pairDraw ? null : skin.make(masks), u: 0, v: 0 }));
    let t = 0, scroll = 0, matePlaced = false;
    return (dt, u, two) => {
      t += dt;
      const { ctx, w, h, dpr } = fitCanvas(canvas);
      if (w < 2 || h < 2) return t;
      const floorH = Math.round(h * 0.3);
      const catH = clamp(w * 0.1, 46, 74), devH = Math.round(catH * dpr);
      const catW = catH * masks[0].w / masks[0].h;
      scroll += dt * (0.4 + 1.6 * u / 100);
      drawArcade(ctx, w, h, floorH, t, scroll);
      if (!matePlaced) { cats[1].runner.x = w - catW - 40; cats[1].runner.dir = -1; matePlaced = true; }
      const on = two ? cats : cats.slice(0, 1);
      on.forEach((c, i) => {
        c.u = i ? MATE_USAGE : u;
        c.v = speedFor(c.u / 100) * 1.15 * (catH / 40);
        if (!reduceMotion) c.runner.step(dt, c.v, fpsFor(c.v * 40 / catH), 8, w - catW - 8);
        c.trail.unshift({ x: c.runner.x, f: Math.floor(c.runner.frame), dir: c.runner.dir });
        if (c.trail.length > 160) c.trail.length = 160;
      });
      const y = h - floorH - catH + Math.max(2, catH * 0.05);
      const g = { t, dt, catW, catH, devH, dpr, y, w };
      if (pairDraw) pairDraw(ctx, { ...g, cats: on });
      // 1P가 앞에 오게 2P부터 그린다
      else for (let i = on.length - 1; i >= 0; i--) on[i].draw(ctx, { ...g, u: on[i].u, runner: on[i].runner, trail: on[i].trail });
      return t;
    };
  }

  /* ───────── 1P·2P: 1P를 누르면 2P(메모리 고양이)가 나왔다 들어가고, 떠 있는 2P를 눌러도 들어간다. 처음엔 두 마리 ───────── */
  function makePlayers() {
    const btns = [$('#p1'), $('#p2')].filter(Boolean), s = { two: true };
    try { s.two = localStorage.getItem('woodada-players') !== '1'; } catch (e) { /* 못 읽으면 두 마리 */ }
    const sync = () => {
      document.body.dataset.players = s.two ? '2' : '1';
      btns.forEach(b => b.setAttribute('aria-pressed', String(s.two)));
    };
    btns.forEach(b => b.addEventListener('click', () => {
      s.two = !s.two;
      sync();
      try { localStorage.setItem('woodada-players', s.two ? '2' : '1'); } catch (e) { /* 저장 못 해도 화면은 바뀐다 */ }
    }));
    sync();
    return s;
  }
  const players = $('#p1') ? makePlayers() : { two: false };

  /* ───────── CPU 막대: 처음엔 저절로 오르내리다가, 만지면 멈춘다 ───────── */
  function makeCtl() {
    const r = $('#cpu'), out = $('#cpu-out'), btn = $('#auto');
    const s = { u: 70, auto: !reduceMotion, t: 1.6 };
    s.sync = () => {
      out.textContent = Math.round(s.u) + '%';
      btn.setAttribute('aria-pressed', String(s.auto));
      btn.textContent = s.auto ? 'AUTO ON' : 'AUTO OFF';
    };
    r.addEventListener('input', () => { s.u = +r.value; s.auto = false; s.sync(); });
    btn.addEventListener('click', () => { s.auto = !s.auto; s.sync(); });
    s.tick = dt => {
      if (!s.auto) return;
      s.t += dt;
      s.u = 50 - 47 * Math.cos(s.t * 0.42);
      r.value = String(Math.round(s.u));
    };
    s.sync();
    return s;
  }

  function updateStats(rows, values) {
    rows.forEach(({ el, segs, out }) => {
      const v = values[el.dataset.stat];
      if (!v) return;
      const k = Math.round(clamp(v[0], 0, 1) * segs.length);
      segs.forEach((c, i) => c.classList.toggle('on', i < k));
      out.textContent = v[1];
    });
  }

  /* ───────── 시작 ───────── */
  const masksReady = loadMasks();
  // 공유 미리보기 이미지를 그리는 도구(app/tools/cat_og.py)가 쓴다
  window.WoodadaCats = { masks: masksReady, drawArcade, drawPoster };

  masksReady.then(masks => {
    const thumbs = () => $$('canvas[data-thumb]').forEach(c => drawThumb(c, c.dataset.thumb, masks));
    thumbs();
    let resizeT = 0;
    addEventListener('resize', () => { clearTimeout(resizeT); resizeT = setTimeout(thumbs, 120); });

    const canvas = $('.js-stage');
    const skin = SKINS[document.body.dataset.cat];
    if (!canvas || !skin) return;

    const stage = makeStage(canvas, skin, masks);
    const ctl = makeCtl();
    const rows = $$('[data-stat]').map(el => ({ el, segs: $$('.seg i', el), out: $('output', el) }));
    let visible = true, lastT = 0, uiT = 0, t = 0;

    function frame(now) {
      const dt = lastT ? Math.min(0.05, (now - lastT) / 1000) : 0;
      lastT = now;
      ctl.tick(dt);
      t = stage(dt, ctl.u, players.two);
      if (now - uiT > 90) { uiT = now; ctl.sync(); updateStats(rows, skin.stats(ctl.u, { t })); }
      if (visible && !document.hidden) requestAnimationFrame(frame);
      else lastT = 0;
    }
    const start = () => { lastT = 0; requestAnimationFrame(frame); };
    if ('IntersectionObserver' in window) {
      new IntersectionObserver(es => es.forEach(e => { const was = visible; visible = e.isIntersecting; if (visible && !was) start(); })).observe(canvas);
    }
    document.addEventListener('visibilitychange', () => { if (!document.hidden && visible) start(); });
    start();
  }).catch(err => console.error('고양이 그림을 불러오지 못했어요', err));
})();
