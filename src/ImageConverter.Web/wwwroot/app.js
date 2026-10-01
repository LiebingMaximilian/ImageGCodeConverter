// ImageConverter web UI
// - builds the parameter form from /api/schema (so every C# setting is editable)
// - decodes the dropped image to grey-scale in the browser and uploads it
// - polls the job, then draws the line on a zoomable canvas and offers downloads
'use strict';

const $ = (id) => document.getElementById(id);
// Only values that differ from the server defaults are stored, so changing a default
// (TspDefaults.cs / appsettings.json) takes effect for every value you haven't changed yourself.
const STORAGE_KEY = 'imageconverter.settings.v2';
const OLD_STORAGE_KEY = 'imageconverter.settings.v1';            // stored *all* values
const RESET_ON_MIGRATION = ['AutoPixelsPerPoint', 'AutoMinPoints', 'AutoMaxPoints'];

// Shown in the cards at the top, so they are skipped in the "All parameters" list.
const ESSENTIAL = new Set([
  'tsp.PointCount', 'tsp.AutoPointCount',
  'gcode.WidthMm', 'gcode.HeightMm', 'gcode.KeepAspectRatio',
  'gcode.PenDownCommand', 'gcode.PenUpCommand', 'gcode.FeedRate',
]);

const state = {
  schema: [],                 // [{section, name, label, category, description, type, value}]
  fields: new Map(),          // "section.Name" -> schema entry
  settings: { tsp: {}, gcode: {} },
  image: null,                // { name, width, height, gray: Uint8Array, bitmap: ImageBitmap }
  jobId: null,
  polling: null,
  result: null,               // { points: Float32Array, stats, unitsPerMm }
  view: { zoom: 1, panX: 0, panY: 0 },
  path2d: null,
};

// ---------------------------------------------------------------------------
// Settings
// ---------------------------------------------------------------------------

async function loadSchema() {
  const res = await fetch('api/schema');
  state.schema = await res.json();
  for (const f of state.schema) state.fields.set(`${f.section}.${f.name}`, f);
  applyDefaults();

  try {
    let saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null');
    if (!saved) {
      // one-time migration from v1: keep everything except the automatic point-count values
      const old = JSON.parse(localStorage.getItem(OLD_STORAGE_KEY) || 'null');
      if (old) {
        for (const k of RESET_ON_MIGRATION) delete old.tsp?.[k];
        saved = old;
      }
    }
    if (saved) {
      for (const section of ['tsp', 'gcode']) {
        for (const [k, v] of Object.entries(saved[section] || {})) {
          if (state.fields.has(`${section}.${k}`)) state.settings[section][k] = v;
        }
      }
    }
  } catch { /* ignore broken storage */ }

  saveSettings();                                   // rewrite in the v2 (changes-only) format
  try { localStorage.removeItem(OLD_STORAGE_KEY); } catch { /* ignore */ }

  buildAllParams();
  bindInputs();
  refreshInputs();
}

function applyDefaults() {
  state.settings = { tsp: {}, gcode: {} };
  for (const f of state.schema) state.settings[f.section][f.name] = structuredClone(f.value);
}

function saveSettings() {
  const changed = { tsp: {}, gcode: {} };
  for (const f of state.schema) {
    const v = state.settings[f.section][f.name];
    if (JSON.stringify(v) !== JSON.stringify(f.value)) changed[f.section][f.name] = v;
  }
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(changed)); } catch { /* private mode etc. */ }
}

