/* Wes.PrintService 管理前端 */
(function () {
  'use strict';
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));

  /* ---------- 通用工具 ---------- */
  function toast(msg, type) {
    const t = $('#toast'); if (!t) return;
    t.textContent = msg;
    t.style.borderLeftColor = type === 'err' ? 'var(--err)' : type === 'ok' ? 'var(--ok)' : 'var(--accent)';
    t.classList.add('show');
    clearTimeout(toast._t);
    toast._t = setTimeout(() => t.classList.remove('show'), 2600);
  }
  async function api(path, opts) {
    opts = opts || {};
    const res = await fetch(path, {
      headers: { 'Content-Type': 'application/json' },
      ...opts,
      body: opts.body != null ? JSON.stringify(opts.body) : undefined
    });
    let data = null;
    try { data = await res.json(); } catch (_) {}
    if (!res.ok) {
      const msg = (data && (data.message || data.error)) || ('HTTP ' + res.status);
      throw new Error(msg);
    }
    return data;
  }

  /* ---------- 打印机 ---------- */
  async function loadPrinters() {
    try {
      const d = await api('/api/printers');
      const list = d.printers || [];
      const def = d.defaultPrinter || '';
      const sel = $('#printer');
      sel.innerHTML = '<option value="">默认打印机</option>' +
        list.map(p => `<option value="${esc(p)}"${p === def ? ' selected' : ''}>${esc(p)}${p === def ? '（默认）' : ''}</option>`).join('');
    } catch (e) { toast('加载打印机失败：' + e.message, 'err'); }
  }

  async function saveDefaultPrinter() {
    const v = $('#printer').value;
    try {
      await api('/api/printer/default', { method: 'POST', body: { value: v } });
      toast(v ? ('默认打印机已设为：' + v) : '已恢复使用系统默认打印机', 'ok');
    } catch (e) { toast('保存默认打印机失败：' + e.message, 'err'); }
  }

  /* ---------- 消息队列配置 ---------- */
  const MQ_KEYS = ['rabbitmq', 'kafka'];
  const MQ_TYPE = { rabbitmq: 'RabbitMQ', kafka: 'Kafka' };
  function mqCard(key) {
    return {
      key: key,
      type: MQ_TYPE[key],
      enabled: $('.mq-on[data-key="' + key + '"]', $('#view-mq')).checked,
      host: $('.f-host', mqEl(key)).value.trim(),
      port: numOr($('.f-port', mqEl(key)).value),
      userName: $('.f-user', mqEl(key)).value.trim(),
      password: $('.f-pass', mqEl(key)).value,
      queue: $('.f-queue', mqEl(key)).value.trim(),
      bootstrapServers: $('.f-bootstrap', mqEl(key)).value.trim(),
      groupId: $('.f-group', mqEl(key)).value.trim(),
      autoAck: true
    };
  }
  function mqEl(key) { return $('.mq-body[data-key="' + key + '"]', $('#view-mq')); }
  function numOr(v, d) { const n = Number(v); return v != null && v !== '' && !isNaN(n) ? n : (d != undefined ? d : null); }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

  async function loadMq() {
    try {
      // 批量取连接/启用状态，再逐个取配置详情填充表单
      const status = await api('/api/mq/status');
      const statusMap = Object.fromEntries((status.items || []).map(s => [s.key, s]));
      for (const key of MQ_KEYS) {
        const cfg = await api('/api/mq/config?key=' + encodeURIComponent(key));
        const st = statusMap[key] || {};
        const el = mqEl(key);
        $('.mq-on[data-key="' + key + '"]', $('#view-mq')).checked = !!cfg.enabled;
        setVal($('.f-host', el), cfg.host); setVal($('.f-port', el), cfg.port);
        setVal($('.f-user', el), cfg.userName); setVal($('.f-pass', el), cfg.password);
        setVal($('.f-queue', el), cfg.queue); setVal($('.f-bootstrap', el), cfg.bootstrapServers);
        setVal($('.f-group', el), cfg.groupId);
        setMqStatus(key, st.connected ? 'ok' : 'muted', st.connected ? '已连接' : '未连接');
        setCardOn(key, !!cfg.enabled || !!st.enabled);
        $('.mq-toggle[data-key="' + key + '"]', $('#view-mq')).textContent = st.connected ? '断开' : '连接';
      }
    } catch (e) { toast('加载 MQ 配置失败：' + e.message, 'err'); }
  }
  function setVal(input, v) { if (input) input.value = v == null ? '' : v; }
  function setCardOn(key, on) {
    const card = key === 'rabbitmq' ? $('#card-rabbitmq') : $('#card-kafka');
    card.classList.toggle('on', on);
  }
  function setMqStatus(key, cls, text) {
    $$('.mq-dot[data-key="' + key + '"]').forEach(d => { d.className = 'dot ' + cls + ' mq-dot'; });
    $$('.mq-dot2[data-key="' + key + '"]').forEach(d => { d.className = 'dot ' + cls + ' mq-dot2'; });
    const t = $('.mq-text[data-key="' + key + '"]', $('#view-mq')); if (t) t.textContent = text;
  }

  async function saveMq() {
    try {
      for (const key of MQ_KEYS) {
        await api('/api/mq/config', { method: 'POST', body: mqCard(key) });
      }
      toast('MQ 配置已保存', 'ok');
      await loadMq();
    } catch (e) { toast('保存失败：' + e.message, 'err'); }
  }

  async function toggleMq(key) {
    const btn = $('.mq-toggle[data-key="' + key + '"]', $('#view-mq'));
    const connecting = btn.textContent === '连接';
    setMqStatus(key, 'warn', connecting ? '连接中…' : '断开中…');
    try {
      await api('/api/mq/' + (connecting ? 'connect' : 'disconnect') + '?key=' + encodeURIComponent(key), { method: 'POST' });
      toast((connecting ? '已连接 ' : '已断开 ') + key, 'ok');
      await loadMq();
    } catch (e) {
      setMqStatus(key, 'err', '失败');
      toast(key + ' 操作失败：' + e.message, 'err');
    }
  }

  /* ---------- 打印记录 ---------- */
  let recPage = 1;
  async function loadRecords(page) {
    recPage = page || recPage;
    const ch = $('#channel').value, st = $('#status').value;
    const q = new URLSearchParams({ page: recPage, pageSize: 12, channel: ch, status: st });
    try {
      const d = await api('/api/records?' + q.toString());
      const rows = $('#rows');
      if (!d.items || !d.items.length) {
        rows.innerHTML = '<tr><td colspan="8" style="color:var(--muted)">暂无记录</td></tr>';
      } else {
        rows.innerHTML = d.items.map(r => {
          const chCls = (r.channel || '').toLowerCase().includes('rabbit') || (r.channel || '').toLowerCase().includes('kafka')
            ? 'mq' : (r.channel || '').toLowerCase() === 'api' ? 'api' : 'other';
          const stCls = (r.status || '').toLowerCase();
          return `<tr>
            <td><span class="chicon ${chCls}">${esc(r.channel || '-')}</span></td>
            <td>${esc(r.templateKind || '-')}</td>
            <td class="ellipsis" title="${esc(r.templateRef || '')}">${esc(r.templateRef || '-')}</td>
            <td class="ellipsis" title="${esc(r.printerName || '')}">${esc(r.printerName || '-')}</td>
            <td>${esc((r.createdAt || '').replace('T', ' ').slice(0, 19))}</td>
            <td><span class="tag ${stCls}">${esc(r.status || '-')}</span></td>
            <td class="ellipsis" title="${esc(r.message || '')}">${esc(r.message || '-')}</td>
            <td><button class="lk-btn" data-id="${esc(r.id)}">查看</button></td>
          </tr>`;
        }).join('');
        $$('#rows .lk-btn').forEach(b => b.onclick = () => showRecord(b.dataset.id));
      }
      const total = d.total || 0, pages = Math.max(1, Math.ceil(total / 12));
      $('#pager').innerHTML = `<span class="pg-info">共 ${total} 条 · 第 ${recPage}/${pages} 页</span>` +
        `<button class="pg-btn" data-pg="prev" ${recPage <= 1 ? 'disabled' : ''}>上一页</button>` +
        `<button class="pg-btn" data-pg="next" ${recPage >= pages ? 'disabled' : ''}>下一页</button>`;
      $$('#pager .pg-btn').forEach(b => b.onclick = () => {
        if (b.dataset.pg === 'prev' && recPage > 1) loadRecords(recPage - 1);
        if (b.dataset.pg === 'next') loadRecords(recPage + 1);
      });
    } catch (e) { toast('加载记录失败：' + e.message, 'err'); }
  }

  async function showRecord(id) {
    try {
      const r = await api('/api/records/' + id);
      const body = $('#modalBody');
      const fields = [
        ['ID', r.id], ['渠道', r.channel], ['类型', r.templateKind], ['模板', r.templateRef],
        ['打印机', r.printerName], ['状态', r.status],
        ['时间', (r.createdAt || '').replace('T', ' ')], ['信息', r.message]
      ];
      const kv = fields.map(([k, v]) => `<div class="kv"><span class="k">${k}</span><span class="v ${v ? '' : 'muted'}">${v ? esc(v) : '—'}</span></div>`).join('');
      const payload = r.request ? `<div class="kv"><span class="k">参数</span><span class="v muted">展开 ↓</span></div><pre class="pre code">${esc(typeof r.request === 'string' ? r.request : JSON.stringify(r.request, null, 2))}</pre>` : '';
      body.innerHTML = kv + payload;
      openModal();
    } catch (e) { toast('详情失败：' + e.message, 'err'); }
  }

  function openModal() { $('#modal').hidden = false; }
  function closeModal() { $('#modal').hidden = true; }

  /* ---------- 系统设置 ---------- */
  async function loadRetention() {
    try { const d = await api('/api/settings/record-retention'); setVal($('#retention'), d.value); } catch (_) {}
  }
  async function saveRetention() {
    const days = numOr($('#retention').value, 30);
    try { await api('/api/settings/record-retention', { method: 'POST', body: { value: days.toString() } }); toast('已保存保留天数', 'ok'); }
    catch (e) { toast('保存失败：' + e.message, 'err'); }
  }
  async function purgeNow() {
    if (!confirm('确认立即清理过期打印记录？')) return;
    try { const d = await api('/api/records/purge', { method: 'POST' }); toast('已清理 ' + (d.removed || 0) + ' 条', 'ok'); loadRecords(); }
    catch (e) { toast('清理失败：' + e.message, 'err'); }
  }

  /* ---------- 模板设计器 ---------- */
  const PX_PER_MM = 3.78;   // 96dpi 下 1mm ≈ 3.78px（仅用于编辑器视觉尺寸）
  const PAGE_PRESETS = {
    a4: { w: 210, h: 297 }, a5: { w: 148, h: 210 }, a6: { w: 105, h: 148 },
    'label-40': { w: 40, h: 30 }, 'label-60': { w: 60, h: 40 },
    'label-80': { w: 80, h: 50 }, 'label-100': { w: 100, h: 60 },
    'pos-80': { w: 80, h: 200 }
  };
  let tpl = null;          // 当前模板对象
  let tplZoom = '0';       // '0' = 自适应
  let tplViews = ['mq', 'records', 'templates', 'settings'];

  function ensureTpl() {
    if (tpl) return tpl;
    tpl = {
      page: { width: 80, height: 50, unit: 'mm', dpi: 203, background: '#ffffff' },
      items: [],
      name: ''
    };
    return tpl;
  }

  function switchTab(name) {
    $$('.tpl-tab').forEach(b => b.classList.toggle('active', b.dataset.tab === name));
    $$('.tpl-tab-panel').forEach(p => { p.hidden = p.dataset.panel !== name; });
  }
  function bindTabs() {
    $$('.tpl-tab').forEach(b => b.onclick = () => switchTab(b.dataset.tab));
  }

  function bindAdd() {
    $$('.add-toolbar [data-add]').forEach(btn => {
      btn.onclick = () => {
        const type = btn.dataset.add;
        addItem(type);
        switchTab('elem');
      };
    });
  }

  function addItem(type) {
    const t = ensureTpl();
    const base = { id: 'i' + Date.now() + Math.floor(Math.random() * 1000), type, x: 5, y: 5 };
    let item;
    if (type === 'text') {
      item = { ...base, text: '文本内容', fontSize: 4, bold: false, color: '#000000', align: 'left' };
    } else if (type === 'barcode') {
      item = { ...base, value: '1234567890', codeType: 'CODE128', barWidth: 0.4, barHeight: 10, showText: true, fontSize: 3 };
    } else if (type === 'line') {
      item = { ...base, width: 45, height: 0, lineWidth: 0.3, color: '#000000', direction: 'h' };
    } else if (type === 'image') {
      item = { ...base, src: '', width: 20, height: 20, fit: 'contain' };
    } else {
      return;
    }
    if (type === 'text' || type === 'barcode' || type === 'image') {
      if (item.width == null) item.width = 35;
      if (item.height == null) item.height = (type === 'barcode' ? 10 : type === 'image' ? 20 : 6);
    }
    t.items.push(item);
    renderCanvas();
    selectItem(item.id);
  }

  function selectItem(id) {
    const t = ensureTpl();
    t._sel = id;
    renderCanvas();
    renderProps();
    switchTab('elem');
  }

  function getSel() {
    const t = ensureTpl();
    return t.items.find(i => i.id === t._sel) || null;
  }

  function unitToMm(v, unit) {
    v = Number(v) || 0;
    if (unit === 'px') return v / (Number(tpl.page.dpi) || 203) * 25.4;
    if (unit === 'in') return v * 25.4;
    return v;
  }

  function mmToPx(v) { return v * PX_PER_MM; }

  function renderCanvas() {
    const t = ensureTpl();
    const canvas = $('#tplCanvas');
    const scaler = $('#tplCanvasScaler');
    const wrap = $('#tplCanvasWrap');
    const pgW = mmToPx(unitToMm(t.page.width, t.page.unit));
    const pgH = mmToPx(unitToMm(t.page.height, t.page.unit));
    canvas.style.width = pgW + 'px';
    canvas.style.height = pgH + 'px';
    canvas.style.background = t.page.background || '#ffffff';
    const scale = computeScale(pgW, pgH, wrap);
    applyScale(scale);
    canvas.innerHTML = '';
    t.items.forEach(it => {
      const el = document.createElement('div');
      el.className = 'tpl-item' + (it.id === t._sel ? ' sel' : '');
      el.style.left = mmToPx(unitToMm(it.x, t.page.unit)) + 'px';
      el.style.top = mmToPx(unitToMm(it.y, t.page.unit)) + 'px';
      const w = mmToPx(unitToMm(it.width || 0, t.page.unit));
      const h = mmToPx(unitToMm(it.height || 0, t.page.unit));
      if (it.type === 'line') {
        el.style.width = w + 'px';
        el.style.height = '0px';
        const lw = Math.max(1, mmToPx(unitToMm(it.lineWidth || 0.3, t.page.unit)));
        el.style.borderTop = lw + 'px solid ' + (it.color || '#000');
        if (it.direction === 'v') {
          el.style.width = '0px';
          el.style.height = h + 'px';
          el.style.borderTop = 'none';
          el.style.borderLeft = lw + 'px solid ' + (it.color || '#000');
        }
      } else {
        el.style.width = (w || 10) + 'px';
        el.style.height = (h || 10) + 'px';
        if (it.type === 'text') {
          const tx = document.createElement('div');
          tx.className = 'tpl-text';
          tx.textContent = it.text || '文本';
          tx.style.fontSize = (mmToPx(unitToMm(it.fontSize || 4, t.page.unit))) + 'px';
          tx.style.color = it.color || '#000';
          tx.style.fontWeight = it.bold ? 'bold' : 'normal';
          tx.style.textAlign = it.align || 'left';
          el.appendChild(tx);
        } else if (it.type === 'barcode') {
          const tag = document.createElement('div');
          tag.className = 'tpl-tag';
          tag.textContent = (it.codeType || 'CODE128') + ' · ' + (it.value || '');
          el.appendChild(tag);
        } else if (it.type === 'image') {
          if (it.src) {
            const img = document.createElement('img');
            img.src = it.src; img.style.width = '100%'; img.style.height = '100%';
            img.style.objectFit = it.fit || 'contain';
            el.appendChild(img);
          } else {
            const tag = document.createElement('div');
            tag.className = 'tpl-tag'; tag.textContent = '图片'; el.appendChild(tag);
          }
        }
      }
      el.onmousedown = (e) => { e.preventDefault(); selectItem(it.id); startDrag(it, e); };
      canvas.appendChild(el);
    });
  }

  function computeScale(pgW, pgH, wrap) {
    if (tplZoom !== '0') return Number(tplZoom);
    const pad = 52;
    const aw = (wrap.clientWidth || 600) - pad;
    const ah = (wrap.clientHeight || 400) - pad;
    if (pgW <= 0 || pgH <= 0) return 1;
    return Math.min(aw / pgW, ah / pgH, 1);
  }
  function applyScale(scale) {
    scale = scale || 1;
    const scaler = $('#tplCanvasScaler');
    const canvas = $('#tplCanvas');
    const pgW = parseFloat(canvas.style.width) || 0;
    const pgH = parseFloat(canvas.style.height) || 0;
    $('#tplCanvasScaler').style.width = (pgW * scale) + 'px';
    $('#tplCanvasScaler').style.height = (pgH * scale) + 'px';
    $('#tplCanvas').style.transform = 'scale(' + scale + ')';
  }

  function startDrag(it, e) {
    const t = ensureTpl();
    const startX = e.clientX, startY = e.clientY;
    const ox = unitToMm(it.x, t.page.unit), oy = unitToMm(it.y, t.page.unit);
    function move(ev) {
      const dx = (ev.clientX - startX) / PX_PER_MM;
      const dy = (ev.clientY - startY) / PX_PER_MM;
      it.x = Math.max(0, ox + dx);
      it.y = Math.max(0, oy + dy);
      renderCanvas();
    }
    function up() {
      document.removeEventListener('mousemove', move);
      document.removeEventListener('mouseup', up);
      renderProps();
    }
    document.addEventListener('mousemove', move);
    document.addEventListener('mouseup', up);
  }

  function renderPageInputs() {
    const t = ensureTpl();
    $('#pgW').value = t.page.width;
    $('#pgH').value = t.page.height;
    $('#pgDpi').value = t.page.dpi;
    $('#pgUnit').value = t.page.unit;
    $('#pgBg').value = toHex(t.page.background);
    // 反查预设
    let preset = '';
    for (const k in PAGE_PRESETS) {
      if (Math.abs(PAGE_PRESETS[k].w - Number(t.page.width)) < 0.01 && Math.abs(PAGE_PRESETS[k].h - Number(t.page.height)) < 0.01) { preset = k; break; }
    }
    $('#pgPreset').value = preset;
  }

  function toHex(c) {
    if (!c) return '#ffffff';
    if (c[0] === '#') return c.length === 4 ? '#' + c[1] + c[1] + c[2] + c[2] + c[3] + c[3] : c;
    return c;
  }

  function bindPage() {
    const upd = () => { const t = ensureTpl(); t.page.width = Number($('#pgW').value) || 0; t.page.height = Number($('#pgH').value) || 0; renderCanvas(); };
    ['#pgW', '#pgH'].forEach(s => $(s).addEventListener('input', upd));
    $('#pgDpi').addEventListener('input', () => { ensureTpl().page.dpi = Number($('#pgDpi').value) || 203; });
    $('#pgUnit').addEventListener('change', () => { ensureTpl().page.unit = $('#pgUnit').value; renderCanvas(); });
    $('#pgBg').addEventListener('input', () => { ensureTpl().page.background = $('#pgBg').value; renderCanvas(); });
    $('#pgPreset').addEventListener('change', () => {
      const p = PAGE_PRESETS[$('#pgPreset').value];
      if (!p) return;
      $('#pgW').value = p.w; $('#pgH').value = p.h;
      ensureTpl().page.width = p.w; ensureTpl().page.height = p.h;
      $('#pgUnit').value = 'mm'; ensureTpl().page.unit = 'mm';
      renderCanvas();
    });
  }

  function renderProps() {
    const t = ensureTpl();
    const box = $('#propFields');
    const it = getSel();
    if (!it) { box.style.display = 'none'; box.innerHTML = ''; return; }
    box.style.display = 'block';
    let html = `<div class="prop-del"><button class="btn-sm danger" id="propDel">删除此元素</button></div>`;
    html += `<div class="fld"><label>类型</label><input value="${esc(it.type)}" disabled></div>`;
    html += `<div class="fld-grid">
      <div class="fld"><label>X</label><input id="pX" type="number" step="0.1" value="${it.x}"></div>
      <div class="fld"><label>Y</label><input id="pY" type="number" step="0.1" value="${it.y}"></div>
    </div>`;
    if (it.type === 'text') {
      html += `<div class="fld"><label>文本</label><textarea id="pText" rows="2" class="fld-wide">${esc(it.text || '')}</textarea></div>`;
      html += `<div class="fld-grid">
        <div class="fld"><label>字号(mm)</label><input id="pFont" type="number" step="0.1" value="${it.fontSize}"></div>
        <div class="fld"><label>颜色</label><input id="pColor" type="color" value="${toHex(it.color)}"></div>
      </div>`;
      html += `<div class="fld fld-check"><label>加粗</label><input id="pBold" type="checkbox" ${it.bold ? 'checked' : ''}></div>`;
      html += `<div class="fld"><label>对齐</label><select id="pAlign"><option value="left"${it.align === 'left' ? ' selected' : ''}>左</option><option value="center"${it.align === 'center' ? ' selected' : ''}>中</option><option value="right"${it.align === 'right' ? ' selected' : ''}>右</option></select></div>`;
    } else if (it.type === 'barcode') {
      html += `<div class="fld"><label>内容</label><input id="pVal" value="${esc(it.value || '')}"></div>`;
      html += `<div class="fld"><label>码制</label><select id="pCode"><option value="CODE128"${it.codeType === 'CODE128' ? ' selected' : ''}>CODE128</option><option value="QRCODE"${it.codeType === 'QRCODE' ? ' selected' : ''}>QRCODE</option><option value="EAN13"${it.codeType === 'EAN13' ? ' selected' : ''}>EAN13</option></select></div>`;
      html += `<div class="fld-grid">
        <div class="fld"><label>线宽(mm)</label><input id="pBw" type="number" step="0.1" value="${it.barWidth}"></div>
        <div class="fld"><label>高度(mm)</label><input id="pBh" type="number" step="0.1" value="${it.barHeight}"></div>
      </div>`;
      html += `<div class="fld fld-check"><label>显示文字</label><input id="pShow" type="checkbox" ${it.showText ? 'checked' : ''}></div>`;
      html += `<div class="fld"><label>字号(mm)</label><input id="pBfs" type="number" step="0.1" value="${it.fontSize}"></div>`;
    } else if (it.type === 'line') {
      html += `<div class="fld-grid">
        <div class="fld"><label>长度(mm)</label><input id="pLw" type="number" step="0.1" value="${it.width}"></div>
        <div class="fld"><label>线宽(mm)</label><input id="pLl" type="number" step="0.1" value="${it.lineWidth}"></div>
      </div>`;
      html += `<div class="fld"><label>方向</label><select id="pDir"><option value="h"${it.direction !== 'v' ? ' selected' : ''}>水平</option><option value="v"${it.direction === 'v' ? ' selected' : ''}>垂直</option></select></div>`;
      html += `<div class="fld"><label>颜色</label><input id="pLc" type="color" value="${toHex(it.color)}"></div>`;
    } else if (it.type === 'image') {
      html += `<div class="fld"><label>图片地址/Base64</label><input id="pSrc" value="${esc(it.src || '')}"></div>`;
      html += `<div class="fld-grid">
        <div class="fld"><label>宽(mm)</label><input id="pIw" type="number" step="0.1" value="${it.width}"></div>
        <div class="fld"><label>高(mm)</label><input id="pIh" type="number" step="0.1" value="${it.height}"></div>
      </div>`;
      html += `<div class="fld"><label>填充</label><select id="pFit"><option value="contain"${it.fit === 'contain' ? ' selected' : ''}>contain</option><option value="fill"${it.fit === 'fill' ? ' selected' : ''}>fill</option><option value="cover"${it.fit === 'cover' ? ' selected' : ''}>cover</option></select></div>`;
    }
    // 宽高（非 line 显示独立宽高）
    if (it.type !== 'line') {
      html += `<div class="fld-grid">
        <div class="fld"><label>宽(mm)</label><input id="pW" type="number" step="0.1" value="${it.width}"></div>
        <div class="fld"><label>高(mm)</label><input id="pH" type="number" step="0.1" value="${it.height}"></div>
      </div>`;
    }
    box.innerHTML = html;

    $('#propDel').onclick = () => {
      const idx = t.items.findIndex(x => x.id === it.id);
      if (idx >= 0) t.items.splice(idx, 1);
      t._sel = null; renderCanvas(); renderProps();
    };
    const bind = (sel, ev, fn) => { const el = $(sel); if (el) el.addEventListener(ev, fn); };
    bind('#pX', 'input', e => { it.x = Number(e.target.value) || 0; renderCanvas(); });
    bind('#pY', 'input', e => { it.y = Number(e.target.value) || 0; renderCanvas(); });
    if (it.type === 'text') {
      bind('#pText', 'input', e => { it.text = e.target.value; renderCanvas(); });
      bind('#pFont', 'input', e => { it.fontSize = Number(e.target.value) || 4; renderCanvas(); });
      bind('#pColor', 'input', e => { it.color = e.target.value; renderCanvas(); });
      bind('#pBold', 'change', e => { it.bold = e.target.checked; renderCanvas(); });
      bind('#pAlign', 'change', e => { it.align = e.target.value; renderCanvas(); });
    } else if (it.type === 'barcode') {
      bind('#pVal', 'input', e => { it.value = e.target.value; renderCanvas(); });
      bind('#pCode', 'change', e => { it.codeType = e.target.value; renderCanvas(); });
      bind('#pBw', 'input', e => { it.barWidth = Number(e.target.value) || 0.4; renderCanvas(); });
      bind('#pBh', 'input', e => { it.barHeight = Number(e.target.value) || 10; renderCanvas(); });
      bind('#pShow', 'change', e => { it.showText = e.target.checked; renderCanvas(); });
      bind('#pBfs', 'input', e => { it.fontSize = Number(e.target.value) || 3; renderCanvas(); });
    } else if (it.type === 'line') {
      bind('#pLw', 'input', e => { it.width = Number(e.target.value) || 0; renderCanvas(); });
      bind('#pLl', 'input', e => { it.lineWidth = Number(e.target.value) || 0.3; renderCanvas(); });
      bind('#pDir', 'change', e => { it.direction = e.target.value; renderCanvas(); });
      bind('#pLc', 'input', e => { it.color = e.target.value; renderCanvas(); });
    } else if (it.type === 'image') {
      bind('#pSrc', 'input', e => { it.src = e.target.value; renderCanvas(); });
      bind('#pIw', 'input', e => { it.width = Number(e.target.value) || 20; renderCanvas(); });
      bind('#pIh', 'input', e => { it.height = Number(e.target.value) || 20; renderCanvas(); });
      bind('#pFit', 'change', e => { it.fit = e.target.value; renderCanvas(); });
    }
    if (it.type !== 'line') {
      bind('#pW', 'input', e => { it.width = Number(e.target.value) || 0; renderCanvas(); });
      bind('#pH', 'input', e => { it.height = Number(e.target.value) || 0; renderCanvas(); });
    }
  }

  async function loadTemplates() {
    try {
      const d = await api('/api/templates');
      const list = d.items || [];
      const sel = $('#tplList');
      sel.innerHTML = '<option value="">（新建空白模板）</option>' +
        list.map(t => `<option value="${esc(t.name)}">${esc(t.name)}</option>`).join('');
    } catch (e) { toast('加载模板失败：' + e.message, 'err'); }
  }

  async function loadTemplate(name) {
    try {
      const d = await api('/api/templates/' + encodeURIComponent(name));
      tpl = {
        page: d.page || { width: 80, height: 50, unit: 'mm', dpi: 203, background: '#ffffff' },
        items: d.items || [],
        name: d.name || name || '',
        _sel: null
      };
      $('#tplSaveName').value = tpl.name;
      renderPageInputs();
      renderCanvas();
      renderProps();
      switchTab('page');
    } catch (e) { toast('读取模板失败：' + e.message, 'err'); }
  }

  function newTemplate() {
    tpl = { page: { width: 80, height: 50, unit: 'mm', dpi: 203, background: '#ffffff' }, items: [], name: '', _sel: null };
    $('#tplSaveName').value = '';
    $('#tplList').value = '';
    renderPageInputs(); renderCanvas(); renderProps(); switchTab('page');
  }

  async function saveTemplate() {
    const name = $('#tplSaveName').value.trim();
    if (!name) { toast('请填写模板名', 'err'); return; }
    const t = ensureTpl();
    const body = { page: t.page, items: t.items };
    try {
      await api('/api/templates/' + encodeURIComponent(name), { method: 'POST', body });
      toast('模板已保存', 'ok');
      await loadTemplates();
      $('#tplList').value = name;
    } catch (e) { toast('保存失败：' + e.message, 'err'); }
  }

  async function deleteTemplate() {
    const name = $('#tplList').value || $('#tplSaveName').value.trim();
    if (!name) { toast('请先选择模板', 'err'); return; }
    if (!confirm('确认删除当前模板？')) return;
    try { await api('/api/templates/' + encodeURIComponent(name), { method: 'DELETE' }); toast('已删除', 'ok'); await loadTemplates(); newTemplate(); }
    catch (e) { toast('删除失败：' + e.message, 'err'); }
  }

  async function previewTemplate() {
    const t = ensureTpl();
    try {
      const d = await api('/api/templates/preview', {
        method: 'POST',
        body: { template: JSON.stringify({ page: t.page, items: t.items }), fields: '' }
      });
      const box = $('#tplPreviewBox');
      const img = $('#tplPreviewImg');
      box.hidden = false;
      img.innerHTML = d.base64 ? `<img src="data:image/png;base64,${d.base64}" alt="预览">` : '<span class="muted">无预览</span>';
    } catch (e) { toast('预览失败：' + e.message, 'err'); }
  }

  function exportTemplate() {
    const t = ensureTpl();
    const data = { name: $('#tplSaveName').value || 'template', page: t.page, items: t.items };
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = (data.name || 'template') + '.json';
    a.click();
    URL.revokeObjectURL(a.href);
  }

  function bindZoom() {
    $('#tplZoom').addEventListener('change', e => { tplZoom = e.target.value; renderCanvas(); });
    $$('[data-zoom]').forEach(b => b.onclick = () => {
      let z = tplZoom === '0' ? 1 : Number(tplZoom);
      z = b.dataset.zoom === '+' ? z * 1.25 : z / 1.25;
      z = Math.min(4, Math.max(0.25, z));
      tplZoom = String(Math.round(z * 100) / 100);
      $('#tplZoom').value = '0';
      renderCanvas();
    });
  }

  function bindTemplate() {
    $('#tplList').addEventListener('change', e => { if (e.target.value) loadTemplate(e.target.value); else newTemplate(); });
    $('#tplSave').onclick = saveTemplate;
    $('#tplDelete').onclick = deleteTemplate;
    $('#tplPreview').onclick = previewTemplate;
    $('#tplExport').onclick = exportTemplate;
  }

  /* ---------- 导航 ---------- */
  function bindNav() {
    $$('.nav-btn').forEach(b => b.onclick = () => switchView(b.dataset.view));
  }
  function switchView(view) {
    $$('.nav-btn').forEach(b => b.classList.toggle('active', b.dataset.view === view));
    $$('.view').forEach(v => v.hidden = v.id !== ('view-' + view));
    if (view === 'mq') loadMq();
    if (view === 'records') loadRecords(1);
    if (view === 'templates') { loadTemplates(); renderPageInputs(); renderCanvas(); renderProps(); }
    if (view === 'settings') loadRetention();
  }

  /* ---------- 初始化 ---------- */
  async function init() {
    bindNav(); bindTabs(); bindAdd(); bindPage(); bindTemplate(); bindZoom();
    $$('.mq-on').forEach(c => c.addEventListener('change', e => {
      const key = e.target.dataset.key; setCardOn(key, e.target.checked);
    }));
    $$('.mq-toggle').forEach(b => b.onclick = () => toggleMq(b.dataset.key));
    ['#channel', '#status'].forEach(s => $(s).addEventListener('change', () => loadRecords(1)));
    $('#saveRetention').onclick = saveRetention;
    $('#purgeNow').onclick = purgeNow;
    $('#openSettingsMq').onclick = () => switchView('mq');
    $('#modalX').onclick = closeModal;
    $('#modalMask').onclick = closeModal;

    newTemplate();
    switchView('mq');
    await loadPrinters();
    $('#printer').addEventListener('change', saveDefaultPrinter);
  }

  document.addEventListener('DOMContentLoaded', init);
})();
