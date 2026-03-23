window.$ = (id) => document.getElementById(id);

window.api = async (url, options) => {
  const res = await fetch(url, options);
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
