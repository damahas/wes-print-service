// ===== 公共工具 =====
function toast(msg, ok) {
  var t = document.getElementById('toast');
  t.textContent = msg;
  t.style.borderLeftColor = ok ? '#34d399' : '#f87171';
  t.classList.add('show');
  setTimeout(function () { t.classList.remove('show'); }, 2200);
}
function api(u, opt) {
  return fetch(u, Object.assign({ headers: { 'Content-Type': 'application/json' } }, opt));
}

// ===== 页面逻辑 =====
function fillPrinters(list, def, sel) {
  printer.innerHTML = '';
  var none = document.createElement('option');
  none.value = '';
  none.text = '（系统默认）';
  printer.appendChild(none);
  list.forEach(function (n) {
    var o = document.createElement('option');
    o.value = n; o.text = n;
    printer.appendChild(o);
  });
  printer.value = (sel && sel !== '') ? sel : (def || '');
}
// ===== MQ 双通道（rabbitmq / kafka）=====
function cardOf(key) { return document.getElementById('card-' + key); }

function fillCfg(key, d) {
  if (!d) return;
  var c = cardOf(key);
  if (!c) return;
  if (key === 'kafka') {
    c.querySelector('.f-bootstrap').value = d.bootstrapServers || '';
    c.querySelector('.f-queue').value = d.queue || '';
    c.querySelector('.f-group').value = d.groupId || '';
    c.querySelector('.f-user').value = d.userName || '';
    c.querySelector('.f-pass').value = d.password || '';
  } else {
    c.querySelector('.f-host').value = d.host || '';
    c.querySelector('.f-port').value = d.port || '';
    c.querySelector('.f-queue').value = d.queue || '';
    c.querySelector('.f-user').value = d.userName || '';
    c.querySelector('.f-pass').value = d.password || '';
  }
}

function saveCfg(key) {
  var c = cardOf(key);
  var b;
  if (key === 'kafka') {
    b = {
      key: 'kafka', type: 'Kafka', enabled: true, autoAck: true,
      bootstrapServers: c.querySelector('.f-bootstrap').value,
      queue: c.querySelector('.f-queue').value,
      groupId: c.querySelector('.f-group').value,
      userName: c.querySelector('.f-user').value,
      password: c.querySelector('.f-pass').value
    };
  } else {
    b = {
      key: 'rabbitmq', type: 'RabbitMQ', enabled: true, autoAck: true,
      host: c.querySelector('.f-host').value,
      port: Number(c.querySelector('.f-port').value) || 0,
      queue: c.querySelector('.f-queue').value,
      userName: c.querySelector('.f-user').value,
      password: c.querySelector('.f-pass').value
    };
  }
  api('/api/mq/config?key=' + key, { method: 'POST', body: JSON.stringify(b) })
    .then(r => r.ok ? toast('已保存', true) : toast('保存失败', false));
}

// 每个输入变更即保存对应通道
document.querySelectorAll('#view-mq .card input').forEach(function (el) {
  el.addEventListener('change', function () {
    var key = el.closest('.card').id.replace('card-', '');
    saveCfg(key);
  });
});

printer.onchange = function () {
  api('/api/printer/default', {
    method: 'POST',
    body: JSON.stringify({ key: 'printer.default', value: printer.value || '' })
  }).then(r => r.ok ? toast('默认打印机已保存', true) : toast('保存失败', false));
};

// 启用开关：按通道 key 存 mq.enabled.{key}
document.querySelectorAll('#view-mq .mq-on').forEach(function (sw) {
  sw.addEventListener('change', function () {
    var key = sw.dataset.key;
    api('/api/settings/mq-enabled?key=' + key, {
      method: 'POST',
      body: JSON.stringify({ key: 'mq.enabled.' + key, value: sw.checked ? 'true' : 'false' })
    }).then(function () { refreshMqState(); });
  });
});

// 连接/断开按钮：按通道 key 操作
document.querySelectorAll('#view-mq .mq-toggle').forEach(function (btn) {
  btn.addEventListener('click', function () {
    var key = btn.dataset.key;
    if (btn.dataset.mode === 'disconnect') {
      api('/api/mq/disconnect?key=' + key, { method: 'POST' })
        .then(function () { toast('已断开', true); refreshMqState(); });
    } else {
      api('/api/mq/connect?key=' + key, { method: 'POST' })
        .then(r => r.ok ? r.json() : null).then(function (d) {
          if (d && d.ok) toast('已发起连接', true);
          else toast('连接失败：' + (d && d.error || ''), false);
          refreshMqState();
        });
    }
  });
});

