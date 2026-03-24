attachNav("upload");

function appendLog(msg) {
  const el = $("uploadLog");
  const now = new Date().toLocaleTimeString();
  el.textContent = `[${now}] ${msg}\n` + (el.textContent || "");
}

function formatBytes(bytes) {
  if (!Number.isFinite(bytes) || bytes < 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let i = 0;
  let n = bytes;
  while (n >= 1024 && i < units.length - 1) {
    n /= 1024;
    i += 1;
  }
  return `${n.toFixed(i === 0 ? 0 : 2)} ${units[i]}`;
}

function setProgress(percent, loaded, total, text) {
  const bar = $("uploadProgress");
  const label = $("uploadProgressText");
  bar.value = Math.max(0, Math.min(100, percent || 0));
  if (text) {
    label.textContent = text;
    return;
  }
  label.textContent = `${Math.round(percent || 0)}% (${formatBytes(loaded)} / ${formatBytes(total)})`;
}

function uploadWithProgress(form) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", "/api/upload", true);

    xhr.upload.onprogress = (evt) => {
      if (!evt.lengthComputable) {
        setProgress(0, 0, 0, "上传中...");
        return;
      }
      const percent = (evt.loaded / evt.total) * 100;
      setProgress(percent, evt.loaded, evt.total);
    };

    xhr.onload = () => {
      let body = null;
      try {
        body = xhr.responseText ? JSON.parse(xhr.responseText) : null;
      } catch {
      }

      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(body);
        return;
      }

      const detail = body?.detail || body?.title || xhr.responseText || `HTTP ${xhr.status}`;
      reject(new Error(`HTTP ${xhr.status}: ${detail}`));
    };
    xhr.onerror = () => reject(new Error("网络错误，上传中断。"));
    xhr.send(form);
  });
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

  setProgress(0, 0, 0, "准备上传...");
  appendLog(`开始上传 ${files.length} 个文件...`);
  const res = await uploadWithProgress(form);
  setProgress(100, 1, 1, "上传完成");
  appendLog(`上传完成: target=${res.Target}, count=${res.Count}, skipped=${res.Skipped || 0}, root=${res.Root}`);
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
