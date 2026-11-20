(function () {
  let cv = null, ctx = null, W = 0, H = 0, dpr = 1;
  let list = [];
  const motion = new Map();
  const pads = [];
  let t = 0, last = 0, quackCb = null;

  const SCALE = { duckling: 0.74, young: 0.88, adult: 1.0 };
  const BASE = 1.9;
  const COLORS = {
    classic: '#F6C445', strawhat: '#F4C24B', swimring: '#F7CE62', sunglasses: '#EFC45C',
    golden: '#F7CB4E', marathon: '#EFA852', earlybird: '#EFD98C', nightowl: '#D7D0EE'
  };

  function init(canvas) {
    cv = canvas;
    ctx = cv.getContext('2d');
    resize();
    window.addEventListener('resize', resize);
    if (window.ResizeObserver) new ResizeObserver(resize).observe(cv);
    for (let i = 0; i < 5; i++) {
      pads.push({ x: 0.08 + Math.random() * 0.84, y: 0.64 + Math.random() * 0.3, r: 14 + Math.random() * 16, ph: Math.random() * 6 });
    }
    cv.addEventListener('click', onClick);
    requestAnimationFrame(frame);
    setTimeout(resize, 80);
    setTimeout(resize, 300);
  }

  function resize() {
    if (!cv) return;
    const rect = cv.getBoundingClientRect();
    dpr = Math.min(2, window.devicePixelRatio || 1);
    W = Math.max(320, rect.width);
    H = Math.max(200, rect.height);
    cv.width = Math.floor(W * dpr);
    cv.height = Math.floor(H * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    for (const m of motion.values()) {
      m.x = Math.min(Math.max(m.x, 70), W - 70);
      m.y = Math.min(Math.max(m.y, 96), H - 76);
    }
  }

  function setDucks(ducks) {
    list = ducks || [];
    const ids = new Set(list.map(d => d.id));
    for (const k of Array.from(motion.keys())) if (!ids.has(k)) motion.delete(k);
    for (const d of list) {
      if (!motion.has(d.id)) {
        motion.set(d.id, {
          x: 70 + Math.random() * Math.max(40, W - 140),
          y: H * 0.42 + Math.random() * Math.max(20, H * 0.38),
          vx: (Math.random() < 0.5 ? -1 : 1) * (6 + Math.random() * 16),
          phase: Math.random() * Math.PI * 2,
          bob: Math.random() * 6
        });
      }
    }
  }

  function frame(now) {
    requestAnimationFrame(frame);
    if (!cv || !ctx) return;
    if (!cv.offsetParent) { last = now; return; }
    const dt = last ? Math.min(0.05, (now - last) / 1000) : 0.016;
    last = now;
    t += dt;
    step(dt);
    draw();
  }

  function step(dt) {
    for (const d of list) {
      const m = motion.get(d.id);
      if (!m) continue;
      m.x += m.vx * dt;
      const margin = 78;
      if (m.x < margin) { m.x = margin; m.vx = Math.abs(m.vx); }
      if (m.x > W - margin) { m.x = W - margin; m.vx = -Math.abs(m.vx); }
      m.y += Math.sin(t * 0.55 + m.phase) * 1.4 * dt;
      m.y = Math.max(H * 0.3, Math.min(H - 68, m.y));
      m.dir = m.vx >= 0 ? 1 : -1;
    }
  }

  function draw() {
    ctx.clearRect(0, 0, W, H);

    const g = ctx.createLinearGradient(0, 0, 0, H);
    g.addColorStop(0, '#0D2F26');
    g.addColorStop(0.5, '#0A2119');
    g.addColorStop(1, '#061512');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, W, H);

    for (let i = 0; i < 2; i++) {
      const cx = W * (0.3 + i * 0.4) + Math.sin(t * 0.2 + i) * 26;
      const cy = H * (0.3 + i * 0.32);
      const rg = ctx.createRadialGradient(cx, cy, 10, cx, cy, Math.max(W, H) * 0.62);
      rg.addColorStop(0, 'rgba(132, 226, 198, 0.07)');
      rg.addColorStop(0.5, 'rgba(120, 210, 186, 0.03)');
      rg.addColorStop(1, 'rgba(120, 210, 186, 0)');
      ctx.fillStyle = rg;
      ctx.fillRect(0, 0, W, H);
    }

    ctx.strokeStyle = 'rgba(160, 230, 210, 0.045)';
    ctx.lineWidth = 1;
    for (let i = 0; i < 6; i++) {
      const y0 = 44 + i * (H - 96) / 6;
      ctx.beginPath();
      for (let x = 0; x <= W; x += 12) {
        const y = y0 + Math.sin(x * 0.02 + t * 0.5 + i) * 3;
        if (x === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
      }
      ctx.stroke();
    }

    for (const p of pads) {
      const px = p.x * W, py = p.y * H, bob = Math.sin(t * 0.5 + p.ph) * 2;
      ctx.fillStyle = 'rgba(44, 116, 88, 0.42)';
      ctx.beginPath();
      ctx.ellipse(px, py + bob, p.r, p.r * 0.5, 0, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = 'rgba(74, 158, 116, 0.35)';
      ctx.beginPath();
      ctx.ellipse(px - 3, py + bob - 2, p.r * 0.45, p.r * 0.22, -0.3, 0, Math.PI * 2);
      ctx.fill();
    }

    drawReeds();

    const sorted = list.slice().sort((a, b) => (motion.get(a.id)?.y || 0) - (motion.get(b.id)?.y || 0));
    for (const d of sorted) drawDuck(d);
  }

  function drawReeds() {
    ctx.save();
    ctx.strokeStyle = 'rgba(52, 120, 92, 0.55)';
    ctx.lineWidth = 2;
    const spots = [10, 26, W - 14, W - 30, W - 46];
    for (let i = 0; i < spots.length; i++) {
      const x = spots[i];
      const sway = Math.sin(t * 0.8 + i) * 4;
      ctx.beginPath();
      ctx.moveTo(x, H);
      ctx.quadraticCurveTo(x + sway * 0.5, H - 40, x + sway, H - 76 - (i % 3) * 8);
      ctx.stroke();
    }
    ctx.restore();
  }

  function drawDuck(d) {
    const m = motion.get(d.id);
    if (!m) return;
    const s = (SCALE[d.stage] || 1) * BASE;
    const body = COLORS[d.species] || COLORS.classic;
    const bob = Math.sin(t * 1.4 + m.phase) * 2;

    ctx.save();
    ctx.translate(m.x, m.y + bob);
    ctx.scale(m.dir * s, s);

    ctx.strokeStyle = 'rgba(180, 240, 225, 0.10)';
    ctx.lineWidth = 1.4;
    ctx.beginPath();
    ctx.ellipse(0, 12, 27, 7, 0, 0, Math.PI * 2);
    ctx.stroke();

    if (d.species === 'swimring') {
      ctx.strokeStyle = 'rgba(238, 138, 58, 0.9)';
      ctx.lineWidth = 5;
      ctx.beginPath();
      ctx.ellipse(-2, 6, 30, 9, 0, 0, Math.PI * 2);
      ctx.stroke();
    }

    ctx.fillStyle = body;
    ctx.beginPath();
    ctx.moveTo(-20, -4);
    ctx.quadraticCurveTo(-32, -12, -29, 2);
    ctx.quadraticCurveTo(-25, 8, -14, 8);
    ctx.closePath();
    ctx.fill();

    ctx.beginPath();
    ctx.ellipse(0, 0, 24, 15, 0, 0, Math.PI * 2);
    ctx.fill();

    ctx.fillStyle = shade(body, -14);
    ctx.beginPath();
    ctx.ellipse(-5, 2, 13, 8, -0.22, 0, Math.PI * 2);
    ctx.fill();

    ctx.fillStyle = body;
    ctx.beginPath();
    ctx.ellipse(14, -13, 9.5, 9.5, 0, 0, Math.PI * 2);
    ctx.fill();

    ctx.fillStyle = '#EE8A3A';
    ctx.beginPath();
    ctx.moveTo(22, -15);
    ctx.lineTo(33, -10);
    ctx.lineTo(22, -5);
    ctx.closePath();
    ctx.fill();

    if (d.species === 'sunglasses') {
      ctx.fillStyle = '#26302C';
      ctx.beginPath();
      ctx.roundRect ? ctx.roundRect(11, -18, 11, 5, 2) : ctx.rect(11, -18, 11, 5);
      ctx.fill();
    } else if (d.species === 'golden') {
      ctx.fillStyle = '#2A2005';
      ctx.beginPath();
      ctx.arc(16, -14.5, 2.3, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = 'rgba(255, 252, 220, 0.95)';
      ctx.beginPath();
      ctx.arc(15.2, -15.3, 0.9, 0, Math.PI * 2);
      ctx.fill();
    } else {
      ctx.fillStyle = '#202824';
      ctx.beginPath();
      ctx.arc(16, -14.5, 2.2, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = 'rgba(255,255,255,.85)';
      ctx.beginPath();
      ctx.arc(15.2, -15.3, 0.7, 0, Math.PI * 2);
      ctx.fill();
    }

    if (d.species === 'strawhat') {
      ctx.fillStyle = '#D9B36B';
      ctx.beginPath();
      ctx.ellipse(14, -22, 12, 3.6, -0.05, 0, Math.PI * 2);
      ctx.fill();
      ctx.beginPath();
      ctx.ellipse(14, -25, 7, 4.4, 0, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = 'rgba(160, 120, 60, .8)';
      ctx.fillRect(8, -23.5, 12.5, 1.4);
    }

    if (d.species === 'marathon') {
      ctx.fillStyle = '#E0523E';
      ctx.fillRect(6, -21, 17, 3);
      ctx.fillRect(-6, -21, 7, 3);
    }

    if (d.species === 'nightowl') {
      ctx.fillStyle = 'rgba(226, 218, 255, .85)';
      ctx.beginPath();
      ctx.arc(26, -30, 5, 0.6, Math.PI * 1.9);
      ctx.fill();
    }

    if (d.species === 'earlybird') {
      ctx.fillStyle = 'rgba(255, 220, 130, .75)';
      ctx.beginPath();
      ctx.arc(24, -30, 4, 0, Math.PI * 2);
      ctx.fill();
      ctx.strokeStyle = 'rgba(255, 220, 130, .5)';
      ctx.lineWidth = 1;
      for (let i = 0; i < 6; i++) {
        const a = i * Math.PI / 3;
        ctx.beginPath();
        ctx.moveTo(24 + Math.cos(a) * 6, -30 + Math.sin(a) * 6);
        ctx.lineTo(24 + Math.cos(a) * 9, -30 + Math.sin(a) * 9);
        ctx.stroke();
      }
    }

    if (d.species === 'golden') {
      ctx.fillStyle = 'rgba(255, 240, 170, .55)';
      ctx.beginPath();
      ctx.arc(-2, -5, 9, 0, Math.PI * 2);
      ctx.fill();
    }

    ctx.restore();
  }

  function shade(hex, amt) {
    const n = parseInt(hex.slice(1), 16);
    const r = Math.max(0, Math.min(255, (n >> 16) + amt));
    const g = Math.max(0, Math.min(255, ((n >> 8) & 255) + amt));
    const b = Math.max(0, Math.min(255, (n & 255) + amt));
    return 'rgb(' + r + ',' + g + ',' + b + ')';
  }

  function onClick(e) {
    const rect = cv.getBoundingClientRect();
    const x = e.clientX - rect.left, y = e.clientY - rect.top;
    let hit = null, dist = 1e9;
    for (const d of list) {
      const m = motion.get(d.id);
      if (!m) continue;
      const r = (SCALE[d.stage] || 1) * 62;
      const dd = (x - m.x) * (x - m.x) + (y - m.y) * (y - m.y);
      if (dd < dist && dd < r * r) { dist = dd; hit = d; }
    }
    if (hit && quackCb) quackCb(hit);
  }

  window.Pond = {
    init,
    setDucks,
    onQuack: fn => { quackCb = fn; }
  };
})();