function tag(s) {
  var c = s === 'Success' ? 'ok' : s === 'Failed' ? 'err' : s === 'Pending' ? 'warn' : 'muted';
  return '<span class="tag ' + c + '">' + esc(s) + '</span>';
}
function channelCell(ch) {
  var c = (ch || '').toLowerCase();
  var k = c.indexOf('mq') >= 0 ? 'mq' : c.indexOf('api') >= 0 ? 'api' : 'other';
  var label = k === 'mq' ? 'RabbitMQ' : k === 'api' ? 'API' : esc(ch || '');
  return '<span class="chicon ' + k + '">' + label + '</span>';
}
function esc(s) {
  return String(s == null ? '' : s)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;')
    .replace(/\r/g, ' ').replace(/\n/g, ' ').replace(/\t/g, ' ');
}
// 用于 <pre> 代码块：保留换行与缩进，仅转义 HTML 特殊字符（换行非 HTML 危险字符，安全）。
function escCode(s) {
  return String(s == null ? '' : s)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}
var PAGE_SIZE = 10;
var curPage = 1;
function q(page) {
  curPage = page || 1;
  var ch = document.getElementById('channel').value, st = document.getElementById('status').value;
  var u = '/api/records?page=' + curPage + '&pageSize=' + PAGE_SIZE
    + (ch ? '&channel=' + ch : '') + (st ? '&status=' + st : '');
  api(u).then(r => r.ok ? r.json() : null).then(function (d) {
    d = d || {};
    var items = d.items || [];
    var total = d.total || 0;
    var totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
    if (curPage > totalPages) curPage = totalPages;
    var h = '';
    items.forEach(function (r) {
      var refRaw = r.templateRef || '';
      var refDisp = refRaw.length > 40 ? refRaw.slice(0, 40) + '…' : refRaw;
      h += '<tr>'
        + '<td>' + channelCell(r.channel) + '</td>'
        + '<td>' + esc(r.templateKind || '-') + '</td>'
        + '<td class="mono ellipsis" title="' + esc(refRaw) + '">' + esc(refDisp || '-') + '</td>'
        + '<td>' + esc(r.printerName || '-') + '</td>'
        + '<td class="mono">' + esc(new Date(r.createdAt).toLocaleString()) + '</td>'
        + '<td>' + tag(r.status) + '</td>'
        + '<td class="ellipsis muted" title="' + esc(r.message || '') + '">' + esc(r.message || '') + '</td>'
        + '<td><button class="lk-btn" data-id="' + r.id + '">查看</button></td>'
        + '</tr>';
    });
    rows.innerHTML = h || '<tr><td colspan="8" style="color:var(--muted)">无记录</td></tr>';
    bindViewBtns();
    renderPager(total, totalPages);
  });
}
function renderPager(total, totalPages) {
  var el = document.getElementById('pager');
  if (!el) return;
  if (total === 0) { el.innerHTML = ''; return; }
  var s = '<span class="pg-info">共 ' + total + ' 条 · 第 ' + curPage + '/' + totalPages + ' 页</span>';
  s += '<button class="pg-btn" data-p="' + (curPage - 1) + '"' + (curPage <= 1 ? ' disabled' : '') + '>上一页</button>';
  s += '<button class="pg-btn" data-p="' + (curPage + 1) + '"' + (curPage >= totalPages ? ' disabled' : '') + '>下一页</button>';
  el.innerHTML = s;
  el.querySelectorAll('.pg-btn').forEach(function (b) {
    b.onclick = function () { var p = Number(b.dataset.p); if (p >= 1) q(p); };
  });
}
document.getElementById('channel').onchange = function () { q(1); };
document.getElementById('status').onchange = function () { q(1); };

