attachNav("infer");

const state = {
  activeNode: null,
  hasSample: false,
  currentDim: null,
  vectorCache: new Map(),
  requestToken: 0
};

function normalizeDisplayName(name) {
  return String(name || "").replace(/^(train_|val_|test_)/i, "");
}

function setTreeStatus(message, isError = false) {
  const el = $("treeStatus");
  el.textContent = message;
  el.style.color = isError ? "#b91c1c" : "var(--muted)";
}

function setActiveNode(el) {
  if (state.activeNode) state.activeNode.classList.remove("selected");
  state.activeNode = el;
  if (state.activeNode) state.activeNode.classList.add("selected");
}

function renderVectors(svgId, vectors) {
  const svg = $(svgId);
  svg.innerHTML = "";
  if (!vectors?.Features?.length) return;

  vectors.Features.forEach((feature) => {
    (feature.Rings || []).forEach((ring) => {
      if (!ring || ring.length < 3) return;
      const poly = document.createElementNS("http://www.w3.org/2000/svg", "polygon");
      poly.setAttribute("points", ring.map((p) => `${p.X},${p.Y}`).join(" "));
      svg.appendChild(poly);
    });
  });
}

function updateLayerVisibility() {
  const showA = $("layerA").checked;
  const showGt = $("layerGt").checked;
  const showPred = $("layerPred").checked;

  $("imgBase").style.display = showA ? "block" : "none";
  $("svgGt").style.display = showGt ? "block" : "none";
  $("svgPred").style.display = showPred ? "block" : "none";

  $("viewerEmpty").style.display = state.hasSample ? "none" : "grid";
}

function calcContainRect(containerW, containerH, imageW, imageH) {
  if (!containerW || !containerH || !imageW || !imageH) return null;
  const scale = Math.min(containerW / imageW, containerH / imageH);
  const width = imageW * scale;
  const height = imageH * scale;
  const left = (containerW - width) / 2;
  const top = (containerH - height) / 2;
  return { left, top, width, height };
}

function applyOverlayRect(imageW, imageH) {
  const canvas = $("overlayCanvas");
  const rect = calcContainRect(canvas.clientWidth, canvas.clientHeight, imageW, imageH);
  if (!rect) return;

  ["imgBase", "svgGt", "svgPred"].forEach((id) => {
    const el = $(id);
    el.style.left = `${rect.left}px`;
    el.style.top = `${rect.top}px`;
    el.style.width = `${rect.width}px`;
    el.style.height = `${rect.height}px`;
    el.style.right = "auto";
    el.style.bottom = "auto";
  });
}

function waitImageLoaded(img) {
  if (img.complete && img.naturalWidth > 0) return Promise.resolve();
  return new Promise((resolve) => {
    const done = () => {
      img.removeEventListener("load", done);
      img.removeEventListener("error", done);
      resolve();
    };
    img.addEventListener("load", done, { once: true });
    img.addEventListener("error", done, { once: true });
  });
}

async function chooseFolder(inputId) {
  const current = $(inputId).value?.trim() || "";
  const path = await pickPath({
    mode: "directory",
    startPath: current,
    title: "选择目录"
  });
  $(inputId).value = path;
  return path;
}

async function chooseModelFile() {
  const current = $("modelPath").value?.trim();
  const health = await api("/api/health");
  const startPath = current || health.repoRoot;
  const path = await pickPath({
    mode: "file",
    startPath,
    title: "选择模型文件（.pth）",
    allFiles: true
  });
  $("modelPath").value = path;
  setTreeStatus(`已选择模型: ${path.split("/").slice(-2).join("/")}`);
}

async function autoSelectBestModel() {
  if ($("modelPath").value.trim()) return;

  const bestList = await api("/api/opencd/checkpoints?keyword=best");
  const best = (bestList || [])[0];
  if (best) {
    $("modelPath").value = best;
    setTreeStatus(`已自动选择最佳模型: ${best}`);
    return;
  }

  const all = await api("/api/opencd/checkpoints");
  const latest = (all || [])[0];
  if (latest) {
    $("modelPath").value = latest;
    setTreeStatus(`未找到 best，已自动选择最新模型: ${latest}`);
  }
}

function updatePairInfo(match, gtVectors, predVectors) {
  const model = $("modelPath").value.trim();
  const sampleDisplay = normalizeDisplayName(match.Sample);
  $("pairInfo").innerHTML = [
    `<div><strong>Sample:</strong> ${sampleDisplay}</div>`,
    `<div><strong>Split:</strong> ${match.Split}</div>`,
    `<div><strong>A:</strong> ${match.APath}</div>`,
    `<div><strong>B:</strong> ${match.BPath}</div>`,
    `<div><strong>Pred:</strong> ${match.PredPath}</div>`,
    `<div><strong>GT Polygons:</strong> ${gtVectors?.Features?.length || 0}</div>`,
    `<div><strong>Pred Polygons:</strong> ${predVectors?.Features?.length || 0}</div>`,
    `<div><strong>Model:</strong> ${model || "未选择"}</div>`
  ].join("");
}

