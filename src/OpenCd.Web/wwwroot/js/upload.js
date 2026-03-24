attachNav("upload");

function appendLog(msg) {
  const el = $("uploadLog");
  const now = new Date().toLocaleTimeString();
  el.textContent = `[${now}] ${msg}\n` + (el.textContent || "");
}

async function postUpload(target, files) {
  if (!files || files.length === 0) {
    appendLog("未选择文件。");
    return;
  }

  const form = new FormData();
  form.append("target", target);

  for (const f of files) {
    const relative = f.webkitRelativePath || f.name;
    form.append("files", f, relative);
  }

  appendLog(`开始上传 ${files.length} 个文件...`);
  const res = await api("/api/upload", {
    method: "POST",
    body: form
  });

  appendLog(`上传完成: target=${res.Target}, count=${res.Count}, root=${res.Root}`);
}

$("uploadDataset").onclick = async () => {
  try {
    const files = [
      ...Array.from($("datasetFiles").files || []),
      ...Array.from($("datasetFolder").files || [])
    ];
    await postUpload($("datasetTarget").value, files);
  } catch (e) {
    appendLog(`上传失败: ${String(e)}`);
  }
};

$("uploadModel").onclick = async () => {
  try {
    const files = Array.from($("modelFiles").files || []);
    await postUpload($("modelTarget").value, files);
  } catch (e) {
    appendLog(`上传失败: ${String(e)}`);
  }
};
