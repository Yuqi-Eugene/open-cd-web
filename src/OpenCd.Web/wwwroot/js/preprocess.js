attachNav("prep");

async function chooseDirectory(targetInputId) {
  try {
    const current = $(targetInputId).value?.trim() || "";
    const q = current ? `?startPath=${encodeURIComponent(current)}` : "";
    const data = await api(`/api/system/choose-directory${q}`);
    $(targetInputId).value = data.Path;
    setStatus("status", `已选择目录: ${data.Path}`);
  } catch (e) {
    setStatus("status", String(e), true);
  }
}

async function chooseFile(targetInputId) {
  try {
    const current = $(targetInputId).value?.trim() || "";
    const q = current ? `?startPath=${encodeURIComponent(current)}` : "";
    const data = await api(`/api/system/choose-file${q}`);
    $(targetInputId).value = data.Path;
    setStatus("status", `已选择文件: ${data.Path}`);
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

detectPython(false).catch(() => {});
renderJobs("jobChips").catch((e) => setStatus("status", String(e), true));
setInterval(() => {
  renderJobs("jobChips").catch(() => {});
  if ($("jobId").value.trim()) loadLog("jobId", "jobLog").catch(() => {});
}, 5000);
