(function () {
  const $ = id => document.getElementById(id);
  const post = (cmd, extra) => {
    const msg = Object.assign({ cmd }, extra || {});
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(msg);
  };

  const S = {
    data: null,
    tab: 'focus',
    preset: 25,
    presetPinned: false,
    editing: null,
    live: { remaining: 1500, planned: 1500, at: 0, running: false }
  };

  const SPECIES_EMOJI = {
    classic: '🦆', strawhat: '🦆', swimring: '🦆', sunglasses: '🦆',
    golden: '🦆', marathon: '🦆', earlybird: '🦆', nightowl: '🦆'
  };
  const SPECIES_COLOR = {
    classic: '#F6C445', strawhat: '#D9B36B', swimring: '#EE8A3A', sunglasses: '#8FB2A6',
    golden: '#F6C445', marathon: '#E0523E', earlybird: '#EFD98C', nightowl: '#B9AEE6'
  };
  const POND_NAMES = ['小水洼', '池塘', '芦苇塘', '鸭湖'];

  function fmtDur(sec) {
    sec = Math.max(0, Math.round(sec || 0));
    const h = Math.floor(sec / 3600), m = Math.round((sec % 3600) / 60);
    if (h <= 0) return m + ' 分';
    return h + ' 小时' + (m > 0 ? ' ' + m + ' 分' : '');
  }
  function fmtShort(sec) {
    sec = Math.max(0, Math.round(sec || 0));
    const h = Math.floor(sec / 3600), m = Math.round((sec % 3600) / 60);
    if (h <= 0) return m + '′';
    return h + 'h' + (m ? m + '′' : '');
  }
  function mmss(sec) {
    sec = Math.max(0, Math.round(sec));
    return String(Math.floor(sec / 60)).padStart(2, '0') + ':' + String(sec % 60).padStart(2, '0');
  }
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  /* ---------- 消息 ---------- */
  function onMessage(e) {
    const m = e.data;
    if (!m || !m.type) return;
    if (m.type === 'state') applyState(m.payload);
    else if (m.type === 'celebrate') celebrate(m.payload);
    else if (m.type === 'sound') playSound(m.payload.kind);
  }

  function applyState(p) {
    S.data = p;
    S.live = {
      remaining: p.remaining,
      planned: p.planned || 1,
      at: performance.now(),
      running: !!p.running && p.phase !== 'idle'
    };
    renderTop(p);
    renderFocus(p);
    if (p.phase === 'idle') syncPreset(p);
    if (S.tab === 'pond') renderPond(p);
    if (S.tab === 'board') renderBoard(p);
    if (S.tab === 'settings' && !editingSettings()) renderSettings(p);
    Pond.setDucks(p.ducks || []);
  }

  function editingSettings() {
    const ae = document.activeElement;
    return !!(ae && $('tab-settings').contains(ae) && /^(INPUT|SELECT|TEXTAREA)$/.test(ae.tagName));
  }

  /* ---------- 顶栏 ---------- */
  function renderTop(p) {
    $('chipToday').textContent = fmtShort(p.today.focusSec);
    $('chipStreak').textContent = p.totals.streak + ' 天';
    $('chipDucks').textContent = p.totals.ducks + ' 只';
    const status = $('chipStatus');
    const text = status.querySelector('b');
    status.classList.remove('on', 'rest');
    if (p.phase === 'focus') {
      status.classList.add('on');
      text.textContent = p.running ? '专注中' : '已暂停';
    } else if (p.phase === 'rest') {
      status.classList.add('rest');
      text.textContent = p.running ? '休息中' : '休息暂停';
    } else {
      text.textContent = p.paused ? '提醒已暂停' : '待机中';
    }
  }

  /* ---------- 专注 ---------- */
  function renderFocus(p) {
    const phase = p.phase;
    const ring = $('ringProgress').closest('.ring');
    ring.classList.toggle('rest', phase === 'rest');

    let phaseText = '准备开始';
    if (phase === 'focus') phaseText = p.running ? '专注中' : '已暂停';
    if (phase === 'rest') phaseText = p.running ? '休息中' : '休息暂停';
    $('phaseLabel').textContent = phaseText;

    if (phase === 'idle') {
      $('taskText').textContent = '选个时长，开始专注';
      paintClock(S.preset * 60, S.preset * 60);
    } else {
      $('taskText').textContent = phase === 'focus' && p.task ? p.task : (phase === 'rest' ? p.overlayLabel : '专注中');
      paintClock(p.remaining, p.planned);
    }

    const main = $('btnMain');
    const skip = $('btnSkip');
    const abort = $('btnAbort');
    skip.hidden = phase === 'idle';
    abort.hidden = phase === 'idle';
    if (phase === 'idle') { main.textContent = '开始专注'; }
    else if (p.running) { main.textContent = phase === 'focus' ? '暂停专注' : '暂停休息'; }
    else { main.textContent = '继续'; }
    skip.textContent = phase === 'rest' ? '跳过休息' : '跳过专注';

    const dots = $('cycleDots');
    dots.innerHTML = '';
    const total = Math.max(1, p.settings.cyclesBeforeLong);
    for (let i = 0; i < total; i++) {
      const d = document.createElement('i');
      if (i < p.cycle) d.classList.add('on');
      dots.appendChild(d);
    }

    const goalSec = p.today.goalSec || 1;
    const pct = Math.min(100, Math.round(p.today.focusSec / goalSec * 100));
    $('goalBar').style.width = pct + '%';
    $('goalText').textContent = fmtShort(p.today.focusSec) + ' / ' + (goalSec / 3600).toFixed(0) + ' 小时';
    $('todayDucks').textContent = p.today.focusCount + ' 只';
    $('todayRests').textContent = p.today.restCount + ' 次';
    $('todayEscapes').textContent = p.today.escapes + ' 只';

    if (p.nextRest) {
      const at = String(p.nextRest.at || '');
      const now = new Date();
      const dayKey = now.getFullYear() + '-' + String(now.getMonth() + 1).padStart(2, '0') + '-' + String(now.getDate()).padStart(2, '0');
      const tomorrow = new Date(now.getTime() + 86400000);
      const tomorrowKey = tomorrow.getFullYear() + '-' + String(tomorrow.getMonth() + 1).padStart(2, '0') + '-' + String(tomorrow.getDate()).padStart(2, '0');
      const prefix = at.startsWith(dayKey) ? '' : (at.startsWith(tomorrowKey) ? '明天 ' : '次日 ');
      $('nextRest').textContent = prefix + p.nextRest.time;
      $('nextRestSub').textContent = p.nextRest.label + ' · ' + (p.nextRest.inMin <= 0 ? '即将开始' : (p.nextRest.inMin >= 600 ? '还有 ' + Math.round(p.nextRest.inMin / 60) + ' 小时' : p.nextRest.inMin + ' 分钟后'));
    } else {
      $('nextRest').textContent = '—';
      $('nextRestSub').textContent = '暂无启用的提醒';
    }

    renderTodayStrip(p);
  }

  function renderTodayStrip(p) {
    const items = p.recent || [];
    const strip = $('todayStrip');
    if (!items.length) {
      $('todayStripHint').textContent = '共 0 分';
      strip.innerHTML = '<span class="muted small">坐住 25 分钟，这里就会出现你的第一段节奏。</span>';
      return;
    }
    const todaySec = items.filter(x => x.kind === 'focus' && x.status === 'completed').reduce((a, b) => a + b.sec, 0);
    $('todayStripHint').textContent = '共 ' + fmtDur(todaySec) + ' · ' + items.length + ' 段';
    strip.innerHTML = items.map(x => {
      const cls = 'rec ' + (x.kind === 'focus' ? 'focus' : 'rest') + (x.status === 'completed' ? '' : ' broke');
      const label = x.kind === 'focus' ? '专注' : '休息';
      const mark = x.status === 'completed' ? '' : ' · 中断';
      return '<div class="' + cls + '"><b>' + esc(x.start) + ' – ' + esc(x.end || '…') + '</b>' +
        '<span>' + label + ' ' + Math.round(x.sec / 60) + '′' + mark + (x.task ? ' · ' + esc(x.task) : '') + '</span></div>';
    }).join('');
  }

  function paintClock(remaining, planned) {
    $('timeText').textContent = mmss(remaining);
    const ratio = Math.max(0, Math.min(1, remaining / Math.max(1, planned)));
    $('ringProgress').style.strokeDashoffset = String(565.49 * ratio);
  }

  /* ---------- 鸭塘 ---------- */
  function renderPond(p) {
    const t = p.totals;
    const level = t.pondLevel;
    $('pondName').textContent = t.pondName || POND_NAMES[level];
    $('pondCount').textContent = t.ducks + ' 只鸭';
    $('pondTotalFocus').textContent = fmtDur(t.focusSec);
    $('pondEscapes').textContent = t.escapes + ' 只';

    const nextAt = t.pondNextAt || 0;
    let pct = 100, nextText = '鸭子已经把湖住满了';
    if (nextAt > 0) {
      pct = Math.min(100, Math.round(t.ducks / nextAt * 100));
      const remain = Math.max(0, nextAt - t.ducks);
      const nextName = POND_NAMES[Math.min(3, level + 1)];
      nextText = '再养 ' + remain + ' 只就能升级成「' + nextName + '」';
    }
    $('pondBar').style.width = pct + '%';
    $('pondNext').textContent = nextText;

    $('pondEmpty').hidden = (p.ducks || []).length > 0;

    const book = $('duckbook');
    $('duckbookCount').textContent = t.ducks + ' 只';
    if (!p.ducks || p.ducks.length === 0) {
      book.innerHTML = '<div class="muted small">还没有鸭。完成一次专注，第一只小鸭就会破壳。</div>';
    } else {
      book.innerHTML = p.ducks.slice().reverse().map(d => {
        const color = SPECIES_COLOR[d.species] || '#8FB2A6';
        return '<div class="duck-card">' +
          '<div class="duck-avatar"><img src="duck.png" alt="duck" /></div>' +
          '<div class="duck-meta"><b style="color:' + color + '">' + esc(d.name) + '</b>' +
          '<span>' + esc(d.stageName) + ' · ' + fmtDur(d.focusSec) + '</span>' +
          '<span>' + esc(d.bornAt) + '</span></div></div>';
      }).join('');
    }

    const ghostCard = $('ghostCard');
    const ghosts = p.ghosts || [];
    ghostCard.hidden = ghosts.length === 0;
    $('ghostList').innerHTML = ghosts.map(g =>
      '<div class="ghost-item">🪶 ' + esc(g.reason) + ' · ' + esc(g.bornAt) + '</div>'
    ).join('');
  }

  /* ---------- 看板 ---------- */
  function renderBoard(p) {
    const week = p.week || [];
    const weekSec = week.reduce((a, b) => a + b.focusSec, 0);
    const month = p.month || { days: [], totalSec: 0, escapes: 0 };
    const totals = p.totals;

    $('mToday').textContent = fmtDur(p.today.focusSec);
    $('mTodaySub').textContent = p.today.focusCount + ' 次 · ' + p.today.escapes + ' 只逃鸭';
    $('mWeek').textContent = fmtDur(weekSec);
    $('mWeekSub').textContent = week.reduce((a, b) => a + b.ducks, 0) + ' 只新鸭 · ' + week.reduce((a, b) => a + b.escapes, 0) + ' 只逃';
    $('mStreak').textContent = totals.streak + ' 天';
    $('mTotal').textContent = fmtDur(totals.focusSec);
    $('mTotalSub').textContent = totals.ducks + ' 只鸭 · ' + totals.escapes + ' 只逃 · 休息 ' + fmtShort(totals.restSec);

    const maxWeek = Math.max(1800, ...week.map(w => w.focusSec));
    $('weekBars').innerHTML = week.map((w, i) => {
      const pct = Math.round(w.focusSec / maxWeek * 100);
      const mins = Math.round(w.focusSec / 60);
      return '<div class="bar-col' + (i === week.length - 1 ? ' today' : '') + '">' +
        '<span class="bar-value">' + (mins > 0 ? mins + '′' : '') + '</span>' +
        '<i class="bar-body" style="height:' + Math.max(2, pct) + '%"></i>' +
        '<span class="bar-label">' + esc(w.label) + '</span></div>';
    }).join('');
    $('weekHint').textContent = '总计 ' + fmtDur(weekSec);

    const hours = p.hours || new Array(24).fill(0);
    const maxHour = Math.max(3600, ...hours);
    $('hourBars').innerHTML = hours.map((s, i) => {
      const pct = s > 0 ? Math.max(4, Math.round(s / maxHour * 100)) : 2;
      return '<i title="' + i + ' 点 · ' + Math.round(s / 60) + ' 分钟" style="height:' + pct + '%"></i>';
    }).join('');
    if (!$('hourAxis')) {
      const axis = document.createElement('div');
      axis.className = 'hour-line';
      axis.id = 'hourAxis';
      axis.innerHTML = '<span>0 点</span><span>6 点</span><span>12 点</span><span>18 点</span><span>23 点</span>';
      $('hourBars').parentNode.appendChild(axis);
    }

    const monthLabel = month.year + ' 年 ' + month.month + ' 月 · ' + fmtDur(month.totalSec);
    $('monthTitle').textContent = monthLabel;

    const days = month.days || [];
    const first = new Date(month.year, month.month - 1, 1);
    const offset = (first.getDay() + 6) % 7;
    const today = new Date();
    const isThisMonth = today.getFullYear() === month.year && (today.getMonth() + 1) === month.month;
    let html = ['一', '二', '三', '四', '五', '六', '日'].map(d => '<div class="heat-dow">' + d + '</div>').join('');
    for (let i = 0; i < offset; i++) html += '<div class="heat-cell blank"></div>';
    for (const d of days) {
      const mins = Math.round(d.focusSec / 60);
      let lv = 'lv0';
      if (mins > 0 && mins < 30) lv = 'lv1';
      else if (mins < 60) lv = 'lv2';
      else if (mins < 120) lv = 'lv3';
      else if (mins >= 120) lv = 'lv4';
      if (mins === 0) lv = 'lv0';
      const cls = 'heat-cell ' + lv + (isThisMonth && d.day === today.getDate() ? ' today' : '');
      html += '<div class="' + cls + '" title="' + month.month + '-' + d.day + ' · ' + fmtDur(d.focusSec) + '">' + d.day + '</div>';
    }
    $('heatmap').innerHTML = html;

    const badges = p.achievements || [];
    $('badgeCount').textContent = badges.filter(b => b.unlocked).length + ' / ' + badges.length;
    $('badges').innerHTML = badges.map(b =>
      '<div class="badge' + (b.unlocked ? '' : ' locked') + '">' +
      '<div class="b-icon">' + (b.unlocked ? b.icon : '🔒') + '</div>' +
      '<b>' + esc(b.name) + '</b><span>' + esc(b.desc) + '</span></div>'
    ).join('');

    $('escapeStats').textContent = '本月逃鸭 ' + month.escapes + ' 只 · 本周 ' + week.reduce((a, b) => a + b.escapes, 0) + ' 只';
  }

  /* ---------- 设置 ---------- */
  function renderSettings(p) {
    const s = p.settings;
    const set = (id, val) => { const el = $(id); if (el && document.activeElement !== el) el.value = val; };
    set('setFocus', s.focusMin);
    set('setShort', s.shortBreakMin);
    set('setLong', s.longBreakMin);
    set('setCycles', s.cyclesBeforeLong);
    set('setPostpone', s.postponeMin);
    set('setGoal', s.dailyGoalMin);
    set('setStrength', Math.round(s.overlayStrength * 100));
    $('setStrengthVal').textContent = Math.round(s.overlayStrength * 100) + '%';
    chk('setAutoNext', s.autoStartNext);
    chk('setOverlay', s.overlayDuringRest);
    chk('setGuard', s.fullscreenGuard);
    chk('setSound', s.duckSound);
    chk('setAutoStart', s.autoStartApp);

    const btn = $('btnPauseRemind');
    btn.textContent = p.paused ? '恢复提醒' : '暂停提醒';
    btn.classList.toggle('primary', !!p.paused);
    btn.classList.toggle('ghost', !p.paused);

    renderRules(p.rules || []);
    $('dataHint').textContent = '数据保存在：' + p.dataDir;
  }

  function chk(id, val) { const el = $(id); if (el) el.checked = !!val; }

  function renderRules(rules) {
    const list = $('rulesList');
    if (!rules.length) {
      list.innerHTML = '<div class="muted small">没有规则。加一条「10:15 休息 10 分钟」就可以开始了。</div>';
      return;
    }
    list.innerHTML = rules.map(r => {
      const mins = Math.round(r.restSec / 60);
      return '<div class="rule-row' + (r.enabled ? '' : ' off') + '">' +
        '<div class="rule-main"><b>' + esc(r.label) + '</b>' +
        '<span>' + esc(r.desc) + ' · 休息 ' + mins + ' 分钟' + (r.useOverlay ? ' · 遮罩' : '') + '</span></div>' +
        '<label class="switch"><input type="checkbox" data-rule="' + r.id + '"' + (r.enabled ? ' checked' : '') + ' /><i></i></label>' +
        '<div class="rule-actions">' +
        '<button class="ghost small" data-edit="' + r.id + '">编辑</button>' +
        '<button class="ghost small danger" data-del="' + r.id + '">删除</button>' +
        '</div></div>';
    }).join('');
  }

  /* ---------- 事件绑定 ---------- */
  function bindTabs() {
    document.querySelectorAll('.tab-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        S.tab = btn.dataset.tab;
        document.querySelectorAll('.tab-btn').forEach(b => b.classList.toggle('active', b === btn));
        document.querySelectorAll('.tab-panel').forEach(p => p.classList.toggle('active', p.id === 'tab-' + S.tab));
        window.scrollTo(0, 0);
        if (!S.data) return;
        if (S.tab === 'pond') { renderPond(S.data); setTimeout(() => window.dispatchEvent(new Event('resize')), 60); }
        if (S.tab === 'board') renderBoard(S.data);
        if (S.tab === 'settings') renderSettings(S.data);
      });
    });
  }

  function syncPreset(p) {
    if (S.presetPinned) return;
    const target = p.settings.focusMin;
    if (S.preset === target) return;
    S.preset = target;
    const row = $('presetRow');
    let btn = row.querySelector('[data-min="' + target + '"]');
    if (!btn) {
      btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'pill';
      btn.dataset.min = String(target);
      btn.textContent = target + ' 分钟';
      btn.addEventListener('click', () => {
        S.presetPinned = true;
        row.querySelectorAll('.pill').forEach(b => b.classList.toggle('active', b === btn));
        S.preset = target;
        if (S.data && S.data.phase === 'idle') paintClock(target * 60, target * 60);
      });
      const next = Array.from(row.querySelectorAll('.pill')).find(b => parseInt(b.dataset.min, 10) > target);
      row.insertBefore(btn, next || null);
    }
    row.querySelectorAll('.pill').forEach(b => b.classList.toggle('active', b === btn));
    if (!p.phase || p.phase === 'idle') paintClock(target * 60, target * 60);
  }

  function bindFocus() {
    $('btnMain').addEventListener('click', () => {
      const p = S.data; if (!p) return;
      if (p.phase === 'idle') startFocus();
      else post('toggle');
    });
    $('btnStartFocus').addEventListener('click', startFocus);
    $('btnSkip').addEventListener('click', () => {
      const p = S.data; if (!p) return;
      if (p.phase === 'rest') post('endRest');
      else post('skip');
    });
    $('btnAbort').addEventListener('click', () => {
      const p = S.data; if (!p) return;
      if (p.phase === 'rest') post('endRest');
      else post('abort');
    });
    $('taskInput').addEventListener('keydown', e => { if (e.key === 'Enter') startFocus(); });

    document.querySelectorAll('#presetRow .pill').forEach(btn => {
      btn.addEventListener('click', () => {
        S.presetPinned = true;
        document.querySelectorAll('#presetRow .pill').forEach(b => b.classList.toggle('active', b === btn));
        S.preset = parseInt(btn.dataset.min, 10) || 25;
        if (S.data && S.data.phase === 'idle') paintClock(S.preset * 60, S.preset * 60);
      });
    });

    document.querySelectorAll('[data-rest]').forEach(btn => {
      btn.addEventListener('click', () => {
        const sec = parseInt(btn.dataset.rest, 10) || 300;
        post('startRest', { sec, label: '休息 ' + Math.round(sec / 60) + ' 分钟' });
      });
    });
  }

  function startFocus() {
    const task = ($('taskInput').value || '').trim();
    post('startFocus', { task, minutes: S.preset });
  }

  function bindSettings() {
    const numeric = ['setFocus', 'setShort', 'setLong', 'setCycles', 'setPostpone', 'setGoal'];
    numeric.forEach(id => $(id).addEventListener('change', () => {
      const key = {
        setFocus: 'focusMin', setShort: 'shortBreakMin', setLong: 'longBreakMin',
        setCycles: 'cyclesBeforeLong', setPostpone: 'postponeMin', setGoal: 'dailyGoalMin'
      }[id];
      const v = parseInt($(id).value, 10);
      if (!isNaN(v)) post('patchSettings', { patch: { [key]: v } });
    }));

    const switches = ['setAutoNext', 'setOverlay', 'setGuard', 'setSound'];
    switches.forEach(id => $(id).addEventListener('change', () => {
      const key = {
        setAutoNext: 'autoStartNext', setOverlay: 'overlayDuringRest',
        setGuard: 'fullscreenGuard', setSound: 'duckSound'
      }[id];
      post('patchSettings', { patch: { [key]: $(id).checked } });
    }));
    $('setAutoStart').addEventListener('change', () => {
      post('patchSettings', { patch: { autoStartApp: $('setAutoStart').checked } });
    });

    $('setStrength').addEventListener('input', () => {
      $('setStrengthVal').textContent = $('setStrength').value + '%';
    });
    $('setStrength').addEventListener('change', () => {
      post('patchSettings', { patch: { overlayStrength: parseInt($('setStrength').value, 10) / 100 } });
    });

    $('btnPauseRemind').addEventListener('click', () => {
      post('pauseReminders', { paused: !(S.data && S.data.paused) });
    });
    $('btnPreview').addEventListener('click', () => post('previewOverlay'));
    $('btnData').addEventListener('click', () => post('openDataFolder'));

    $('ruleMode').addEventListener('change', syncFormVisibility);
    $('ruleForm').addEventListener('submit', submitRule);
    $('ruleCancel').addEventListener('click', resetForm);

    $('rulesList').addEventListener('click', e => {
      const edit = e.target.closest('[data-edit]');
      if (edit) return beginEdit(edit.dataset.edit);
      const del = e.target.closest('[data-del]');
      if (del) { post('deleteRule', { id: del.dataset.del }); return; }
    });
    $('rulesList').addEventListener('change', e => {
      const cb = e.target.closest('input[data-rule]');
      if (cb) post('toggleRule', { id: cb.dataset.rule, enabled: cb.checked });
    });
  }

  function syncFormVisibility() {
    const mode = $('ruleMode').value;
    $('wrapTime').hidden = mode === 'interval' || mode === 'once';
    $('wrapOnce').hidden = mode !== 'once';
    $('wrapInterval').hidden = mode !== 'interval';
    $('wrapWeekdays').hidden = mode !== 'weekly';
  }

  function submitRule(e) {
    e.preventDefault();
    const mode = $('ruleMode').value;
    let weekdays = 0;
    document.querySelectorAll('#wrapWeekdays input').forEach(c => { if (c.checked) weekdays |= (1 << parseInt(c.value, 10)); });
    if (mode === 'weekly' && weekdays === 0) weekdays = 127;
    const rule = {
      id: S.editing || '',
      mode,
      time: $('ruleTime').value || '10:15',
      weekdays,
      intervalMin: parseInt($('ruleInterval').value, 10) || 60,
      restSec: Math.max(60, (parseInt($('ruleRestMin').value, 10) || 10) * 60),
      label: ($('ruleLabel').value || '休息一下').slice(0, 12),
      useOverlay: $('ruleOverlay').checked,
      enabled: true,
      onceAt: mode === 'once' ? ($('ruleOnce').value || null) : null
    };
    if (!rule.time.includes(':')) rule.time = '10:15';
    if (S.editing) post('updateRule', { rule });
    else post('addRule', { rule });
    resetForm();
  }

  function beginEdit(id) {
    const r = (S.data.rules || []).find(x => x.id === id);
    if (!r) return;
    S.editing = id;
    $('ruleMode').value = r.mode;
    $('ruleTime').value = r.time;
    $('ruleInterval').value = r.intervalMin;
    $('ruleRestMin').value = Math.round(r.restSec / 60);
    $('ruleLabel').value = r.label;
    $('ruleOverlay').checked = r.useOverlay;
    document.querySelectorAll('#wrapWeekdays input').forEach(c => {
      const bit = 1 << parseInt(c.value, 10);
      c.checked = (r.weekdays & bit) !== 0;
    });
    if (r.mode === 'once' && r.onceAt) {
      $('ruleOnce').value = String(r.onceAt).replace(' ', 'T').slice(0, 16);
    }
    $('ruleSubmit').textContent = '保存修改';
    $('ruleFormTitle').textContent = '编辑「' + r.label + '」';
    $('ruleCancel').hidden = false;
    syncFormVisibility();
    $('ruleForm').scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  function resetForm() {
    S.editing = null;
    $('ruleSubmit').textContent = '添加规则';
    $('ruleFormTitle').textContent = '新增一条提醒';
    $('ruleCancel').hidden = true;
    $('ruleMode').value = 'daily';
    $('ruleLabel').value = '休息一下';
    $('ruleRestMin').value = 10;
    $('ruleInterval').value = 60;
    $('ruleOverlay').checked = true;
    syncFormVisibility();
  }

  function bindBoard() {
    $('btnExport').addEventListener('click', exportCard);
  }

  /* ---------- 庆祝 & 声音 ---------- */
  function celebrate(payload) {
    const el = $('celebrate');
    el.innerHTML = '<div class="cel-inner"><div class="cel-duck"><img src="duck.png" alt="duck" /></div><div class="cel-text">' + esc(payload.text) + '</div></div>';
    el.classList.remove('hidden');
    setTimeout(() => el.classList.add('hidden'), 1700);
    playSound('celebrate');
  }

  let actx = null;
  function playSound(kind) {
    if (!S.data || !S.data.settings.duckSound) return;
    try {
      if (!actx) actx = new (window.AudioContext || window.webkitAudioContext)();
      if (actx.state === 'suspended') actx.resume();
      const now = actx.currentTime;
      const gain = actx.createGain();
      gain.gain.value = 0.5;
      gain.connect(actx.destination);

      if (kind === 'quack') {
        const osc = actx.createOscillator();
        const g = actx.createGain();
        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(420, now);
        osc.frequency.exponentialRampToValueAtTime(180, now + 0.16);
        g.gain.setValueAtTime(0.0001, now);
        g.gain.exponentialRampToValueAtTime(0.55, now + 0.02);
        g.gain.exponentialRampToValueAtTime(0.0001, now + 0.26);
        osc.connect(g).connect(gain);
        osc.start(now); osc.stop(now + 0.28);
      } else if (kind === 'chime') {
        [660, 880].forEach((f, i) => {
          const osc = actx.createOscillator();
          const g = actx.createGain();
          osc.type = 'sine';
          osc.frequency.value = f;
          const t0 = now + i * 0.12;
          g.gain.setValueAtTime(0.0001, t0);
          g.gain.exponentialRampToValueAtTime(0.35, t0 + 0.03);
          g.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.34);
          osc.connect(g).connect(gain);
          osc.start(t0); osc.stop(t0 + 0.36);
        });
      } else if (kind === 'celebrate') {
        [523, 659, 784, 1047].forEach((f, i) => {
          const osc = actx.createOscillator();
          const g = actx.createGain();
          osc.type = 'triangle';
          osc.frequency.value = f;
          const t0 = now + i * 0.09;
          g.gain.setValueAtTime(0.0001, t0);
          g.gain.exponentialRampToValueAtTime(0.3, t0 + 0.02);
          g.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.3);
          osc.connect(g).connect(gain);
          osc.start(t0); osc.stop(t0 + 0.32);
        });
      } else if (kind === 'fail') {
        const osc = actx.createOscillator();
        const g = actx.createGain();
        osc.type = 'sine';
        osc.frequency.setValueAtTime(320, now);
        osc.frequency.exponentialRampToValueAtTime(120, now + 0.5);
        g.gain.setValueAtTime(0.25, now);
        g.gain.exponentialRampToValueAtTime(0.0001, now + 0.6);
        osc.connect(g).connect(gain);
        osc.start(now); osc.stop(now + 0.62);
      }
    } catch (e) { /* 音频失败不影响主流程 */ }
  }

  /* ---------- 导出打卡图 ---------- */
  let duckImgCache = null;
  function loadDuckImg() {
    if (duckImgCache && duckImgCache.complete && duckImgCache.naturalWidth) return Promise.resolve(duckImgCache);
    return new Promise(res => {
      const im = new Image();
      im.onload = () => { duckImgCache = im; res(im); };
      im.onerror = () => res(null);
      im.src = 'duck.png';
    });
  }

  async function exportCard() {
    const p = S.data; if (!p) return;
    const name = '鸭塘打卡';
    const W = 1000, H = 1400;
    const c = document.createElement('canvas');
    c.width = W; c.height = H;
    const g = c.getContext('2d');

    const bg = g.createLinearGradient(0, 0, W, H);
    bg.addColorStop(0, '#0D2A23'); bg.addColorStop(0.55, '#081814'); bg.addColorStop(1, '#06110E');
    g.fillStyle = bg; g.fillRect(0, 0, W, H);

    const glow = g.createRadialGradient(760, 120, 10, 760, 120, 460);
    glow.addColorStop(0, 'rgba(246,196,69,.18)');
    glow.addColorStop(1, 'rgba(246,196,69,0)');
    g.fillStyle = glow; g.fillRect(0, 0, W, 600);

    g.fillStyle = '#F6C445';
    g.font = '600 30px "Microsoft YaHei UI", sans-serif';
    g.fillText('鸭塘 · DuckPond', 72, 92);

    g.fillStyle = '#E9F4EE';
    g.font = '700 60px "Microsoft YaHei UI", sans-serif';
    g.fillText('今日打卡', 72, 182);

    const now = new Date();
    g.fillStyle = '#8FB2A6';
    g.font = '20px "Microsoft YaHei UI", sans-serif';
    g.fillText(now.getFullYear() + '.' + String(now.getMonth() + 1).padStart(2, '0') + '.' + String(now.getDate()).padStart(2, '0'), 76, 224);

    const im = await loadDuckImg();
    if (im) {
      const gg = g.createRadialGradient(830, 200, 10, 830, 200, 190);
      gg.addColorStop(0, 'rgba(246,196,69,.18)'); gg.addColorStop(1, 'rgba(246,196,69,0)');
      g.fillStyle = gg; g.fillRect(660, 40, 340, 300);
      g.drawImage(im, 726, 92, 208, 208);
    } else {
      g.font = '140px "Segoe UI Emoji", sans-serif';
      g.fillText('🦆', 740, 236);
    }

    const cardY = 290;
    const items = [
      ['今日专注', fmtDur(p.today.focusSec)],
      ['今日孵鸭', p.today.focusCount + ' 只'],
      ['连续天数', p.totals.streak + ' 天'],
      ['鸭塘规模', p.totals.ducks + ' 只 · ' + p.totals.pondName],
      ['累计专注', fmtDur(p.totals.focusSec)],
      ['逃走的鸭', p.totals.escapes + ' 只']
    ];
    for (let i = 0; i < items.length; i++) {
      const col = i % 2, row = Math.floor(i / 2);
      const x = 72 + col * 440, y = cardY + row * 120;
      g.fillStyle = 'rgba(255,255,255,.035)';
      g.fillRect(x, y, 400, 96);
      g.strokeStyle = 'rgba(122,190,171,.16)'; g.lineWidth = 1;
      g.strokeRect(x + .5, y + .5, 399, 95);
      g.fillStyle = '#8FB2A6'; g.font = '18px "Microsoft YaHei UI", sans-serif';
      g.fillText(items[i][0], x + 22, y + 36);
      g.fillStyle = '#E9F4EE'; g.font = '600 34px "Microsoft YaHei UI", sans-serif';
      g.fillText(items[i][1], x + 22, y + 78);
    }

    const chartY = cardY + 400;
    g.fillStyle = '#8FB2A6'; g.font = '18px "Microsoft YaHei UI", sans-serif';
    g.fillText('最近 7 天', 72, chartY);
    const week = p.week || [];
    const maxW = Math.max(600, ...week.map(w => w.focusSec));
    const baseY = chartY + 250, barW = 86, gap = 28;
    g.strokeStyle = 'rgba(122,190,171,.14)'; g.lineWidth = 1;
    g.beginPath(); g.moveTo(72, baseY + .5); g.lineTo(72 + week.length * (barW + gap) - gap, baseY + .5); g.stroke();
    week.forEach((w, i) => {
      const x = 72 + i * (barW + gap);
      const hgt = Math.max(4, Math.round(w.focusSec / maxW * 200));
      const grad2 = g.createLinearGradient(0, baseY - hgt, 0, baseY);
      grad2.addColorStop(0, i === week.length - 1 ? '#F8D169' : 'rgba(63,182,168,.85)');
      grad2.addColorStop(1, i === week.length - 1 ? '#EDB42F' : 'rgba(63,182,168,.25)');
      g.fillStyle = grad2;
      g.fillRect(x, baseY - hgt, barW, hgt);
      g.fillStyle = '#8FB2A6'; g.font = '15px "Microsoft YaHei UI", sans-serif';
      g.fillText(w.label, x + 18, baseY + 26);
      g.fillText(Math.round(w.focusSec / 60) + '′', x + 26, baseY - hgt - 10);
    });

    const m = p.month || { totalSec: 0, ducks: 0, escapes: 0, days: [] };
    const activeDays = (m.days || []).filter(d => d.focusSec > 0).length;
    g.fillStyle = 'rgba(255,255,255,.03)';
    g.fillRect(72, 1030, 856, 118);
    g.strokeStyle = 'rgba(122,190,171,.16)'; g.lineWidth = 1;
    g.strokeRect(72.5, 1030.5, 855, 117);
    g.fillStyle = '#8FB2A6'; g.font = '18px "Microsoft YaHei UI", sans-serif';
    g.fillText('本月', 96, 1066);
    const mItems = [
      ['专注', fmtDur(m.totalSec)],
      ['孵鸭', m.ducks + ' 只'],
      ['达标', activeDays + ' 天'],
      ['逃鸭', m.escapes + ' 只']
    ];
    mItems.forEach((it, i) => {
      const x = 96 + i * 212;
      g.fillStyle = '#8FB2A6'; g.font = '15px "Microsoft YaHei UI", sans-serif';
      g.fillText(it[0], x, 1104);
      g.fillStyle = i === 3 && m.escapes > 0 ? '#E3907F' : '#E9F4EE';
      g.font = '600 30px "Microsoft YaHei UI", sans-serif';
      g.fillText(it[1], x, 1136);
    });

    g.fillStyle = 'rgba(233,244,238,.55)';
    g.font = '16px "Microsoft YaHei UI", sans-serif';
    g.fillText('每一次坐住，塘里就多一只鸭。', 72, 1280);
    g.fillStyle = '#F6C445';
    g.fillText('Ctrl+Alt+R 随时休息 · 鸭塘 DuckPond', 72, 1320);

    const dataUrl = c.toDataURL('image/png');
    post('savePng', { dataUrl, name });
  }

  /* ---------- 初始化 ---------- */
  function init() {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener('message', onMessage);
    }
    Pond.init($('pondCanvas'));
    Pond.onQuack(() => playSound('quack'));

    bindTabs();
    bindFocus();
    bindSettings();
    bindBoard();
    syncFormVisibility();

    setInterval(() => {
      if (!S.data) return;
      if (S.data.phase === 'idle') return;
      const elapsed = (performance.now() - S.live.at) / 1000;
      const remaining = Math.max(0, S.live.remaining - (S.live.running ? elapsed : 0));
      paintClock(remaining, S.live.planned);
    }, 200);

    post('ready');
  }

  document.addEventListener('DOMContentLoaded', init);
})();
