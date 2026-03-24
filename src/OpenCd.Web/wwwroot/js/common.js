window.$ = (id) => document.getElementById(id);

window.getAuthToken = () => sessionStorage.getItem("ocd_token") || "";
window.setAuthToken = (token) => {
  sessionStorage.setItem("ocd_token", token || "");
  // cleanup old persistent token from previous versions
  localStorage.removeItem("ocd_token");
};
window.clearAuthToken = () => {
  sessionStorage.removeItem("ocd_token");
  localStorage.removeItem("ocd_token");
};

window.logout = () => {
  window.clearAuthToken();
  if (!location.pathname.endsWith("/login.html")) {
    location.href = "/login.html";
  }
};

window.withAuthHeaders = (headers = {}) => {
  const merged = { ...(headers || {}) };
  const token = window.getAuthToken();
  if (token) merged.Authorization = `Bearer ${token}`;
  return merged;
};

window.api = async (url, options) => {
  const opts = { ...(options || {}) };
  opts.headers = window.withAuthHeaders(opts.headers);
  const res = await fetch(url, opts);
  if (res.status === 401) {
    window.clearAuthToken();
    if (!location.pathname.endsWith("/login.html")) {
      location.href = "/login.html";
    }
    throw new Error("Unauthorized");
  }
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `HTTP ${res.status}`);
  }
  const contentType = res.headers.get("content-type") || "";
  return contentType.includes("application/json") ? res.json() : res.text();
};

window.postJson = (url, body) => api(url, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(body)
});

window.requireAuth = async () => {
  if (location.pathname.endsWith("/login.html")) return;
  const token = window.getAuthToken();
  if (!token) {
    location.href = "/login.html";
    return;
  }
  try {
    await window.api("/api/auth/me");
  } catch {
    window.clearAuthToken();
    location.href = "/login.html";
  }
};

window.parseNumber = (id) => Number($(id).value);

window.setStatus = (id, message, isError = false) => {
  const el = $(id);
  if (!el) return;
  el.textContent = message;
  el.style.color = isError ? "#b91c1c" : "#475569";
};

window.attachNav = (activePage) => {
  const links = document.querySelectorAll(".nav a");
  links.forEach((a) => {
    if (a.dataset.page === activePage) a.classList.add("active");
  });
};

window.renderJobs = async (chipsId) => {
  const jobs = await api("/api/jobs");
  const wrap = $(chipsId);
  if (!wrap) return jobs;
  wrap.innerHTML = "";
  const statusMap = {
    0: "Pending",
    1: "Running",
    2: "Succeeded",
    3: "Failed",
    4: "Canceled"
  };
  jobs.slice(0, 20).forEach((j) => {
    const btn = document.createElement("button");
    btn.className = "chip";
    const s = statusMap[j.Status] ?? String(j.Status);
    btn.textContent = `${j.Type} | ${s} | ${j.Id.slice(0, 8)}`;
    btn.onclick = () => {
      const input = document.querySelector("#jobId");
      if (input) input.value = j.Id;
    };
    wrap.appendChild(btn);
  });
  return jobs;
};

window.loadLog = async (jobIdInputId, logId) => {
  const id = $(jobIdInputId)?.value.trim();
  if (!id) return;
  const statusMap = {
    0: "Pending",
    1: "Running",
    2: "Succeeded",
    3: "Failed",
    4: "Canceled"
  };
  const job = await api(`/api/jobs/${id}`);
  const data = await api(`/api/jobs/${id}/log?tail=500`);
  const header = [
    `Job: ${job.Type} | ${statusMap[job.Status] ?? job.Status} | ${job.Id}`,
    `Started: ${job.StartedAt || "-"}`,
    `Ended: ${job.EndedAt || "-"}`,
    `ExitCode: ${job.ExitCode ?? "-"}`,
    `Error: ${job.Error || "-"}`
  ].join("\n");
  $(logId).textContent = `${header}\n\n${(data.Lines || []).join("\n")}`;
};

