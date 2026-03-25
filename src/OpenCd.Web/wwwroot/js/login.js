function setLoginStatus(msg, isErr = false) {
  const el = $("loginStatus");
  if (!el) return;
  el.textContent = msg;
  el.style.color = isErr ? "#b91c1c" : "#475569";
}

async function handleAdminLogin() {
  try {
    const username = $("username").value.trim();
    const password = $("password").value;
    if (!username || !password) {
      setLoginStatus("请输入管理员账号和密码。", true);
      return;
    }

    const res = await postJson("/api/auth/login", { Username: username, Password: password });
    window.setAuthToken(res.Token);
    window.setCurrentUser({ Username: res.Username, Role: res.Role });
    setLoginStatus("登录成功，正在跳转...");
    location.href = "/";
  } catch (e) {
    setLoginStatus(`管理员登录失败: ${String(e)}`, true);
  }
}

async function handleInternalLogin() {
  try {
    const res = await postJson("/api/auth/internal-login", {});
    window.setAuthToken(res.Token);
    window.setCurrentUser({ Username: res.Username, Role: res.Role });
    setLoginStatus("内测登录成功，正在跳转...");
    location.href = "/";
  } catch (e) {
    setLoginStatus(`内测登录失败: ${String(e)}`, true);
  }
}

document.addEventListener("DOMContentLoaded", () => {
  $("adminLogin").onclick = handleAdminLogin;
  $("internalLogin").onclick = handleInternalLogin;

  $("password").addEventListener("keydown", (ev) => {
    if (ev.key === "Enter") handleAdminLogin();
  });

  const token = window.getAuthToken();
  if (token) {
    window.api("/api/auth/me")
      .then(() => {
        location.href = "/";
      })
      .catch(() => {
        window.clearAuthToken();
      });
  }
});