// ===== 查看打印参数 =====
function bindViewBtns() {
  document.querySelectorAll('#rows .lk-btn').forEach(function (b) {
    b.onclick = function () { viewReq(Number(b.dataset.id)); };
  });
}
function viewReq(id) {
  api('/api/records/' + id).then(r => r.ok ? r.json() : null).then(function (d) {
    if (!d) { toast('未找到记录', false); return; }
    var html = ''
      + row2('渠道', d.channel)
      + row2('类型', d.templateKind)
      + row2('模板', d.templateRef)
      + row2('打印机', d.printerName)
      + row2('状态', d.status)
      + row2('信息', d.message)
      + row2('来源', d.sourceRef)
      + row2('时间', new Date(d.createdAt).toLocaleString());
    html += '<div class="kv"><div class="k">打印预览</div><div class="v" id="previewBox"><span class="muted">加载中…</span></div></div>';
    html += renderFields(d.request);
    document.getElementById('modalBody').innerHTML = html;
    document.getElementById('modal').hidden = false;

    // 拉取并打印内容预览（PNG base64 图片展示）
    api('/api/records/' + id + '/preview').then(r => r.ok ? r.json() : null).then(function (p) {
      var box = document.getElementById('previewBox');
      if (!box) return;
      if (p && p.base64) {
        box.innerHTML = '<img class="preview-img" alt="打印预览" src="data:image/png;base64,' + p.base64 + '">';
      } else {
        box.innerHTML = '<span class="muted">无预览</span>';
      }
    }).catch(function () {
      var box = document.getElementById('previewBox');
      if (box) box.innerHTML = '<span class="muted">预览加载失败</span>';
    });
  });
}
// 提交参数（Fields）：直接展示原始 JSON。
function renderFields(req) {
  if (!req) return '<div class="kv"><div class="k">提交参数</div><div class="v muted">无（未记录参数）</div></div>';
  var raw;
  try { raw = JSON.stringify(JSON.parse(req), null, 2); } catch { raw = req; }
  return '<div class="kv"><div class="k">提交参数</div><div class="v"><pre class="code">' + escCode(raw) + '</pre></div></div>';
}
function row2(k, v) {
  return '<div class="kv"><div class="k">' + esc(k) + '</div><div class="v">' + esc(v == null ? '-' : v) + '</div></div>';
}
function closeModal() { document.getElementById('modal').hidden = true; }
document.getElementById('modalX').onclick = closeModal;
document.getElementById('modalMask').onclick = closeModal;
document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeModal(); });

function refreshMqState() {
  api('/api/mq/status').then(r => r.ok ? r.json() : null).then(function (s) {
    if (!s) return;
    var items = s.items || [];
    var map = {
      Disabled: ['muted', 'MQ 消费未启用'], NoConfig: ['warn', '已启用，未配置'],
      Idle: ['muted', '已启用，等待连接'], Connecting: ['warn', '连接中...'],
      Connected: ['ok', '已连接，正在消费'], Reconnecting: ['warn', '重连中...'],
      Failed: ['err', '连接失败'], Stopped: ['muted', '已停止']
    };
    items.forEach(function (it) {
      var c = cardOf(it.key);
      if (!c) return;
      var m = map[it.state] || ['muted', it.message];
      var dot = c.querySelector('.mq-dot');
      var dot2 = c.querySelector('.mq-dot2');
      var txt = c.querySelector('.mq-text');
      var btn = c.querySelector('.mq-toggle');
      var sw = c.querySelector('.mq-on');
      if (dot) { dot.className = 'dot ' + m[0] + ' mq-dot'; dot.title = it.message || m[1]; }
      if (dot2) dot2.className = 'dot ' + m[0] + ' mq-dot2';
      if (txt) txt.textContent = it.message || m[1];
      if (sw) sw.checked = it.enabled;
      c.classList.toggle('on', !!it.enabled); // 未启用则隐藏下方配置/状态
      var canConnect = ['Idle', 'Disabled', 'NoConfig', 'Failed', 'Stopped'].indexOf(it.state) >= 0;
      var canDisconnect = ['Connected', 'Connecting', 'Reconnecting'].indexOf(it.state) >= 0;
      if (!btn) return;
      if (canConnect) {
        btn.dataset.mode = 'connect'; btn.textContent = '连接';
        btn.className = 'btn-connect mq-toggle'; btn.disabled = !it.enabled;
      } else if (canDisconnect) {
        btn.dataset.mode = 'disconnect'; btn.textContent = '断开';
        btn.className = 'btn-disconnect mq-toggle'; btn.disabled = false;
      } else {
        btn.dataset.mode = 'connect'; btn.textContent = '连接';
        btn.className = 'btn-connect mq-toggle'; btn.disabled = true;
      }
    });
  });
}

// 初始化
function loadPrinters(sel) {
  api('/api/printers').then(r => r.ok ? r.json() : null).then(function (data) {
    var list = (data && data.printers) || [];
    var def = (data && data.defaultPrinter) || '';
    api('/api/printer/default').then(r => r.ok ? r.json() : null).then(function (d) {
      fillPrinters(list, def, (d && d.value) ? d.value : '');
    }).catch(function () { fillPrinters(list, def, ''); });
  });
}
loadPrinters();
['rabbitmq', 'kafka'].forEach(function (key) {
  api('/api/mq/config?key=' + key).then(r => r.ok ? r.json() : null).then(function (d) { fillCfg(key, d); });
  api('/api/settings/mq-enabled?key=' + key).then(r => r.ok ? r.json() : null).then(function (d) {
    var sw = document.querySelector('#card-' + key + ' .mq-on');
    var on = (d && d.value === 'true');
    if (sw) sw.checked = on;
    var card = document.getElementById('card-' + key);
    if (card) card.classList.toggle('on', on); // 立即按启用状态显隐下方内容
  });
});
// 默认打印机已从 MQ 配置拆分，独立读取回填顶部下拉（loadPrinters 内部已拉取回填，这里仅确保已选中）
api('/api/printer/default').then(r => r.ok ? r.json() : null).then(function (d) {
  if (d) loadPrinters(d.value || '');
});
q();
refreshMqState();
setInterval(refreshMqState, 5000);