function buildAllParams() {
  const host = $('allParams');
  host.innerHTML = '';
  const groups = new Map();
  for (const f of state.schema) {
    if (ESSENTIAL.has(`${f.section}.${f.name}`)) continue;
    if (f.section === 'tsp' && f.category === 'Preview') continue;   // WinForms-only PNG settings
    const title = `${f.section === 'tsp' ? 'Line' : 'G-code'} · ${f.category}`;
    if (!groups.has(title)) groups.set(title, []);
    groups.get(title).push(f);
  }

  for (const [title, fields] of groups) {
    const details = document.createElement('details');
    const summary = document.createElement('summary');
    summary.textContent = title;
    details.appendChild(summary);

    for (const f of fields) {
      const id = `p_${f.section}_${f.name}`;
      let input;
      if (f.type === 'bool') {
        const label = document.createElement('label');
        label.className = 'check';
        input = document.createElement('input');
        input.type = 'checkbox';
        label.append(input, ' ' + f.label);
        details.appendChild(label);
        if (f.description) {
          const d = document.createElement('div');
          d.className = 'desc';
          d.textContent = f.description;
          details.appendChild(d);
        }
      } else {
        const row = document.createElement('div');
        row.className = 'row';
        const label = document.createElement('label');
        label.htmlFor = id;
        label.textContent = f.label;
        if (f.type === 'lines') {
          input = document.createElement('textarea');
          input.spellcheck = false;
        } else {
          input = document.createElement('input');
          input.type = f.type === 'string' ? 'text' : 'number';
          if (f.type === 'int') input.step = '1';
          if (f.type === 'number') input.step = 'any';
        }
        row.append(label, input);
        if (f.description) {
          const d = document.createElement('div');
          d.className = 'desc';
          d.textContent = f.description;
          row.appendChild(d);
        }
        details.appendChild(row);
      }
      input.id = id;
      input.dataset.section = f.section;
      input.dataset.key = f.name;
    }
    host.appendChild(details);
  }
}

function inputs() { return document.querySelectorAll('[data-section][data-key]'); }

function bindInputs() {
  for (const el of inputs()) {
    el.addEventListener(el.type === 'checkbox' ? 'change' : 'input', () => {
      const f = state.fields.get(`${el.dataset.section}.${el.dataset.key}`);
      if (!f) return;
      const v = readValue(el, f.type);
      if (v === undefined) return;               // incomplete number while typing
      state.settings[f.section][f.name] = v;
      saveSettings();
      updateHints();
    });
  }
}

function readValue(el, type) {
  switch (type) {
    case 'bool': return el.checked;
    case 'int': { const n = parseInt(el.value, 10); return Number.isFinite(n) ? n : undefined; }
    case 'number': { const n = parseFloat(el.value); return Number.isFinite(n) ? n : undefined; }
    case 'lines': return el.value.split('\n').map(s => s.trim()).filter(Boolean);
    default: return el.value;
  }
}

function refreshInputs() {
  for (const el of inputs()) {
    const f = state.fields.get(`${el.dataset.section}.${el.dataset.key}`);
    if (!f) continue;
    const v = state.settings[f.section][f.name];
    if (f.type === 'bool') el.checked = !!v;
    else if (f.type === 'lines') el.value = (v || []).join('\n');
    else el.value = v ?? '';
  }
  updateHints();
}

function resolvePointCount() {
  const t = state.settings.tsp;
  if (!t.AutoPointCount || !state.image) return t.AutoPointCount ? null : Math.max(2, t.PointCount);
  const pixels = state.image.width * state.image.height;
  const min = Math.max(2, t.AutoMinPoints);
  const max = Math.max(min, t.AutoMaxPoints);
  return Math.min(max, Math.max(min, Math.floor(pixels / Math.max(1, t.AutoPixelsPerPoint))));
}

