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
          // 앱의 갈라짐 거리는 화면 단위라서, 무대에서 크게 그린 만큼 키운다
          const unit = catH / 30;
          const f = Math.floor(runner.frame);
          const pad = catW * 0.45;
          const off = mk((catW + pad * 2) * dpr, (catH + pad * 2) * dpr), ox = off.getContext('2d');
          ox.setTransform(dpr, 0, 0, dpr, 0, 0);
          const sp = (bursting ? st.shape.split * st.shape.side : 1) * unit;
          ox.globalAlpha = bursting ? GLITCH.splitOpacity : GLITCH.idleFringe;
          drawFlipped(ox, silFrame(masks, f, GLITCH.a, devH), pad - sp, pad, catW, catH, runner.dir);
          drawFlipped(ox, silFrame(masks, f, GLITCH.b, devH), pad + sp, pad, catW, catH, runner.dir);
          ox.globalAlpha = 1;
          drawFlipped(ox, catFrame(masks, f, GLITCH.body, devH), pad, pad, catW, catH, runner.dir);
          let img = off;
          if (bursting) {
            img = mk(off.width, off.height);
            const qx = img.getContext('2d');
            qx.drawImage(off, 0, 0);
            for (const b of st.shape.bands) {
              const sy = Math.round((pad + b.top * catH) * dpr), sh = Math.max(1, Math.round(b.height * catH * dpr));
              qx.clearRect(0, sy, img.width, sh);
              qx.drawImage(off, 0, sy, off.width, sh, Math.round(b.shift * catW * dpr), sy, off.width, sh);
            }
            qx.globalAlpha = GLITCH.noiseOpacity;
            for (const n of st.shape.noise) {
              qx.fillStyle = n.c;
              qx.fillRect((pad + n.x * catW) * dpr, (pad + n.y * catH) * dpr, n.w * catW * dpr, n.h * catH * dpr);
            }
          }
          ctx.drawImage(img, runner.x - pad, y - pad, catW + pad * 2, catH + pad * 2);
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
  };

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
  function makeStage(canvas, skin, masks) {
    const draw = skin.make(masks);
    const runner = new Runner(40, 1), trail = [];
    let t = 0, scroll = 0;
    return (dt, u) => {
      t += dt;
      const { ctx, w, h, dpr } = fitCanvas(canvas);
      if (w < 2 || h < 2) return t;
      const floorH = Math.round(h * 0.3);
      const catH = clamp(w * 0.1, 46, 74), devH = Math.round(catH * dpr);
      const catW = catH * masks[0].w / masks[0].h;
      const v = speedFor(u / 100) * 1.15 * (catH / 40);
      scroll += dt * (0.4 + 1.6 * u / 100);
      drawArcade(ctx, w, h, floorH, t, scroll);
      if (!reduceMotion) runner.step(dt, v, fpsFor(v * 40 / catH), 8, w - catW - 8);
      trail.unshift({ x: runner.x, f: Math.floor(runner.frame), dir: runner.dir });
      if (trail.length > 160) trail.length = 160;
      const y = h - floorH - catH + Math.max(2, catH * 0.05);
      draw(ctx, { u, t, dt, runner, trail, catW, catH, devH, dpr, y });
      return t;
    };
  }

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
  loadMasks().then(masks => {
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
      t = stage(dt, ctl.u);
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