// ===== 导航切换 =====
function switchView(name) {
  document.querySelectorAll('.view').forEach(function (v) { v.hidden = true; });
  var el = document.getElementById('view-' + name);
  if (el) el.hidden = false;
  document.querySelectorAll('.nav-btn').forEach(function (b) {
    b.classList.toggle('active', b.dataset.view === name);
  });
  if (name === 'templates') Tpl.ensureLoaded();
}
document.querySelectorAll('.nav-btn').forEach(function (b) {
  b.onclick = function () { switchView(b.dataset.view); };
});
switchView('records');

// 设置页绑定
var retentionEl = document.getElementById('retention');
api('/api/settings/record-retention').then(r => r.ok ? r.json() : null).then(function (d) {
  if (d && d.value) retentionEl.value = d.value;
});
document.getElementById('saveRetention').onclick = function () {
  var v = Number(retentionEl.value) || 30;
  api('/api/settings/record-retention', { method: 'POST', body: JSON.stringify({ key: 'record.retention.days', value: String(v) }) })
    .then(function () { toast('保留天数已保存', true); });
};
document.getElementById('purgeNow').onclick = function () {
  api('/api/records/purge', { method: 'POST' }).then(r => r.ok ? r.json() : null).then(function (d) {
    toast('已清理 ' + ((d && d.deleted) || 0) + ' 条', true); q(1);
  });
};
document.getElementById('openSettingsMq').onclick = function () { switchView('mq'); };

