const COLORS = {
  fridge: '#4f8cff',
  shelf: '#ffb74d',
  overShelf: '#9ccc65',
  iceMaker: '#ba68c8',
};

const LEGEND_NAMES = {
  fridge: '冰箱 (fridge)',
  shelf: '货架 (shelf)',
  overShelf: '离地架 (overShelf)',
  iceMaker: '制冰机 (iceMaker)',
};

const canvas = document.getElementById('canvas');
const ctx = canvas.getContext('2d');
const inputEl = document.getElementById('input');
const resultEl = document.getElementById('result');
const legendEl = document.getElementById('legend');
const resultPanel = document.getElementById('result-panel');
const canvasPanel = document.getElementById('canvas-panel');

let currentInput = null;
let currentResult = null;

function buildLegend() {
  legendEl.innerHTML = '';
  for (const [type, color] of Object.entries(COLORS)) {
    const span = document.createElement('span');
    const sw = document.createElement('span');
    sw.className = 'sw';
    sw.style.background = color;
    span.appendChild(sw);
    span.appendChild(document.createTextNode(LEGEND_NAMES[type] || type));
    legendEl.appendChild(span);
  }
}

async function loadExample(name) {
  const res = await fetch(`examples/${name}.json`);
  const data = await res.json();
  inputEl.value = JSON.stringify(data, null, 2);
  currentInput = data;
  currentResult = null;
  hideResultAndCanvas();
}

