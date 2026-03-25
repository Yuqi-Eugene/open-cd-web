attachNav("home");

const prev = {
  cpu: null,
  mem: null,
  rx: null,
  tx: null
};

function fmtRate(bytesPerSec) {
  const b = Number(bytesPerSec) || 0;
  if (b < 1024) return `${b.toFixed(0)} B/s`;
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(1)} KB/s`;
  return `${(b / (1024 * 1024)).toFixed(2)} MB/s`;
}

function trendArrow(cur, old) {
  if (old === null || old === undefined) return "→";
  const delta = Number(cur) - Number(old);
  if (Math.abs(delta) < 0.3) return "→";
  return delta > 0 ? "↑" : "↓";
}

function trendClass(cur, old) {
  if (old === null || old === undefined) return "flat";
  const delta = Number(cur) - Number(old);
  if (Math.abs(delta) < 0.3) return "flat";
  return delta > 0 ? "up" : "down";
}

function setTrend(arrowId, valueId, cur, old) {
  const el = $(arrowId);
  const valueEl = $(valueId);
  if (!el || !valueEl) return;
  const cls = trendClass(cur, old);
  el.textContent = trendArrow(cur, old);
  el.className = cls;
  valueEl.classList.remove("up", "down", "flat");
  valueEl.classList.add(cls);
}

async function refreshMetrics() {
  try {
    const m = await api("/api/system/metrics");

    const cpu = Number(m.CpuPercent) || 0;
    const mem = Number(m.MemoryPercent) || 0;
    const rx = Number(m.NetworkRxBytesPerSec) || 0;
    const tx = Number(m.NetworkTxBytesPerSec) || 0;

    $("cpuNow").textContent = `${cpu.toFixed(1)}%`;
    $("memNow").textContent = `${mem.toFixed(1)}%`;
    $("rxNow").textContent = fmtRate(rx);
    $("txNow").textContent = fmtRate(tx);

    setTrend("cpuTrend", "cpuNow", cpu, prev.cpu);
    setTrend("memTrend", "memNow", mem, prev.mem);
    setTrend("rxTrend", "rxNow", rx, prev.rx);
    setTrend("txTrend", "txNow", tx, prev.tx);

    prev.cpu = cpu;
    prev.mem = mem;
    prev.rx = rx;
    prev.tx = tx;
  } catch (e) {
    void e;
  }
}

refreshMetrics().catch(() => {});
setInterval(() => refreshMetrics().catch(() => {}), 3000);
