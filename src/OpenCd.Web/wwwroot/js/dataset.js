attachNav("data");

const state = {
  root: "data/wj1024_split",
  activeNode: null,
  vectors: null,
  comparePercent: 50,
  hasSample: false,
  viewRect: null,
  dimCache: new Map(),
  vectorCache: new Map(),
  requestToken: 0
};

function colorByClass(v) {
  const map = {
    1: "rgba(239, 68, 68, 0.34)",
    2: "rgba(16, 185, 129, 0.34)",
    3: "rgba(245, 158, 11, 0.32)"
  };
  return map[v] || "rgba(59, 130, 246, 0.30)";
}

function strokeByClass(v) {
  const map = {
    1: "#ef4444",
    2: "#10b981",
    3: "#f59e0b"
  };
  return map[v] || "#3b82f6";
}

function setStatus(message, isError = false) {
  const el = $("treeStatus");
  el.textContent = message;
  el.style.color = isError ? "#b91c1c" : "var(--muted)";
}

function setActiveNode(el) {
  if (state.activeNode) state.activeNode.classList.remove("selected");
  state.activeNode = el;
  if (state.activeNode) state.activeNode.classList.add("selected");
}

function renderVectors(vectors) {
  const svg = $("svgLabel");
  svg.innerHTML = "";
  if (!vectors?.Features?.length) return;

  vectors.Features.forEach((feature) => {
    (feature.Rings || []).forEach((ring) => {
      if (!ring || ring.length < 3) return;
      const poly = document.createElementNS("http://www.w3.org/2000/svg", "polygon");
      poly.setAttribute("points", ring.map((p) => `${p.X},${p.Y}`).join(" "));
      poly.setAttribute("fill", colorByClass(feature.ClassValue));
      poly.setAttribute("stroke", strokeByClass(feature.ClassValue));
      poly.setAttribute("stroke-width", "0.0025");
      svg.appendChild(poly);
    });
  });
}

function updateCompare() {
  const slider = $("compareSlider");
  const v = Math.max(0, Math.min(100, Number(slider.value)));
  slider.value = String(v);
  state.comparePercent = v;
  $("imgB").style.clipPath = `inset(0 0 0 ${v}%)`;

  const line = $("compareLine");
  const rect = state.viewRect;
  if (rect) {
    const x = Math.max(rect.left, Math.min(rect.left + rect.width, rect.left + (rect.width * v) / 100));
    line.style.left = `${x}px`;
    line.style.top = `${rect.top}px`;
    line.style.height = `${rect.height}px`;
    line.style.bottom = "auto";
  } else {
    line.style.left = `${v}%`;
    line.style.top = "0";
    line.style.height = "100%";
    line.style.bottom = "0";
  }
}

function updateLayerVisibility() {
  const showA = $("layerA").checked;
  const showB = $("layerB").checked;
  const showLabel = $("layerLabel").checked;

  $("imgA").style.display = showA ? "block" : "none";
  $("imgB").style.display = showB ? "block" : "none";
  $("compareLine").style.display = showA && showB ? "block" : "none";
  $("compareSlider").disabled = !(showA && showB);
  $("svgLabel").style.display = showLabel ? "block" : "none";

  $("viewerEmpty").style.display = state.hasSample ? "none" : "grid";
}