window.pickPath = async ({ mode = "directory", startPath = "", title = "", allFiles = false } = {}) => {
  let modal = document.getElementById("pathPickerModal");
  if (!modal) {
    const host = document.createElement("div");
    host.innerHTML = `
      <div id="pathPickerModal" class="pp-backdrop hidden">
        <div class="pp-modal">
          <div class="pp-head">
            <strong id="ppTitle">选择路径</strong>
            <button id="ppClose" type="button">关闭</button>
          </div>
          <div class="pp-toolbar">
            <input id="ppPathInput" />
            <button id="ppGo" type="button">进入</button>
            <button id="ppUp" type="button">上级</button>
          </div>
          <div id="ppStatus" class="note">加载中...</div>
          <div id="ppList" class="pp-list"></div>
          <div class="pp-actions">
            <button id="ppCancel" type="button">取消</button>
            <button id="ppCreateDir" type="button">新建文件夹</button>
            <button id="ppSelectDir" type="button">选择当前目录</button>
          </div>
        </div>
      </div>`;
    document.body.appendChild(host.firstElementChild);
    modal = document.getElementById("pathPickerModal");
  }

  const $q = (id) => document.getElementById(id);
  const titleEl = $q("ppTitle");
  const statusEl = $q("ppStatus");
  const listEl = $q("ppList");
  const pathInput = $q("ppPathInput");
  const btnSelectDir = $q("ppSelectDir");
  const btnCreateDir = $q("ppCreateDir");

  titleEl.textContent = title || (mode === "file" ? "选择文件" : "选择目录");
  btnSelectDir.style.display = mode === "directory" ? "inline-flex" : "none";
  btnCreateDir.style.display = mode === "directory" ? "inline-flex" : "none";

  let current = startPath || "";
  let parentPath = null;

  const load = async (path) => {
    const endpoint = allFiles || mode === "file" ? "/api/fs/list-all" : "/api/fs/list";
    const q = path ? `?path=${encodeURIComponent(path)}&dirsOnly=${mode === "directory"}` : `?dirsOnly=${mode === "directory"}`;
    const data = await window.api(`${endpoint}${q}`);
    current = data.CurrentPath;
    parentPath = data.ParentPath;
    pathInput.value = current;
    statusEl.textContent = `当前目录: ${current}`;
    listEl.innerHTML = "";
    (data.Entries || []).forEach((entry) => {
      const row = document.createElement("button");
      row.type = "button";
      row.className = "pp-row";
      row.textContent = `${entry.IsDirectory ? "[D]" : "[F]"} ${entry.Name}`;
      row.onclick = async () => {
        if (entry.IsDirectory) {
          await load(entry.Path);
          return;
        }
        if (mode === "file") {
          cleanup();
          resolve(entry.Path);
        }
      };
      listEl.appendChild(row);
    });
  };

  let resolve;
  let reject;
  const promise = new Promise((res, rej) => {
    resolve = res;
    reject = rej;
  });

  const onCancel = () => {
    cleanup();
    reject(new Error("Canceled"));
  };
  const onClose = onCancel;
  const onGo = () => load(pathInput.value.trim()).catch((e) => { statusEl.textContent = String(e); });
  const onUp = () => {
    if (!parentPath) return;
    load(parentPath).catch((e) => { statusEl.textContent = String(e); });
  };
  const onSelectDir = () => {
    cleanup();
    resolve(current);
  };
  const onCreateDir = async () => {
    try {
      const raw = prompt("请输入新建文件夹名称");
      if (!raw) return;
      const name = raw.trim();
      if (!name) return;
      if (name.includes("/") || name.includes("\\")) {
        statusEl.textContent = "文件夹名称不能包含斜杠";
        return;
      }
      const target = `${current.replace(/[\\/]+$/, "")}/${name}`;
      await window.postJson("/api/fs/mkdir", { Path: target });
      await load(current);
      statusEl.textContent = `已创建: ${target}`;
    } catch (e) {
      statusEl.textContent = String(e);
    }
  };

  const cleanup = () => {
    modal.classList.add("hidden");
    $q("ppCancel").removeEventListener("click", onCancel);
    $q("ppClose").removeEventListener("click", onClose);
    $q("ppGo").removeEventListener("click", onGo);
    $q("ppUp").removeEventListener("click", onUp);
    $q("ppSelectDir").removeEventListener("click", onSelectDir);
    $q("ppCreateDir").removeEventListener("click", onCreateDir);
  };

  $q("ppCancel").addEventListener("click", onCancel);
  $q("ppClose").addEventListener("click", onClose);
  $q("ppGo").addEventListener("click", onGo);
  $q("ppUp").addEventListener("click", onUp);
  $q("ppSelectDir").addEventListener("click", onSelectDir);
  $q("ppCreateDir").addEventListener("click", onCreateDir);

  modal.classList.remove("hidden");
  await load(current);
  return promise;
};

document.addEventListener("DOMContentLoaded", () => {
  window.requireAuth();
});
