attachNav("train");

const state = {
  scalars: null,
  selectedMetrics: new Set(["loss", "decode.acc_seg"])
};

async function loadConfigs() {
  const keyword = $("configSearch").value.trim();
  let list = await api(`/api/opencd/configs?keyword=${encodeURIComponent(keyword)}`);
  if (keyword && (!list || list.length === 0)) {
    const all = await api("/api/opencd/configs");
    const key = keyword.toLowerCase();
    list = (all || []).filter((x) => x.toLowerCase().includes(key));
  }
  const sel = $("configSelect");
  sel.innerHTML = "";
  if (!list || list.length === 0) {
    const opt = document.createElement("option");
    opt.value = "";
    opt.textContent = "未找到匹配配置";
    sel.appendChild(opt);
    setStatus("status", `未检索到匹配配置: ${keyword || "(空)"}`, true);
    return;
  }
  list.forEach((x) => {
    const opt = document.createElement("option");
    opt.value = x;
    opt.textContent = x;
    sel.appendChild(opt);
  });
  sel.selectedIndex = 0;
  setStatus("status", `已加载配置 ${list.length} 项`);
}

async function loadCheckpoints() {
  const list = await api("/api/opencd/checkpoints");
  const sel = $("ckptSelect");
  sel.innerHTML = "";
  if (!list.length) {
    const opt = document.createElement("option");
    opt.value = "";
    opt.textContent = "未找到 .pth（新训练可忽略，验证/测试前再刷新）";
    sel.appendChild(opt);
    return;
  }
  list.forEach((x) => {
    const opt = document.createElement("option");
    opt.value = x;
    opt.textContent = x;
    sel.appendChild(opt);
  });
}

async function submit(url, body, msg) {
  try {
    const res = await postJson(url, body);
    $("jobId").value = res.Id;
    setStatus("status", `${msg}，JobId: ${res.Id}`);
    await renderJobs("jobChips");
    await loadLog("jobId", "jobLog");
  } catch (e) {
    setStatus("status", String(e), true);
  }
}

async function validateDatasetRootForTask(task) {
  const root = $("datasetRoot").value.trim();
  if (!root) {
    return true;
  }

  const index = await api(`/api/data/index?datasetRoot=${encodeURIComponent(root)}`);
  const splitMap = Object.fromEntries((index.Splits || []).map((s) => [String(s.Name).toLowerCase(), Number(s.Count) || 0]));
  const trainCount = splitMap.train || 0;
  const valCount = splitMap.val || 0;
  const testCount = splitMap.test || 0;

  if (task === "train" && trainCount <= 0) {
    setStatus("status", `数据集目录 ${root} 的 train 样本数为 0，无法训练。`, true);
    return false;
  }
  if (task === "validate" && valCount <= 0) {
    setStatus("status", `数据集目录 ${root} 的 val 样本数为 0，无法验证。`, true);
    return false;
  }
  if (task === "test" && testCount <= 0) {
    setStatus("status", `数据集目录 ${root} 的 test 样本数为 0，无法测试。`, true);
    return false;
  }

  return true;
}

async function chooseDatasetRoot() {
  try {
    setStatus("status", "打开目录索引...");
    const current = $("datasetRoot").value?.trim() || "";
    const path = await pickPath({
      mode: "directory",
      startPath: current,
      title: "选择数据集目录"
    });
    $("datasetRoot").value = path;
    setStatus("status", `已选择数据集目录: ${path}`);
  } catch (e) {
    setStatus("status", `目录选择失败: ${String(e)}`, true);
  }
}