function updateHints() {
  const t = state.settings.tsp, g = state.settings.gcode;
  const n = resolvePointCount();

  // In automatic mode the field shows the computed count (display only – the manual value is kept).
  const pc = $('PointCount');
  pc.disabled = !!t.AutoPointCount;
  if (t.AutoPointCount) {
    pc.value = n ?? '';
    pc.placeholder = 'auto – load an image';
  } else if (document.activeElement !== pc) {
    pc.value = t.PointCount;
  }
  let hint;
  if (t.AutoPointCount) {
    hint = n == null
      ? `Image pixels ÷ ${t.AutoPixelsPerPoint}, between ${fmt(t.AutoMinPoints)} and ${fmt(t.AutoMaxPoints)}.`
      : `→ ${fmt(n)} points for this ${state.image.width}×${state.image.height} image.`;
  } else {
    hint = 'More points = more detail, longer computing and drawing time.';
  }
  if (n && g.WidthMm > 0 && g.HeightMm > 0) {
    // rough line spacing on paper: drawing area / points
    let dw = g.WidthMm, dh = g.HeightMm;
    if (state.image && g.KeepAspectRatio) {
      const a = state.image.width / state.image.height;
      dw = Math.min(g.WidthMm, g.HeightMm * a); dh = dw / a;
    }
    const spacing = Math.sqrt((dw * dh) / n);
    hint += ` Avg. spacing ≈ ${spacing.toFixed(2)} mm – use a pen thinner than that.`;
  }
  $('pointHint').textContent = hint;

  let size = `${g.WidthMm} × ${g.HeightMm} mm area`;
  if (state.image && g.KeepAspectRatio) {
    const a = state.image.width / state.image.height;
    const w = Math.min(g.WidthMm, g.HeightMm * a), h = w / a;
    size = `Drawing ≈ ${w.toFixed(0)} × ${h.toFixed(0)} mm (image aspect ratio kept).`;
  } else if (!g.KeepAspectRatio) {
    size = `Drawing is stretched to exactly ${g.WidthMm} × ${g.HeightMm} mm.`;
  }
  $('sizeHint').textContent = size;
}

const fmt = (n) => Number(n).toLocaleString();

// ---------------------------------------------------------------------------
// Image input
// ---------------------------------------------------------------------------

function setupDrop() {
  const drop = $('drop'), fileInput = $('fileInput');
  drop.addEventListener('click', () => fileInput.click());
  drop.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') fileInput.click(); });
  fileInput.addEventListener('change', () => { if (fileInput.files[0]) loadImage(fileInput.files[0]); });

  for (const ev of ['dragenter', 'dragover']) {
    drop.addEventListener(ev, (e) => { e.preventDefault(); drop.classList.add('dragover'); });
  }
  for (const ev of ['dragleave', 'drop']) {
    drop.addEventListener(ev, (e) => { e.preventDefault(); drop.classList.remove('dragover'); });
  }
  drop.addEventListener('drop', (e) => {
    const file = [...e.dataTransfer.files].find(f => /image\/(png|jpeg)/.test(f.type) || /\.(png|jpe?g)$/i.test(f.name));
    if (file) loadImage(file); else setStatus('Please drop a JPG or PNG image.', 'error');
  });
  // dropping anywhere on the page should not open the image in the browser
  window.addEventListener('dragover', (e) => e.preventDefault());
  window.addEventListener('drop', (e) => e.preventDefault());
}

async function loadImage(file) {
  try {
    setStatus('Reading image…');
    const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
    const { width, height } = bitmap;

    const canvas = document.createElement('canvas');
    canvas.width = width; canvas.height = height;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    ctx.fillStyle = '#fff';                       // transparent areas -> white
    ctx.fillRect(0, 0, width, height);
    ctx.drawImage(bitmap, 0, 0);
    const rgba = ctx.getImageData(0, 0, width, height).data;

    const gray = new Uint8Array(width * height);
    for (let i = 0, j = 0; j < gray.length; i += 4, j++) {
      gray[j] = Math.round(0.299 * rgba[i] + 0.587 * rgba[i + 1] + 0.114 * rgba[i + 2]);   // BT.601, same as the C# code
    }

    state.image?.bitmap?.close?.();
    state.image = { name: file.name, width, height, gray, bitmap };

    const thumb = $('thumb');
    if (thumb.src) URL.revokeObjectURL(thumb.src);
    thumb.src = URL.createObjectURL(file);
    thumb.hidden = false;
    $('drop').classList.add('has-image');
    $('dropText').innerHTML = '';
    const strong = document.createElement('strong');
    strong.textContent = file.name;
    const span = document.createElement('span');
    span.textContent = `${width} × ${height} px – drop another image or click to replace`;
    $('dropText').append(strong, span);

    $('generateBtn').disabled = false;
    setStatus('Image loaded. Adjust the parameters and click “Generate line”.');
    updateHints();
    draw();
  } catch (err) {
    console.error(err);
    setStatus('Could not read this image: ' + err.message, 'error');
  }
}

// ---------------------------------------------------------------------------
// Job
// ---------------------------------------------------------------------------