async function getVectors(vectorUrl, cacheKey) {
  if (state.vectorCache.has(cacheKey)) return state.vectorCache.get(cacheKey);
  const v = await api(vectorUrl);
  state.vectorCache.set(cacheKey, v);
  return v;
}

async function selectImage(path, nodeEl) {
  const predRoot = $("predRoot").value.trim();
  if (!predRoot) {
    setTreeStatus("请先选择预测目录。", true);
    return;
  }

  const token = ++state.requestToken;
  setActiveNode(nodeEl);
  $("viewerPath").textContent = path;

  const match = await api(`/api/data/match-infer?path=${encodeURIComponent(path)}&predRoot=${encodeURIComponent(predRoot)}`);
  if (token !== state.requestToken) return;

  const imgBase = $("imgBase");
  imgBase.src = match.BPreviewUrl || "";

  await waitImageLoaded(imgBase);
  if (token !== state.requestToken) return;

  const width = imgBase.naturalWidth;
  const height = imgBase.naturalHeight;
  if (width && height) {
    state.currentDim = { width, height };
    applyOverlayRect(width, height);
  }

  const [gtVectors, predVectors] = await Promise.all([
    getVectors(match.LabelVectorUrl, match.LabelPath),
    getVectors(match.PredVectorUrl, match.PredPath)
  ]);
  if (token !== state.requestToken) return;

  renderVectors("svgGt", gtVectors);
  renderVectors("svgPred", predVectors);
  updatePairInfo(match, gtVectors, predVectors);

  state.hasSample = true;
  updateLayerVisibility();
}

function buildNode(entry) {
  const li = document.createElement("li");
  li.className = "tree-li";

  const btn = document.createElement("button");
  btn.className = "tree-node";
  const displayName = entry.IsDirectory ? entry.Name : normalizeDisplayName(entry.Name);
  btn.textContent = entry.IsDirectory ? `[D] ${displayName}` : `[F] ${displayName}`;
  li.appendChild(btn);

  if (entry.IsDirectory) {
    btn.onclick = async () => {
      if (li.dataset.loaded === "1") {
        const sub = li.querySelector("ul");
        if (sub) sub.classList.toggle("hidden");
        return;
      }

      const data = await api(`/api/fs/list?path=${encodeURIComponent(entry.Path)}&dirsOnly=false`);
      const ul = document.createElement("ul");
      ul.className = "tree-ul";
      data.Entries.forEach((child) => ul.appendChild(buildNode(child)));
      li.appendChild(ul);
      li.dataset.loaded = "1";
    };
  } else {
    btn.onclick = () => selectImage(entry.Path, btn).catch((err) => setTreeStatus(String(err), true));
  }

  return li;
}

async function loadTree(path) {
  const q = path ? `?path=${encodeURIComponent(path)}&dirsOnly=false` : "?dirsOnly=false";
  const data = await api(`/api/fs/list${q}`);

  $("treeRoot").value = data.CurrentPath;

  const pane = $("treePane");
  pane.innerHTML = "";

  const rootNode = document.createElement("div");
  rootNode.className = "tree-node root";
  rootNode.textContent = data.CurrentPath;
  pane.appendChild(rootNode);

  const list = document.createElement("ul");
  list.className = "tree-ul";
  pane.appendChild(list);

  if (data.ParentPath) {
    const liUp = document.createElement("li");
    liUp.className = "tree-li";
    const upBtn = document.createElement("button");
    upBtn.className = "tree-node";
    upBtn.textContent = "..";
    upBtn.onclick = () => loadTree(data.ParentPath).catch((err) => setTreeStatus(String(err), true));
    liUp.appendChild(upBtn);
    list.appendChild(liUp);
  }

  data.Entries.forEach((entry) => list.appendChild(buildNode(entry)));
  setTreeStatus("目录已加载，点击 [F] 测试影像进行解译对比。", false);
}

$("browseRoot").onclick = async () => {
  try {
    setTreeStatus("正在打开 Finder...", false);
    const path = await chooseFolder("treeRoot");
    await loadTree(path);
  } catch (err) {
    setTreeStatus(String(err), true);
  }
};

$("browsePredRoot").onclick = async () => {
  try {
    setTreeStatus("正在选择预测目录...", false);
    await chooseFolder("predRoot");
    setTreeStatus(`已选择预测目录: ${$("predRoot").value}`);
  } catch (err) {
    setTreeStatus(String(err), true);
  }
};

$("browseModel").onclick = () => chooseModelFile().catch((err) => setTreeStatus(String(err), true));

$("layerA").onchange = updateLayerVisibility;
$("layerGt").onchange = updateLayerVisibility;
$("layerPred").onchange = updateLayerVisibility;

$("treeRoot").addEventListener("keydown", (e) => {
  if (e.key === "Enter") {
    loadTree($("treeRoot").value.trim()).catch((err) => setTreeStatus(String(err), true));
  }
});

updateLayerVisibility();
Promise.all([
  autoSelectBestModel(),
  loadTree($("treeRoot").value.trim())
]).catch((err) => setTreeStatus(String(err), true));

let resizeTimer = null;
window.addEventListener("resize", () => {
  if (!state.hasSample || !state.currentDim) return;
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => {
    applyOverlayRect(state.currentDim.width, state.currentDim.height);
  }, 80);
});