async function run() {
  let data;
  try {
    data = JSON.parse(inputEl.value);
  } catch (e) {
    showResultAndCanvas();
    clearCanvas();
    resultEl.innerHTML = '<span class="bad">JSON 解析失败：</span>' + e.message;
    return;
  }
  currentInput = data;

  // 每次求解：先清空结果框与图形框内容，再显示出来
  showResultAndCanvas();
  clearCanvas();
  resultEl.innerHTML = '<span class="muted">求解中…</span>';

  try {
    const res = await fetch('api/place', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    currentResult = await res.json();
  } catch (e) {
    resultEl.innerHTML = '<span class="bad">请求失败：</span>' + e.message;
    return;
  }
  render();
}

function render() {
  renderResult();
  renderCanvas();
}

function renderResult() {
  const r = currentResult;
  if (!r) return;
  const meta = `<div class="meta">求解耗时 ${r.elapsedMs ?? '?'} ms · 探索节点 ${r.nodesExplored ?? '?'}</div>`;
  if (!r.feasible) {
    resultEl.innerHTML = `<div class="bad">不可行</div>${meta}<div>原因：${r.reason || '未知'}</div>`;
    return;
  }
  let html = `<div class="ok">可行 ✓</div>${meta}<table><thead><tr>
    <th>物体</th><th>类型</th><th>中心 X (mm)</th><th>中心 Y (mm)</th><th>旋转角</th><th>尺寸 (L×W)</th></tr></thead><tbody>`;
  for (const p of r.placements) {
    html += `<tr>
      <td>${p.id}</td>
      <td>${LEGEND_NAMES[p.type] || p.type}</td>
      <td>${p.centerX.toFixed(2)}</td>
      <td>${p.centerY.toFixed(2)}</td>
      <td>${p.rotation}°</td>
      <td>${p.length} × ${p.width}</td>
    </tr>`;
  }
  html += '</tbody></table>';
  resultEl.innerHTML = html;
}

function renderCanvas() {
  const data = currentInput;
  if (!data || !data.boundary) return;

  const boundary = data.boundary.map(p => ({ x: p[0], y: p[1] }));
  const items = currentResult && currentResult.feasible ? currentResult.placements : [];

  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  boundary.forEach(p => { minX = Math.min(minX, p.x); minY = Math.min(minY, p.y); maxX = Math.max(maxX, p.x); maxY = Math.max(maxY, p.y); });

  const dpr = window.devicePixelRatio || 1;
  const cssW = canvas.clientWidth || 1000;
  const cssH = 620;
  canvas.width = cssW * dpr;
  canvas.height = cssH * dpr;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

  ctx.clearRect(0, 0, cssW, cssH);

  const pad = 40;
  const sx = (cssW - pad * 2) / (maxX - minX);
  const sy = (cssH - pad * 2) / (maxY - minY);
  const s = Math.min(sx, sy);
  const ox = (cssW - (maxX - minX) * s) / 2;
  const oy = (cssH - (maxY - minY) * s) / 2;

  const tx = (x) => ox + (x - minX) * s;
  const ty = (y) => cssH - (oy + (y - minY) * s); // Y 翻转

  // 轮廓
  ctx.beginPath();
  boundary.forEach((p, i) => i === 0 ? ctx.moveTo(tx(p.x), ty(p.y)) : ctx.lineTo(tx(p.x), ty(p.y)));
  ctx.closePath();
  ctx.fillStyle = '#1e293b';
  ctx.fill();
  ctx.strokeStyle = '#64748b';
  ctx.lineWidth = 2;
  ctx.stroke();

  // 门摆动区（内开门）
  if (currentResult && currentResult.swingMinX != null) {
    const r = currentResult;
    ctx.fillStyle = 'rgba(248,113,113,0.35)';
    ctx.strokeStyle = '#f87171';
    ctx.setLineDash([5, 4]);
    ctx.fillRect(tx(r.swingMinX), ty(r.swingMaxY), (r.swingMaxX - r.swingMinX) * s, (r.swingMaxY - r.swingMinY) * s);
    ctx.strokeRect(tx(r.swingMinX), ty(r.swingMaxY), (r.swingMaxX - r.swingMinX) * s, (r.swingMaxY - r.swingMinY) * s);
    ctx.setLineDash([]);
  }

  // 门线段
  if (data.door && data.door.length === 2) {
    const d1 = data.door[0], d2 = data.door[1];
    ctx.beginPath();
    ctx.moveTo(tx(d1[0]), ty(d1[1]));
    ctx.lineTo(tx(d2[0]), ty(d2[1]));
    ctx.strokeStyle = '#f87171';
    ctx.lineWidth = 5;
    ctx.stroke();
  }

  // 物体
  for (const p of items) {
    const color = COLORS[p.type] || '#e2e8f0';
    const x = tx(p.minX), y = ty(p.maxY);
    const w = (p.maxX - p.minX) * s, h = (p.maxY - p.minY) * s;
    ctx.fillStyle = color + '33';
    ctx.fillRect(x, y, w, h);
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.strokeRect(x, y, w, h);

    // 冰箱净空区
    if (p.clearanceMinX != null) {
      ctx.save();
      ctx.fillStyle = 'rgba(79,140,255,0.12)';
      ctx.strokeStyle = '#4f8cff';
      ctx.setLineDash([3, 3]);
      const cx = tx(p.clearanceMinX), cy = ty(p.clearanceMaxY);
      const cw = (p.clearanceMaxX - p.clearanceMinX) * s, ch = (p.clearanceMaxY - p.clearanceMinY) * s;
      ctx.fillRect(cx, cy, cw, ch);
      ctx.strokeRect(cx, cy, cw, ch);
      ctx.setLineDash([]);
      ctx.restore();
    }

    // 标签
    ctx.fillStyle = '#fff';
    ctx.font = '12px "Microsoft YaHei", sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText(p.id, x + w / 2, y + h / 2 + 4);
  }
}

function clearCanvas() {
  ctx.clearRect(0, 0, canvas.width, canvas.height);
}

function showResultAndCanvas() {
  resultPanel.classList.remove('hidden');
  canvasPanel.classList.remove('hidden');
}

function hideResultAndCanvas() {
  resultPanel.classList.add('hidden');
  canvasPanel.classList.add('hidden');
  resultEl.innerHTML = '';
  clearCanvas();
}

document.querySelectorAll('.ex').forEach(btn => {
  btn.addEventListener('click', () => loadExample(btn.dataset.ex));
});
document.getElementById('run').addEventListener('click', run);

buildLegend();
loadExample('example1');
