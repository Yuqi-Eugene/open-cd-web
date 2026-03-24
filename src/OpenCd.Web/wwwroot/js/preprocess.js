attachNav("prep");

function trimTrailingSlash(p) {
  return (p || "").replace(/[\\/]+$/, "");
}

function parentPath(p) {
  const s = trimTrailingSlash((p || "").replace(/\\/g, "/"));
  if (!s) return "";
  const i = s.lastIndexOf("/");
  if (i <= 0) return "";
  return s.slice(0, i);
}

function deriveUint8Out(inDir) {
  const s = trimTrailingSlash((inDir || "").replace(/\\/g, "/"));
  if (!s) return "";
  const i = s.lastIndexOf("/");
  if (i <= 0) {
    return `${s}_uint8`;
  }
  const parent = s.slice(0, i);
  const leaf = s.slice(i + 1);
  return `${parent}_uint8/${leaf}`;
}

function autoSyncUint8Out(previousIn, nextIn) {
  const out = $("uint8Out");
  if (!out) return;
  const prevDerived = deriveUint8Out(previousIn);
  const curOut = trimTrailingSlash(out.value);
  if (!curOut || curOut === prevDerived) {
    out.value = deriveUint8Out(nextIn);
  }
}

let lastUint8InValue = $("uint8In")?.value?.trim?.() || "";

async function chooseDirectory(targetInputId) {
  try {
    const current = $(targetInputId).value?.trim() || "";
    const fallback = targetInputId === "uint8Out" ? (parentPath(current) || $("uint8In").value?.trim() || "") : current;
    const path = await pickPath({
      mode: "directory",
      startPath: fallback,
      title: "选择目录"
    });
    const previous = current;
    $(targetInputId).value = path;
    if (targetInputId === "uint8In") {
      autoSyncUint8Out(previous, path);
      lastUint8InValue = path;
    }
    setStatus("status", `已选择目录: ${path}`);
  } catch (e) {
    setStatus("status", String(e), true);
  }
}

async function chooseFile(targetInputId) {
  try {
    const current = $(targetInputId).value?.trim() || "";
    const path = await pickPath({
      mode: "file",
      startPath: current,
      title: "选择文件",
      allFiles: true
    });
    $(targetInputId).value = path;
    setStatus("status", `已选择文件: ${path}`);
  } catch (e) {
    setStatus("status", String(e), true);
  }
}

async function detectPython(refresh = false) {
  try {
    const q = refresh ? "?refresh=true" : "";
    const data = await api(`/api/system/python/detect${q}`);
    if (data.Found && data.Python) {
      $("pythonExec").value = data.Python;
      $("pythonHint").textContent = `已自动检测: ${data.Python}`;
      $("pythonHint").style.color = "#166534";
      setStatus("status", "已自动配置 Python 环境");
    } else {
      $("pythonHint").textContent = "未检测到可用 Python（需包含 numpy/rasterio/tqdm）";
      $("pythonHint").style.color = "#b91c1c";
      setStatus("status", "自动检测失败，请手动选择 Python", true);
    }
  } catch (e) {
    $("pythonHint").textContent = String(e);
    $("pythonHint").style.color = "#b91c1c";
    setStatus("status", String(e), true);
  }
}

async function submit(url, body, message) {
  try {
    const res = await postJson(url, body);
    setStatus("status", `${message}，JobId: ${res.Id}`);
    $("jobId").value = res.Id;
    await renderJobs("jobChips");
    await loadLog("jobId", "jobLog");
  } catch (e) {
    setStatus("status", String(e), true);
  }
}

$("runRename").onclick = () => submit("/api/preprocess/batch-name", {
  Directory: $("renameDir").value,
  Recursive: $("renameRecursive").checked,
  Ext: $("renameExt").value,
  AddPrefix: $("renamePrefix").value,
  AddSuffix: $("renameSuffix").value,
  RemovePrefix: $("renameRemovePrefix").value,
  RemoveSuffix: $("renameRemoveSuffix").value,
  Apply: true,
  Python: $("pythonExec").value || null
}, "命名任务已提交");

$("runUint8").onclick = () => submit("/api/preprocess/uint8", {
  InDir: $("uint8In").value,
  OutDir: $("uint8Out").value,
  ClipP2: parseNumber("clipP2"),
  ClipP98: parseNumber("clipP98"),
  NoScale: $("noScale").checked,
  MapNodataTo: parseNumber("mapNodata"),
  NodataValue: parseNumber("nodataValue"),
  Python: $("pythonExec").value || null
}, "uint8 任务已提交");

$("runSplit").onclick = () => submit("/api/preprocess/split", {
  InDir: $("splitIn").value,
  OutDir: $("splitOut").value,
  Train: parseNumber("ratioTrain"),
  Val: parseNumber("ratioVal"),
  Test: parseNumber("ratioTest"),
  Method: $("splitMethod").value,
  Apply: true,
  Python: $("pythonExec").value || null
}, "切分任务已提交");

$("refreshJobs").onclick = async () => {
  await renderJobs("jobChips");
  setStatus("status", "任务列表已刷新");
};

$("viewLog").onclick = async () => {
  try {
    await loadLog("jobId", "jobLog");
    setStatus("status", "日志已更新");
  } catch (e) {
    setStatus("status", String(e), true);
  }
};
$("detectPython").onclick = () => detectPython(true);

document.querySelectorAll("button[data-browse]").forEach((btn) => {
  btn.addEventListener("click", () => chooseDirectory(btn.dataset.browse));
});
document.querySelectorAll("button[data-browse-file]").forEach((btn) => {
  btn.addEventListener("click", () => chooseFile(btn.dataset.browseFile));
});

$("uint8In").addEventListener("change", (ev) => {
  const nextIn = ev.target?.value || "";
  autoSyncUint8Out(lastUint8InValue, nextIn);
  lastUint8InValue = nextIn;
});

// Keep default output convention as: parent + _uint8 + /leaf
(() => {
  const inDir = $("uint8In")?.value?.trim() || "";
  const outDir = $("uint8Out")?.value?.trim() || "";
  if (!outDir || outDir.endsWith("/A_uint8") || outDir.endsWith("\\A_uint8")) {
    $("uint8Out").value = deriveUint8Out(inDir);
  }
})();

detectPython(false).catch(() => {});
renderJobs("jobChips").catch((e) => setStatus("status", String(e), true));
setInterval(() => {
  renderJobs("jobChips").catch(() => {});
  if ($("jobId").value.trim()) loadLog("jobId", "jobLog").catch(() => {});
}, 5000);