async function generate() {
  if (!state.image) return;
  const g = state.settings.gcode;
  if (!g.PenDownCommand || !g.PenDownCommand.trim()) { setStatus('The pen down command must not be empty.', 'error'); return; }
  if (!(g.WidthMm > 0 && g.HeightMm > 0)) { setStatus('Width and height must be greater than 0 mm.', 'error'); return; }

  stopPolling();
  setBusy(true);
  setStatus('Uploading…');

  const form = new FormData();
  form.append('width', state.image.width);
  form.append('height', state.image.height);
  form.append('name', state.image.name);
  form.append('settings', JSON.stringify(state.settings));
  form.append('gray', new Blob([state.image.gray], { type: 'application/octet-stream' }), 'gray.bin');

  try {
    const res = await fetch('api/jobs', { method: 'POST', body: form });
    const body = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(body.error || `HTTP ${res.status}`);
    state.jobId = body.id;
    state.polling = setInterval(poll, 400);
  } catch (err) {
    setBusy(false);
    setStatus('Upload failed: ' + err.message, 'error');
  }
}

async function poll() {
  const id = state.jobId;
  if (!id) return;
  let job;
  try {
    const res = await fetch(`api/jobs/${id}`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    job = await res.json();
  } catch (err) {
    stopPolling(); setBusy(false);
    setStatus('Lost the job: ' + err.message, 'error');
    return;
  }
  if (id !== state.jobId) return;

  if (job.status === 'queued' || job.status === 'running') {
    setStatus(`${job.message} (${job.elapsedSeconds.toFixed(1)} s)`);
    return;
  }

  stopPolling();
  setBusy(false);
  if (job.status === 'done') {
    await showResult(job);
    setStatus(`Done in ${job.elapsedSeconds.toFixed(1)} s.`, 'ok');
  } else if (job.status === 'cancelled') {
    setStatus('Cancelled.');
  } else {
    setStatus('Failed: ' + (job.error || 'unknown error'), 'error');
  }
}

async function cancel() {
  if (state.jobId) await fetch(`api/jobs/${state.jobId}`, { method: 'DELETE' }).catch(() => {});
}

function stopPolling() {
  if (state.polling) clearInterval(state.polling);
  state.polling = null;
}

async function showResult(job) {
  setStatus('Loading preview…');
  const res = await fetch(`api/jobs/${job.id}/path`);
  const points = new Float32Array(await res.arrayBuffer());
  state.result = { points, stats: job.stats, id: job.id, unitsPerMm: unitsPerMm(points, job.stats) };
  buildPath2d();
  fitView();

  const s = job.stats;
  const rows = [
    ['Points', fmt(s.points)],
    ['Drawing size', `${s.drawingWidthMm} × ${s.drawingHeightMm} mm`],
    ['Line length', `${s.lineLengthM} m`],
    ['Est. drawing time', minutes(s.estimatedMinutes)],
    ['G-code moves', fmt(s.moves)],
    ['G-code size', s.gcodeKb > 1024 ? `${(s.gcodeKb / 1024).toFixed(1)} MB` : `${s.gcodeKb} KB`],
    ['Working image', `${s.workingWidth} × ${s.workingHeight} px`],
  ];
  const dl = $('stats');
  dl.innerHTML = '';
  for (const [k, v] of rows) {
    const div = document.createElement('div');
    const dt = document.createElement('dt'); dt.textContent = k;
    const dd = document.createElement('dd'); dd.textContent = v;
    div.append(dt, dd); dl.appendChild(div);
  }
  $('gcodeHead').textContent = s.gcodeHead;
  $('dlGcode').href = `api/jobs/${job.id}/gcode`;
  updatePenWidth();
  $('results').hidden = false;
}

function minutes(m) {
  if (m < 60) return `${Math.round(m)} min`;
  return `${Math.floor(m / 60)} h ${Math.round(m % 60)} min`;
}

function setBusy(busy) {
  $('generateBtn').disabled = busy || !state.image;
  $('cancelBtn').hidden = !busy;
}

function setStatus(text, kind = '') {
  const el = $('status');
  el.textContent = text;
  el.className = kind;
}

// ---------------------------------------------------------------------------
// Preview canvas (zoom / pan)
// ---------------------------------------------------------------------------

function buildPath2d() {
  const p = state.result.points;
  const path = new Path2D();
  if (p.length >= 2) {
    path.moveTo(p[0], p[1]);
    for (let i = 2; i < p.length; i += 2) path.lineTo(p[i], p[i + 1]);
  }
  state.path2d = path;
}

function contentSize() {
  if (state.result) return { w: state.result.stats.workingWidth, h: state.result.stats.workingHeight };
  if (state.image) return { w: state.image.width, h: state.image.height };
  return null;
}

function fitView() {
  const size = contentSize();
  if (!size) return;
  const c = $('canvas');
  const pad = 16;
  const zoom = Math.min((c.clientWidth - 2 * pad) / size.w, (c.clientHeight - 2 * pad) / size.h);
  state.view = {
    zoom,
    panX: (c.clientWidth - size.w * zoom) / 2,
    panY: (c.clientHeight - size.h * zoom) / 2,
  };
  draw();
}

let drawQueued = false;
function draw() {
  if (drawQueued) return;
  drawQueued = true;
  requestAnimationFrame(() => { drawQueued = false; drawNow(); });
}

function drawNow() {
  const c = $('canvas');
  const dpr = window.devicePixelRatio || 1;
  const w = Math.round(c.clientWidth * dpr), h = Math.round(c.clientHeight * dpr);
  if (c.width !== w || c.height !== h) { c.width = w; c.height = h; }
  const ctx = c.getContext('2d');
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.fillStyle = '#fff';
  ctx.fillRect(0, 0, w, h);

  const size = contentSize();
  $('emptyPreview').hidden = !!state.result;
  if (!size) return;

  const { zoom, panX, panY } = state.view;
  ctx.setTransform(zoom * dpr, 0, 0, zoom * dpr, panX * dpr, panY * dpr);

  // original underneath (scaled to the working-image coordinate space)
  if (state.image && ($('showOriginal').checked || !state.result)) {
    ctx.globalAlpha = state.result ? 0.35 : 1;
    ctx.drawImage(state.image.bitmap, 0, 0, size.w, size.h);
    ctx.globalAlpha = 1;
  }

  if (state.result && state.path2d) {
    // real pen width on paper, but never thinner than half a screen pixel so the line stays visible
    ctx.lineWidth = Math.max(penMm() * state.result.unitsPerMm, 0.5 / zoom);
    ctx.lineJoin = 'round';
    ctx.strokeStyle = '#000';
    ctx.stroke(state.path2d);

    const p = state.result.points, r = 5 / zoom;
    dot(ctx, p[0], p[1], r, '#22c55e');
    dot(ctx, p[p.length - 2], p[p.length - 1], r, '#ef4444');
  }
}

function dot(ctx, x, y, r, color) {
  ctx.beginPath();
  ctx.arc(x, y, r, 0, Math.PI * 2);
  ctx.fillStyle = color;
  ctx.fill();
}

function setupCanvas() {
  const c = $('canvas');
  c.addEventListener('wheel', (e) => {
    e.preventDefault();
    const rect = c.getBoundingClientRect();
    const mx = e.clientX - rect.left, my = e.clientY - rect.top;
    const v = state.view;
    const factor = Math.exp(-e.deltaY * 0.0015);
    const zoom = Math.min(200, Math.max(0.01, v.zoom * factor));
    v.panX = mx - (mx - v.panX) * (zoom / v.zoom);
    v.panY = my - (my - v.panY) * (zoom / v.zoom);
    v.zoom = zoom;
    draw();
  }, { passive: false });

  let drag = null;
  c.addEventListener('pointerdown', (e) => {
    drag = { x: e.clientX, y: e.clientY, panX: state.view.panX, panY: state.view.panY };
    c.setPointerCapture(e.pointerId);
    c.classList.add('dragging');
  });
  c.addEventListener('pointermove', (e) => {
    if (!drag) return;
    state.view.panX = drag.panX + (e.clientX - drag.x);
    state.view.panY = drag.panY + (e.clientY - drag.y);
    draw();
  });
  const end = () => { drag = null; c.classList.remove('dragging'); };
  c.addEventListener('pointerup', end);
  c.addEventListener('pointercancel', end);
  c.addEventListener('dblclick', fitView);

  new ResizeObserver(() => draw()).observe(c);
  $('fitBtn').addEventListener('click', fitView);
  $('showOriginal').addEventListener('change', draw);
  $('lineWidth').addEventListener('input', () => { updatePenWidth(); draw(); });
  try {
    const saved = parseFloat(localStorage.getItem(PEN_KEY));
    if (saved > 0) $('lineWidth').value = saved;
  } catch { /* ignore */ }
  updatePenWidth();
}

// ---------------------------------------------------------------------------
// Pen width (mm on paper)
// ---------------------------------------------------------------------------

const PEN_KEY = 'imageconverter.penMm';
const penMm = () => parseFloat($('lineWidth').value);

/** Path units (working-image px) per mm on paper – same scaling as the G-code writer. */
function unitsPerMm(points, stats) {
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (let i = 0; i < points.length; i += 2) {
    const x = points[i], y = points[i + 1];
    if (x < minX) minX = x; if (x > maxX) maxX = x;
    if (y < minY) minY = y; if (y > maxY) maxY = y;
  }
  const sx = Math.max(1e-3, maxX - minX) / stats.drawingWidthMm;
  const sy = Math.max(1e-3, maxY - minY) / stats.drawingHeightMm;
  return Math.sqrt(sx * sy);    // equal when the aspect ratio is kept
}

function updatePenWidth() {
  const mm = penMm();
  const label = $('lineWidthMm');
  if (label) label.textContent = `${mm.toFixed(2)} mm`;
  try { localStorage.setItem(PEN_KEY, String(mm)); } catch { /* ignore */ }
  if (state.result) $('dlSvg').href = `api/jobs/${state.result.id}/svg?strokeMm=${mm}`;
}

// PNG download: render the line on a white canvas of the chosen size.
function downloadPng() {
  if (!state.result) return;
  const { workingWidth: ww, workingHeight: wh } = state.result.stats;
  const longSide = parseInt($('pngSize').value, 10);
  const scale = longSide / Math.max(ww, wh);
  const c = document.createElement('canvas');
  c.width = Math.max(1, Math.round(ww * scale));
  c.height = Math.max(1, Math.round(wh * scale));
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#fff';
  ctx.fillRect(0, 0, c.width, c.height);
  ctx.setTransform(scale, 0, 0, scale, 0, 0);
  ctx.lineWidth = Math.max(penMm() * state.result.unitsPerMm, 0.5 / scale);   // same pen width as the preview
  ctx.lineJoin = 'round';
  ctx.strokeStyle = '#000';
  ctx.stroke(state.path2d);

  c.toBlob((blob) => {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = (state.image?.name?.replace(/\.[^.]+$/, '') || 'image') + '_tsp.png';
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 10000);
  }, 'image/png');
}

// ---------------------------------------------------------------------------

async function init() {
  // Each step is isolated: if one part fails (e.g. an outdated cached index.html is missing an
  // element), the rest of the page keeps working and the problem is shown instead of a dead page.
  const problems = [];
  const step = (name, fn) => {
    try { fn(); } catch (err) { console.error(name, err); problems.push(`${name}: ${err.message}`); }
  };

  step('drop zone', setupDrop);
  step('preview', setupCanvas);
  step('buttons', () => {
    $('generateBtn').addEventListener('click', generate);
    $('cancelBtn').addEventListener('click', cancel);
    $('dlPng').addEventListener('click', downloadPng);
    $('resetBtn').addEventListener('click', () => {
      if (!confirm('Reset all parameters to their defaults?')) return;
      applyDefaults();
      saveSettings();
      refreshInputs();
    });
  });

  try {
    await loadSchema();
  } catch (err) {
    problems.push('Could not load settings from the server: ' + err.message);
  }
  step('draw', draw);

  if (problems.length) {
    setStatus('Page error – reload with Ctrl+F5. ' + problems.join(' · '), 'error');
  }
}

init();