function drawChart() {
  const svg = $("chart");
  svg.innerHTML = "";
  const data = state.scalars;
  if (!data?.Points?.length) return;

  const metrics = [...state.selectedMetrics].filter((m) => data.AvailableMetrics.includes(m));
  if (!metrics.length) return;

  const w = Math.max(760, Math.round(svg.clientWidth || 960));
  const h = Math.max(220, Math.round(svg.clientHeight || 280));
  svg.setAttribute("viewBox", `0 0 ${w} ${h}`);
  svg.setAttribute("preserveAspectRatio", "xMidYMid meet");

  const leftPad = Math.max(72, Math.round(w * 0.08));
  const rightPad = Math.max(28, Math.round(w * 0.035));
  const topPad = Math.max(12, Math.round(h * 0.06));
  const bottomPad = Math.max(24, Math.round(h * 0.10));
  const yTicks = 8;
  const xTicks = 4;
  const xs = data.Points.map((p) => p.Step);
  const minX = Math.min(...xs), maxX = Math.max(...xs);
  const ys = data.Points.flatMap((p) => metrics.map((m) => p.Values[m]).filter((x) => Number.isFinite(x)));
  const minY = Math.min(...ys), maxY = Math.max(...ys);
  const rangeX = Math.max(1, maxX - minX);
  const rangeY = Math.max(1e-9, maxY - minY);
  const colors = ["#2563eb", "#059669", "#dc2626", "#7c3aed", "#f59e0b"];

  for (let i = 0; i <= xTicks; i++) {
    const x = leftPad + (i / xTicks) * (w - leftPad - rightPad);
    const xv = minX + (i / xTicks) * rangeX;
    const tx = document.createElementNS("http://www.w3.org/2000/svg", "text");
    tx.setAttribute("x", String(x.toFixed(2)));
    tx.setAttribute("y", String(h - 20));
    tx.setAttribute("text-anchor", "middle");
    tx.setAttribute("fill", "#0f172a");
    tx.setAttribute("font-size", "13");
    tx.setAttribute("font-weight", "700");
    tx.textContent = `${Math.round(xv)}`;
    svg.appendChild(tx);
  }

  for (let i = 0; i <= yTicks; i++) {
    const y = h - bottomPad - (i / yTicks) * (h - topPad - bottomPad);
    const yv = minY + (i / yTicks) * rangeY;
    const ty = document.createElementNS("http://www.w3.org/2000/svg", "text");
    ty.setAttribute("x", String(leftPad - 12));
    ty.setAttribute("y", String(y + 5));
    ty.setAttribute("text-anchor", "end");
    ty.setAttribute("fill", "#0f172a");
    ty.setAttribute("font-size", "13");
    ty.setAttribute("font-weight", "700");
    ty.textContent = yv.toFixed(3);
    svg.appendChild(ty);
  }

  const axis = document.createElementNS("http://www.w3.org/2000/svg", "path");
  axis.setAttribute("d", `M ${leftPad} ${h - bottomPad} L ${w - rightPad} ${h - bottomPad} M ${leftPad} ${topPad} L ${leftPad} ${h - bottomPad}`);
  axis.setAttribute("stroke", "#334155");
  axis.setAttribute("stroke-width", "1.6");
  axis.setAttribute("fill", "none");
  svg.appendChild(axis);

  const xLabel = document.createElementNS("http://www.w3.org/2000/svg", "text");
  xLabel.setAttribute("x", String((leftPad + w - rightPad) / 2));
  xLabel.setAttribute("y", String(h - 3));
  xLabel.setAttribute("text-anchor", "middle");
  xLabel.setAttribute("fill", "#0f172a");
  xLabel.setAttribute("font-size", "14");
  xLabel.setAttribute("font-weight", "700");
  xLabel.textContent = "Step (iteration)";
  svg.appendChild(xLabel);

  const yLabel = document.createElementNS("http://www.w3.org/2000/svg", "text");
  yLabel.setAttribute("x", "24");
  yLabel.setAttribute("y", String(h / 2));
  yLabel.setAttribute("text-anchor", "middle");
  yLabel.setAttribute("fill", "#0f172a");
  yLabel.setAttribute("font-size", "14");
  yLabel.setAttribute("font-weight", "700");
  yLabel.setAttribute("transform", `rotate(-90 24 ${h / 2})`);
  yLabel.textContent = "Metric Value";
  svg.appendChild(yLabel);

  metrics.forEach((metric, i) => {
    const points = [];
    data.Points.forEach((p) => {
      const yv = p.Values[metric];
      if (!Number.isFinite(yv)) return;
      const x = leftPad + ((p.Step - minX) / rangeX) * (w - leftPad - rightPad);
      const y = h - bottomPad - ((yv - minY) / rangeY) * (h - topPad - bottomPad);
      points.push(`${x.toFixed(2)},${y.toFixed(2)}`);
    });
    const line = document.createElementNS("http://www.w3.org/2000/svg", "polyline");
    line.setAttribute("points", points.join(" "));
    line.setAttribute("fill", "none");
    line.setAttribute("stroke", colors[i % colors.length]);
    line.setAttribute("stroke-width", "2");
    svg.appendChild(line);

    const text = document.createElementNS("http://www.w3.org/2000/svg", "text");
    text.setAttribute("x", String(leftPad + i * 165));
    text.setAttribute("y", String(Math.max(20, topPad - 10)));
    text.setAttribute("fill", colors[i % colors.length]);
    text.setAttribute("font-size", "14");
    text.setAttribute("font-weight", "700");
    text.textContent = metric;
    svg.appendChild(text);
  });
}

async function loadScalars() {
  const wd = $("metricsWorkDir").value.trim();
  state.scalars = await api(`/api/opencd/scalars?workDir=${encodeURIComponent(wd)}`);

  const wrap = $("metricList");
  wrap.innerHTML = "";
  state.scalars.AvailableMetrics.forEach((m) => {
    const chip = document.createElement("button");
    chip.className = "chip";
    chip.textContent = m;
    if (state.selectedMetrics.has(m)) chip.style.borderColor = "#2563eb";
    chip.onclick = () => {
      if (state.selectedMetrics.has(m)) state.selectedMetrics.delete(m);
      else state.selectedMetrics.add(m);
      loadScalars().catch(console.error);
    };
    wrap.appendChild(chip);
  });

  drawChart();
}

