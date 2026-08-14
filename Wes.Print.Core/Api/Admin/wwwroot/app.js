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
function fillCfg(d) {
  if (!d) return;
  host.value = d.host || '';
  port.value = d.port || '';
  user.value = d.userName || '';
  pass.value = d.password || '';
  queue.value = d.queue || '';
}
function loadPrinters(sel) {
  api('/api/printers').then(r => r.ok ? r.json() : null)
    .then(function (p) { fillPrinters(p.printers, p.defaultPrinter, sel); });
}
function saveCfg() {
  var b = {
    key: 'default', type: 'RabbitMQ', enabled: true,
    host: host.value, port: Number(port.value) || 0, userName: user.value,
    password: pass.value, queue: queue.value, autoAck: true, printerName: printer.value
  };
  api('/api/mq/config', { method: 'POST', body: JSON.stringify(b) })
    .then(r => r.ok ? toast('已保存', true) : toast('保存失败', false));
}
[host, port, user, pass, queue].forEach(function (el) { el.addEventListener('change', saveCfg); });

printer.onchange = function () {
  api('/api/mq/config').then(r => r.ok ? r.json() : null).then(function (d) {
    d = d || {}; d.key = d.key || 'default'; d.type = d.type || 'RabbitMQ'; d.printerName = printer.value;
    api('/api/mq/config', { method: 'POST', body: JSON.stringify(d) })
      .then(r => r.ok ? toast('打印机已保存', true) : toast('保存失败', false));
  });
};

mqOn.onchange = function () {
  api('/api/settings/mq-enabled', {
    method: 'POST',
    body: JSON.stringify({ key: 'mq.enabled', value: mqOn.checked ? 'true' : 'false' })
  }).then(function () { mqBox.style.display = mqOn.checked ? 'block' : 'none'; });
};

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

mqToggle.onclick = function () {
  if (mqToggle.dataset.mode === 'connect') {
    api('/api/mq/connect', { method: 'POST' }).then(r => r.ok ? r.json() : null).then(function (d) {
      if (d && d.ok) { toast('已发起连接', true); } else { toast('连接失败：' + (d && d.error || ''), false); }
      refreshMqState();
    });
  } else {
    api('/api/mq/disconnect', { method: 'POST' }).then(function () { toast('已断开', true); refreshMqState(); });
  }
};

function refreshMqState() {
  api('/api/mq/status').then(r => r.ok ? r.json() : null).then(function (s) {
    if (!s) return;
    var map = {
      Disabled: ['muted', 'MQ 消费未启用'], NoConfig: ['warn', '已启用，未配置主机/队列'],
      Idle: ['muted', '已启用，等待连接'], Connecting: ['warn', '连接中...'],
      Connected: ['ok', '已连接，正在消费'], Reconnecting: ['warn', '重连中...'],
      Failed: ['err', '连接失败'], Stopped: ['muted', '已停止']
    };
    var m = map[s.state] || ['muted', s.message];
    mqState.className = 'dot ' + m[0]; mqState.title = s.message || m[1];
    mqState2.className = 'dot ' + m[0];
    mqText.textContent = s.message || m[1];
    var canConnect = (s.state === 'Idle' || s.state === 'Disabled' || s.state === 'NoConfig' || s.state === 'Failed' || s.state === 'Stopped');
    var canDisconnect = (s.state === 'Connected' || s.state === 'Connecting' || s.state === 'Reconnecting');
    if (canConnect) {
      mqToggle.dataset.mode = 'connect'; mqToggle.textContent = '连接';
      mqToggle.className = 'btn-connect'; mqToggle.disabled = !s.enabled;
    } else if (canDisconnect) {
      mqToggle.dataset.mode = 'disconnect'; mqToggle.textContent = '断开';
      mqToggle.className = 'btn-disconnect'; mqToggle.disabled = false;
    } else {
      mqToggle.dataset.mode = 'connect'; mqToggle.textContent = '连接';
      mqToggle.className = 'btn-connect'; mqToggle.disabled = true;
    }
  });
}

// 初始化
loadPrinters();
api('/api/mq/config').then(r => r.ok ? r.json() : null).then(function (d) {
  fillCfg(d);
  if (d) loadPrinters(d.printerName);
});
// 从后端读取已保存的启用状态来还原开关，避免硬编码覆盖
api('/api/settings/mq-enabled').then(r => r.ok ? r.json() : null).then(function (d) {
  if (d && d.value === 'true') mqOn.checked = true;
  else if (d && d.value === 'false') mqOn.checked = false;
  if (!mqOn.checked) mqBox.style.display = 'none';
});
q();
refreshMqState();
setInterval(refreshMqState, 5000);