function updatePairInfo(match, vectors) {
  $("pairInfo").innerHTML = [
    `<div><strong>Sample:</strong> ${match.Sample}</div>`,
    `<div><strong>Split:</strong> ${match.Split}</div>`,
    `<div><strong>A:</strong> ${match.APath}</div>`,
    `<div><strong>B:</strong> ${match.BPath}</div>`,
    `<div><strong>Label:</strong> ${match.LabelPath}</div>`,
    `<div><strong>Vectors:</strong> ${vectors?.Features?.length || 0}</div>`
  ].join("");
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

function applyViewerRect(imageW, imageH) {
  const canvas = $("compareCanvas");
  const imgA = $("imgA");
  if (!canvas || !imgA) return;

  const w = imageW || imgA.naturalWidth;
  const h = imageH || imgA.naturalHeight;
  if (!w || !h) return;

  const rect = calcContainRect(canvas.clientWidth, canvas.clientHeight, w, h);
  if (!rect) return;

  state.viewRect = rect;
  ["imgA", "imgB", "svgLabel"].forEach((id) => {
    const el = $(id);
    el.style.left = `${rect.left}px`;
    el.style.top = `${rect.top}px`;
    el.style.width = `${rect.width}px`;
    el.style.height = `${rect.height}px`;
    el.style.right = "auto";
    el.style.bottom = "auto";
  });

  updateCompare();
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

async function chooseRootFolder() {
  try {
    setStatus("打开目录索引...", false);
    const current = $("treeRoot").value?.trim() || "";
    const path = await pickPath({
      mode: "directory",
      startPath: current,
      title: "选择样本目录"
    });
    $("treeRoot").value = path;
    await loadTree(path);
    setStatus(`已选择目录: ${path}`, false);
  } catch (err) {
    const msg = String(err);
    setStatus(msg, true);
  }
}

async function selectImage(path, nodeEl) {
  const token = ++state.requestToken;
  setActiveNode(nodeEl);
  $("viewerPath").textContent = path;

  const match = await api(`/api/data/match?path=${encodeURIComponent(path)}`);
  if (token !== state.requestToken) return;

  const imgA = $("imgA");
  const imgB = $("imgB");
  const dimKey = match.APath.slice(0, match.APath.lastIndexOf("/"));
  const cachedDim = state.dimCache.get(dimKey);
  if (cachedDim) {
    applyViewerRect(cachedDim.width, cachedDim.height);
  }

  imgA.src = match.APreviewUrl || "";
  imgB.src = match.BPreviewUrl || "";

  Promise.all([waitImageLoaded(imgA), waitImageLoaded(imgB)]).then(() => {
    if (token !== state.requestToken) return;
    const width = imgA.naturalWidth || imgB.naturalWidth;
    const height = imgA.naturalHeight || imgB.naturalHeight;
    if (width && height) {
      state.dimCache.set(dimKey, { width, height });
      applyViewerRect(width, height);
    }
  }).catch(() => {});

  let vectors = state.vectorCache.get(match.LabelPath);
  if (!vectors) {
    vectors = await api(match.LabelVectorUrl);
    state.vectorCache.set(match.LabelPath, vectors);
  }
  if (token !== state.requestToken) return;
  state.vectors = vectors;
  renderVectors(vectors);

  state.hasSample = true;
  updateCompare();
  updatePairInfo(match, vectors);
  updateLayerVisibility();
}

function buildNode(entry) {
  const li = document.createElement("li");
  li.className = "tree-li";

  const btn = document.createElement("button");
  btn.className = "tree-node";
  btn.textContent = entry.IsDirectory ? `[D] ${entry.Name}` : `[F] ${entry.Name}`;
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
    btn.onclick = () => selectImage(entry.Path, btn).catch((err) => setStatus(String(err), true));
  }

  return li;
}

async function loadTree(path) {
  const q = path ? `?path=${encodeURIComponent(path)}&dirsOnly=false` : "?dirsOnly=false";
  const data = await api(`/api/fs/list${q}`);

  state.root = data.CurrentPath;
  $("treeRoot").value = state.root;

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
    upBtn.onclick = () => loadTree(data.ParentPath).catch((err) => setStatus(String(err), true));
    liUp.appendChild(upBtn);
    list.appendChild(liUp);
  }

  data.Entries.forEach((entry) => list.appendChild(buildNode(entry)));
  setStatus("目录已加载，点击 [F] 文件进入 Viewer。", false);
}

$("browseRoot").onclick = chooseRootFolder;
$("compareSlider").oninput = updateCompare;
$("layerA").onchange = updateLayerVisibility;
$("layerB").onchange = updateLayerVisibility;
$("layerLabel").onchange = updateLayerVisibility;
$("treeRoot").addEventListener("keydown", (e) => {
  if (e.key === "Enter") {
    loadTree($("treeRoot").value.trim()).catch((err) => setStatus(String(err), true));
  }
});

updateCompare();
updateLayerVisibility();
loadTree($("treeRoot").value.trim()).catch((err) => setStatus(String(err), true));

let resizeTimer = null;
window.addEventListener("resize", () => {
  if (!state.hasSample) return;
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => applyViewerRect(), 80);
});