// ===== 模板设计器 =====
var Tpl = (function () {
  var state = {
    name: '',
    page: { width: 100, height: 60, unit: 'mm', dpi: 203, backgroundColor: '#ffffff' },
    items: []
  };
  var selectedIdx = -1;
  var loaded = false;

  function mmToPx(mm, dpi) { return (mm * dpi) / 25.4; }
  function unitToMm(v, unit) {
    if (unit === 'mm') return v;
    if (unit === 'in') return v * 25.4;
    return v / (state.page.dpi / 25.4); // px
  }
  function unitToPx(v, unit, dpi) { return mmToPx(unitToMm(v, unit), dpi); }

  function loadList() {
    api('/api/templates').then(r => r.ok ? r.json() : null).then(function (d) {
      var sel = document.getElementById('tplList');
      var cur = sel.value;
      sel.innerHTML = '<option value="">（新建空白模板）</option>';
      (d && d.items || []).forEach(function (t) {
        var o = document.createElement('option');
        o.value = t.name; o.text = t.name; sel.appendChild(o);
      });
      sel.value = cur;
    });
  }

  function newTemplate() {
    state.name = '';
    state.page = { width: 100, height: 60, unit: 'mm', dpi: 203, backgroundColor: '#ffffff' };
    state.items = [];
    selectedIdx = -1;
    var nb = document.getElementById('tplSaveName'); if (nb) nb.value = '';
    renderAll();
  }

  function loadTemplate(name) {
    if (!name) { newTemplate(); return; }
    api('/api/templates/' + encodeURIComponent(name)).then(r => r.ok ? r.json() : null).then(function (d) {
      if (!d) { toast('加载失败', false); return; }
      state.name = name;
      state.page = Object.assign({ width: 100, height: 60, unit: 'mm', dpi: 203, backgroundColor: '#ffffff' }, d.page || {});
      state.items = Array.isArray(d.items) ? d.items.map(function (it) { return Object.assign({}, it); }) : [];
      selectedIdx = -1;
      var nb = document.getElementById('tplSaveName'); if (nb) nb.value = name;
      renderAll();
    }).catch(function () { toast('加载失败', false); });
  }

  function renderAll() {
    renderPageInputs();
    renderCanvas();
    renderProps();
    renderJson();
  }

  var PAGE_PRESETS = {
    'a4': { w: 210, h: 297 },
    'a5': { w: 148, h: 210 },
    'a6': { w: 105, h: 148 },
    'label-40': { w: 40, h: 30 },
    'label-60': { w: 60, h: 40 },
    'label-80': { w: 80, h: 50 },
    'label-100': { w: 100, h: 60 },
    'pos-80': { w: 80, h: 200 }
  };

  function renderPageInputs() {
    document.getElementById('pgUnit').value = state.page.unit || 'mm';
    document.getElementById('pgW').value = state.page.width;
    document.getElementById('pgH').value = state.page.height;
    document.getElementById('pgDpi').value = state.page.dpi;
    document.getElementById('pgBg').value = state.page.backgroundColor || '#ffffff';
    var match = '';
    for (var key in PAGE_PRESETS) {
      if (PAGE_PRESETS[key].w === Number(state.page.width) && PAGE_PRESETS[key].h === Number(state.page.height) && (state.page.unit || 'mm') === 'mm') {
        match = key; break;
      }
    }
    document.getElementById('pgPreset').value = match;
  }

  function canvasSize() {
    var dpi = Number(state.page.dpi) || 203;
    var w = unitToPx(Number(state.page.width) || 0, state.page.unit || 'mm', dpi);
    var h = unitToPx(Number(state.page.height) || 0, state.page.unit || 'mm', dpi);
    return { w: Math.max(1, Math.round(w)), h: Math.max(1, Math.round(h)), dpi: dpi };
  }
  var manualScale = 0; // 0 = 自适应
  function canvasScale() {
    if (manualScale > 0) return manualScale;
    var wrap = document.getElementById('tplCanvasWrap');
    if (!wrap) return 1;
    var s = canvasSize();
    var pad = 52;
    var maxW = Math.max(80, wrap.clientWidth - pad);
    var maxH = Math.max(80, wrap.clientHeight - pad);
    return Math.min(1, maxW / s.w, maxH / s.h);
  }

  function renderCanvas() {
    var canvas = document.getElementById('tplCanvas');
    var scaler = document.getElementById('tplCanvasScaler');
    var s = canvasSize();
    var scale = canvasScale();
    canvas.style.width = s.w + 'px';
    canvas.style.height = s.h + 'px';
    canvas.style.background = state.page.backgroundColor || '#ffffff';
    canvas.style.transform = 'scale(' + scale + ')';
    if (scaler) { scaler.style.width = (s.w * scale) + 'px'; scaler.style.height = (s.h * scale) + 'px'; }
    canvas.innerHTML = '';
    var dpi = s.dpi;
    state.items.forEach(function (it, idx) {
      var el = document.createElement('div');
      el.className = 'tpl-item' + (idx === selectedIdx ? ' sel' : '');
      var x = unitToPx(Number(it.x) || 0, state.page.unit, dpi);
      var y = unitToPx(Number(it.y) || 0, state.page.unit, dpi);
      el.style.left = x + 'px';
      el.style.top = y + 'px';
      if (it.type === 'line') {
        var w = unitToPx(Number(it.endX) || 0, state.page.unit, dpi) - x;
        var h = unitToPx(Number(it.endY) || 0, state.page.unit, dpi) - y;
        el.style.width = Math.abs(w) + 'px';
        el.style.height = Math.abs(h) + 'px';
        var sw = Number(it.weight) || 1;
        el.style.background = 'transparent';
        el.style.borderTop = sw + 'px solid ' + (it.color || '#000');
        el.title = 'line';
      } else if (it.type === 'barcode') {
        var bw = unitToPx(Number(it.width) || 30, state.page.unit, dpi);
        var bh = unitToPx(Number(it.height) || 10, state.page.unit, dpi);
        el.style.width = bw + 'px';
        el.style.height = bh + 'px';
        el.style.background = 'rgba(248,250,252,.9)';
        el.style.outline = '1px dashed #94a3b8';
        el.innerHTML = '<span class="tpl-tag">条码:' + esc(it.code || '{{}}') + '</span>';
      } else if (it.type === 'image') {
        var iw = unitToPx(Number(it.width) || 20, state.page.unit, dpi);
        var ih = unitToPx(Number(it.height) || 20, state.page.unit, dpi);
        el.style.width = iw + 'px';
        el.style.height = ih + 'px';
        el.style.background = 'rgba(248,250,252,.9)';
        el.style.outline = '1px dashed #94a3b8';
        el.innerHTML = '<span class="tpl-tag">图片:' + esc(it.embedBase64 ? '嵌入图' : (it.path || '{{}}')) + '</span>';
      } else { // text
        var tw = unitToPx(Number(it.width) || 40, state.page.unit, dpi);
        var th = unitToPx(Number(it.height) || 8, state.page.unit, dpi);
        el.style.width = tw + 'px';
        el.style.height = th + 'px';
        var fs = Math.max(6, unitToPx(Number(it.fontSize) || 3, state.page.unit, dpi));
        el.style.fontSize = fs + 'px';
        el.style.color = it.color || '#000';
        el.style.fontWeight = it.bold ? 'bold' : 'normal';
        el.style.textAlign = it.align || 'left';
        el.style.fontFamily = it.fontFamily || 'sans-serif';
        el.innerHTML = '<span class="tpl-text">' + esc(it.text || '{{}}') + '</span>';
      }
      el.onmousedown = function (e) { startDrag(e, idx); };
      el.onclick = function (e) { e.stopPropagation(); select(idx); };
      canvas.appendChild(el);
    });
  }

  // 拖拽移动（仅在画布像素坐标系，松手后换算回单位值）
  function startDrag(e, idx) {
    e.preventDefault(); e.stopPropagation();
    select(idx);
    var it = state.items[idx];
    var canvas = document.getElementById('tplCanvas');
    var rect = canvas.getBoundingClientRect();
    var scale = canvasScale();
    var startX = e.clientX, startY = e.clientY;
    var origX = unitToPx(Number(it.x) || 0, state.page.unit, Number(state.page.dpi) || 203);
    var origY = unitToPx(Number(it.y) || 0, state.page.unit, Number(state.page.dpi) || 203);
    function move(ev) {
      var dx = (ev.clientX - startX) / scale, dy = (ev.clientY - startY) / scale;
      var nx = origX + dx, ny = origY + dy;
      var dpi = Number(state.page.dpi) || 203;
      it.x = Math.round(pxToUnit(nx, state.page.unit, dpi) * 100) / 100;
      it.y = Math.round(pxToUnit(ny, state.page.unit, dpi) * 100) / 100;
      renderCanvas();
    }
    function up() {
      document.removeEventListener('mousemove', move);
      document.removeEventListener('mouseup', up);
      renderProps(); renderJson();
    }
    document.addEventListener('mousemove', move);
    document.addEventListener('mouseup', up);
  }
  function pxToUnit(px, unit, dpi) {
    var mm = (px * 25.4) / dpi;
    if (unit === 'mm') return mm;
    if (unit === 'in') return mm / 25.4;
    return px;
  }

  function select(idx) {
    selectedIdx = idx;
    renderCanvas();
    renderProps();
    if (idx >= 0) switchTab('elem');
  }

  function addItem(type) {
    var dpi = Number(state.page.dpi) || 203;
    var it = { type: type, x: 8, y: 8 };
    if (type === 'text') { it.text = '{{字段}}'; it.width = 35; it.height = 6; it.fontSize = 2.5; it.align = 'left'; it.color = '#000000'; it.bold = false; }
    else if (type === 'barcode') { it.code = '{{code}}'; it.width = 35; it.height = 10; it.barcodeType = 'Code128'; it.showText = true; it.color = '#000000'; }
    else if (type === 'line') { it.x = 8; it.y = 8; it.endX = 45; it.endY = 8; it.weight = 1; it.color = '#000000'; }
    else if (type === 'image') { it.path = ''; it.width = 20; it.height = 20; it.embedBase64 = null; }
    state.items.push(it);
    select(state.items.length - 1);
    renderJson();
  }

  function renderProps() {
    var box = document.getElementById('propFields');
    if (selectedIdx < 0 || !state.items[selectedIdx]) {
      box.style.display = 'none'; box.innerHTML = ''; return;
    }
    box.style.display = '';
    var it = state.items[selectedIdx];
    var h = '<div class="prop-del"><button class="btn-sm danger" id="propDel">删除此元素</button></div>';
    h += '<div class="fld-grid">';
    h += '<div class="fld"><label>类型</label><input value="' + esc(it.type) + '" disabled></div>';
    h += '<div class="fld"><label>X(' + (state.page.unit || 'mm') + ')</label><input type="number" step="0.1" data-k="x" value="' + (it.x || 0) + '"></div>';
    h += '<div class="fld"><label>Y(' + (state.page.unit || 'mm') + ')</label><input type="number" step="0.1" data-k="y" value="' + (it.y || 0) + '"></div>';
    if (it.type === 'text') {
      h += '<div class="fld fld-wide"><label>内容 text</label><input data-k="text" value="' + esc(it.text || '') + '"></div>';
      h += '<div class="fld"><label>宽 W</label><input type="number" step="0.1" data-k="width" value="' + (it.width || 0) + '"></div>';
      h += '<div class="fld"><label>高 H</label><input type="number" step="0.1" data-k="height" value="' + (it.height || 0) + '"></div>';
      h += '<div class="fld"><label>字号</label><input type="number" step="0.1" data-k="fontSize" value="' + (it.fontSize || 0) + '"></div>';
      h += '<div class="fld"><label>对齐</label><select data-k="align"><option value="left">left</option><option value="center">center</option><option value="right">right</option></select></div>';
      h += '<div class="fld"><label>字体</label><select data-k="fontFamily"><option value="sans-serif">sans-serif</option><option value="serif">serif</option><option value="monospace">monospace</option></select></div>';
      h += '<div class="fld"><label>颜色</label><input type="color" data-k="color" value="' + (it.color || '#000000') + '"></div>';
      h += '<div class="fld fld-check"><label>加粗</label><input type="checkbox" data-k="bold"' + (it.bold ? ' checked' : '') + '></div>';
    } else if (it.type === 'barcode') {
      h += '<div class="fld fld-wide"><label>码值 code</label><input data-k="code" value="' + esc(it.code || '') + '"></div>';
      h += '<div class="fld"><label>宽 W</label><input type="number" step="0.1" data-k="width" value="' + (it.width || 0) + '"></div>';
      h += '<div class="fld"><label>高 H</label><input type="number" step="0.1" data-k="height" value="' + (it.height || 0) + '"></div>';
      h += '<div class="fld"><label>类型</label><select data-k="barcodeType"><option value="Code128">Code128</option><option value="Code39">Code39</option><option value="QRCode">QRCode</option><option value="EAN13">EAN13</option><option value="PDF417">PDF417</option></select></div>';
      h += '<div class="fld"><label>颜色</label><input type="color" data-k="color" value="' + (it.color || '#000000') + '"></div>';
      h += '<div class="fld fld-check"><label>显示文字</label><input type="checkbox" data-k="showText"' + (it.showText ? ' checked' : '') + '></div>';
    } else if (it.type === 'line') {
      h += '<div class="fld"><label>终点X</label><input type="number" step="0.1" data-k="endX" value="' + (it.endX || 0) + '"></div>';
      h += '<div class="fld"><label>终点Y</label><input type="number" step="0.1" data-k="endY" value="' + (it.endY || 0) + '"></div>';
      h += '<div class="fld"><label>线宽</label><input type="number" step="0.1" data-k="weight" value="' + (it.weight || 1) + '"></div>';
      h += '<div class="fld"><label>颜色</label><input type="color" data-k="color" value="' + (it.color || '#000000') + '"></div>';
    } else if (it.type === 'image') {
      h += '<div class="fld fld-wide"><label>路径 path</label><input data-k="path" value="' + esc(it.path || '') + '"></div>';
      h += '<div class="fld"><label>宽 W</label><input type="number" step="0.1" data-k="width" value="' + (it.width || 0) + '"></div>';
      h += '<div class="fld"><label>高 H</label><input type="number" step="0.1" data-k="height" value="' + (it.height || 0) + '"></div>';
    }
    h += '</div>';
    box.innerHTML = h;
    // 回填 select
    if (it.type === 'text') box.querySelector('[data-k="align"]').value = it.align || 'left';
    if (it.type === 'text') box.querySelector('[data-k="fontFamily"]').value = it.fontFamily || 'sans-serif';
    if (it.type === 'barcode') box.querySelector('[data-k="barcodeType"]').value = it.barcodeType || 'Code128';
    // 绑定
    box.querySelectorAll('[data-k]').forEach(function (inp) {
      var ev = (inp.type === 'checkbox' || inp.tagName === 'SELECT') ? 'change' : 'input';
      inp.addEventListener(ev, function () {
        var k = inp.dataset.k;
        var v = inp.type === 'checkbox' ? inp.checked : (inp.type === 'number' ? Number(inp.value) : inp.value);
        if (inp.type === 'number' && inp.value === '') v = 0;
        state.items[selectedIdx][k] = v;
        renderCanvas(); renderJson();
      });
    });
    document.getElementById('propDel').onclick = function () {
      state.items.splice(selectedIdx, 1); selectedIdx = -1; renderAll();
    };
  }

  function renderJson() {
    // 前端不再展示模板 JSON 区
  }

  function currentTemplateJson() {
    return JSON.stringify({ page: state.page, items: state.items });
  }

  function save() {
    var box = document.getElementById('tplSaveName');
    var input = (box && box.value || '').trim().replace(/\.json$/i, '');
    var name = input || state.name || '';
    if (!name) { toast('请先填写模板名称', false); if (box) box.focus(); return; }
    state.name = name;
    api('/api/templates/' + encodeURIComponent(name), {
      method: 'POST', body: currentTemplateJson()
    }).then(r => r.ok ? r.json() : null).then(function (d) {
      if (d && d.saved) {
        toast('已保存：' + name, true);
        if (box) box.value = name; // 保留为当前名，可直接编辑
        loadList();
        document.getElementById('tplList').value = name;
      }
      else toast('保存失败', false);
    }).catch(function () { toast('保存失败', false); });
  }

  function del() {
    if (!state.name) { toast('当前为未保存模板', false); return; }
    if (!confirm('确认删除模板 ' + state.name + '？')) return;
    api('/api/templates/' + encodeURIComponent(state.name), { method: 'DELETE' })
      .then(function (r) { return r.ok ? r.json() : null; }).then(function (d) {
        if (d && d.deleted) { toast('已删除', true); loadList(); newTemplate(); }
        else toast('删除失败', false);
      });
  }

  function preview() {
    var fields = '{}';
    var box = document.getElementById('tplPreviewBox');
    var img = document.getElementById('tplPreviewImg');
    box.hidden = false;
    img.innerHTML = '<span class="muted">渲染中…</span>';
    api('/api/templates/preview', { method: 'POST', body: JSON.stringify({ template: currentTemplateJson(), fields: fields }) })
      .then(r => r.ok ? r.json() : null).then(function (d) {
        if (d && d.base64) img.innerHTML = '<img class="preview-img" alt="预览" src="data:image/png;base64,' + d.base64 + '">';
        else img.innerHTML = '<span class="muted">预览失败：' + esc((d && d.error) || '未知错误') + '</span>';
      }).catch(function () { img.innerHTML = '<span class="muted">预览失败</span>'; });
  }

  function exportJson() {
    var blob = new Blob([currentTemplateJson()], { type: 'application/json' });
    var a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = (state.name || 'template') + '.json';
    a.click();
    URL.revokeObjectURL(a.href);
  }

  // 缩放控件绑定
  function bindZoom() {
    var sel = document.getElementById('tplZoom');
    var zooms = [0, 0.5, 0.75, 1, 1.25, 1.5, 2];
    if (sel) sel.onchange = function () {
      manualScale = parseFloat(this.value) || 0;
      renderCanvas();
    };
    document.querySelectorAll('[data-zoom]').forEach(function (b) {
      b.onclick = function () {
        var dir = b.dataset.zoom;
        var idx = zooms.indexOf(manualScale);
        if (idx < 0) idx = 0;
        idx = dir === '+' ? idx + 1 : idx - 1;
        idx = Math.max(0, Math.min(zooms.length - 1, idx));
        manualScale = zooms[idx];
        if (sel) sel.value = String(manualScale);
        renderCanvas();
      };
    });
  }

  // 页面设置绑定
  function bindPage() {
    on('pgPreset', 'change', function () {
      var p = PAGE_PRESETS[this.value];
      if (!p) { renderPageInputs(); return; }
      state.page.unit = 'mm';
      state.page.width = p.w; state.page.height = p.h;
      renderPageInputs(); renderAll();
    });
    on('pgUnit', 'change', function () { state.page.unit = this.value; renderAll(); });
    on('pgW', 'input', function () { state.page.width = Number(this.value) || 0; renderCanvas(); renderJson(); });
    on('pgH', 'input', function () { state.page.height = Number(this.value) || 0; renderCanvas(); renderJson(); });
    on('pgDpi', 'input', function () { state.page.dpi = Number(this.value) || 203; renderAll(); });
    on('pgBg', 'input', function () { state.page.backgroundColor = this.value; renderCanvas(); renderJson(); });
  }

  function bindToolbar() {
    on('tplList', 'change', function () { loadTemplate(this.value); });
    on('tplSave', 'click', save);
    on('tplDelete', 'click', del);
    on('tplPreview', 'click', preview);
    on('tplExport', 'click', exportJson);
    document.querySelectorAll('[data-add]').forEach(function (b) {
      b.onclick = function () { addItem(b.dataset.add); };
    });
    on('tplCanvas', 'click', function (e) {
      if (e.target.id === 'tplCanvas') { selectedIdx = -1; renderCanvas(); renderProps(); }
    });
  }

  function switchTab(name) {
    document.querySelectorAll('.tpl-tab').forEach(function (t) {
      t.classList.toggle('active', t.dataset.tab === name);
    });
    document.querySelectorAll('.tpl-tab-panel').forEach(function (p) {
      p.hidden = p.dataset.panel !== name;
    });
  }
  function bindTabs() {
    document.querySelectorAll('.tpl-tab').forEach(function (t) {
      t.onclick = function () { switchTab(t.dataset.tab); };
    });
  }

  var inited = false;
  function on(id, evt, fn) { var el = document.getElementById(id); if (el) el['on' + evt] = fn; }
  function init() {
    if (inited) return; inited = true;
    try {
      bindPage();
      bindZoom();
      bindToolbar();
      bindTabs();
    } catch (err) {
      console.error('Tpl.init failed:', err);
      inited = false; // 允许重试，避免一次性失败后永远绑不上
      throw err;
    }
  }

  return {
    ensureLoaded: function () {
      if (!inited) { init(); loaded = true; loadList(); newTemplate(); return; }
      if (!loaded) { loaded = true; loadList(); newTemplate(); return; }
      loadList(); // 再次进入：仅刷新列表，不重置当前编辑内容
    },
    init: init
  };
})();