async function importParams(file) {
  const text = await file.text();
  const cfg = JSON.parse(text);
  if (cfg.ConfigPath) $("configSelect").value = cfg.ConfigPath;
  if (cfg.CheckpointPath) $("ckptSelect").value = cfg.CheckpointPath;
  if (cfg.WorkDir) $("workDir").value = cfg.WorkDir;
  if (cfg.ResumeFrom) $("resumeFrom").value = cfg.ResumeFrom;
  if (cfg.ExtraArgs) $("extraArgs").value = cfg.ExtraArgs;
  if (cfg.DatasetRoot) $("datasetRoot").value = cfg.DatasetRoot;
  if (cfg.TestOut) $("testOut").value = cfg.TestOut;
  if (typeof cfg.Amp === "boolean") $("amp").checked = cfg.Amp;
}

$("loadConfigs").onclick = () => loadConfigs().catch((e) => setStatus("status", String(e), true));
$("loadCheckpoints").onclick = () => loadCheckpoints().catch((e) => setStatus("status", String(e), true));
$("browseDatasetRoot").onclick = () => chooseDatasetRoot().catch((e) => setStatus("status", String(e), true));
$("refreshJobs").onclick = () => renderJobs("jobChips").catch((e) => setStatus("status", String(e), true));
$("viewLog").onclick = () => loadLog("jobId", "jobLog").catch((e) => setStatus("status", String(e), true));
$("cancelJob").onclick = async () => {
  const id = $("jobId").value.trim();
  if (!id) {
    setStatus("status", "请先选择或输入 Job ID", true);
    return;
  }
  try {
    await api(`/api/jobs/${encodeURIComponent(id)}/cancel`, { method: "POST" });
    setStatus("status", `已请求停止任务: ${id}`);
    await renderJobs("jobChips");
    await loadLog("jobId", "jobLog");
  } catch (e) {
    setStatus("status", `停止任务失败: ${String(e)}`, true);
  }
};

$("runTrain").onclick = async () => {
  if (!(await validateDatasetRootForTask("train"))) return;
  submit("/api/opencd/train", {
    ConfigPath: $("configSelect").value,
    WorkDir: $("workDir").value,
    DatasetRoot: $("datasetRoot").value || null,
    Amp: $("amp").checked,
    ResumeFrom: $("resumeFrom").value || null,
    ExtraArgs: $("extraArgs").value || null
  }, "训练任务已提交");
};

$("runVal").onclick = () => {
  const ckpt = $("ckptSelect").value;
  if (!ckpt) {
    setStatus("status", "验证任务需要选择 checkpoint（.pth）", true);
    return;
  }
  validateDatasetRootForTask("validate").then((ok) => {
    if (!ok) return;
    submit("/api/opencd/validate", {
      ConfigPath: $("configSelect").value,
      CheckpointPath: ckpt,
      DatasetRoot: $("datasetRoot").value || null,
      WorkDir: $("workDir").value,
      ExtraArgs: $("extraArgs").value || null
    }, "验证任务已提交");
  }).catch((e) => setStatus("status", String(e), true));
};

$("runTest").onclick = () => {
  const ckpt = $("ckptSelect").value;
  if (!ckpt) {
    setStatus("status", "测试任务需要选择 checkpoint（.pth）", true);
    return;
  }
  validateDatasetRootForTask("test").then((ok) => {
    if (!ok) return;
    submit("/api/opencd/test", {
      ConfigPath: $("configSelect").value,
      CheckpointPath: ckpt,
      DatasetRoot: $("datasetRoot").value || null,
      WorkDir: $("workDir").value,
      Out: $("testOut").value,
      ShowDirPred: true,
      ExtraArgs: $("extraArgs").value || null
    }, "测试任务已提交");
  }).catch((e) => setStatus("status", String(e), true));
};

$("loadScalars").onclick = () => loadScalars().catch((e) => setStatus("status", String(e), true));

$("importParams").onchange = async (e) => {
  const f = e.target.files?.[0];
  if (!f) return;
  try {
    await importParams(f);
    setStatus("status", "参数导入成功");
  } catch (err) {
    setStatus("status", String(err), true);
  }
};

Promise.all([loadConfigs(), loadCheckpoints(), renderJobs("jobChips"), loadScalars()])
  .catch((e) => setStatus("status", String(e), true));

setInterval(() => {
  renderJobs("jobChips").catch(() => {});
  if ($("jobId").value.trim()) loadLog("jobId", "jobLog").catch(() => {});
}, 5000);

let resizeTimer = null;
window.addEventListener("resize", () => {
  if (!state.scalars) return;
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => drawChart(), 120);
});
