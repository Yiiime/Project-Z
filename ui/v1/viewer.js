(() => {
  "use strict";

  const byId = (id) => document.getElementById(id);
  const el = {
    viewer: byId("viewer"), navigationToggle: byId("navigation-toggle"), navigation: byId("chapter-navigation"),
    sidebar: byId("chapter-sidebar"), scrim: byId("sidebar-scrim"), projectTitle: byId("project-title"),
    scope: byId("project-scope"), version: byId("version-label"), total: byId("screen-total"),
    title: byId("screen-title"), context: byId("screen-context"), status: byId("screen-status"),
    stage: byId("image-stage"), imageButton: byId("image-button"), image: byId("screen-image"),
    message: byId("stage-message"), messageTitle: byId("stage-message-title"), messageText: byId("stage-message-text"),
    retry: byId("retry-button"), manifestButton: byId("manifest-button"), manifestInput: byId("manifest-input"),
    actions: byId("screen-actions"), actionStrip: byId("action-strip"), previous: byId("previous-button"),
    next: byId("next-button"), count: byId("page-count"), fullscreen: byId("fullscreen-button"),
    fullscreenLabel: byId("fullscreen-label"), detailsToggle: byId("details-toggle"),
    detailsToggleLabel: byId("details-toggle-label"), details: byId("details-sidebar"),
    detailsClose: byId("details-close"), goal: byId("screen-goal"), summary: byId("screen-summary"),
    notes: byId("screen-notes"), notesSection: byId("notes-section"), dialog: byId("image-dialog"),
    zoomImage: byId("zoom-image"), zoomTitle: byId("zoom-title"), zoomClose: byId("zoom-close"),
    toast: byId("toast"), announcement: byId("screen-announcement"),
  };

  const compact = window.matchMedia("(max-width: 960px)");
  const state = { manifest: null, screens: [], index: -1, imageReady: false, imageUrl: "", imageSequence: 0, manifestSequence: 0, toastTimer: null, retry: null };
  const cleanText = (value, fallback = "") => typeof value === "string" && value.trim() ? value.trim() : fallback;
  const numberLabel = (number) => String(number).padStart(2, "0");

  function showToast(message) {
    clearTimeout(state.toastTimer);
    el.toast.textContent = message;
    el.toast.hidden = false;
    state.toastTimer = setTimeout(() => { el.toast.hidden = true; }, 5000);
  }

  function syncScrim() {
    el.scrim.hidden = !compact.matches || (el.viewer.dataset.navigation !== "open" && el.viewer.dataset.details !== "open");
  }

  function setNavigation(open, restoreFocus = false) {
    el.viewer.dataset.navigation = open ? "open" : "closed";
    el.sidebar.inert = !open;
    el.navigationToggle.setAttribute("aria-expanded", String(open));
    el.navigationToggle.setAttribute("aria-label", open ? "收起章节导航" : "展开章节导航");
    if (open && compact.matches) setDetails(false);
    if (!open && restoreFocus) el.navigationToggle.focus();
    syncScrim();
  }

  function setDetails(open, restoreFocus = false) {
    el.viewer.dataset.details = open ? "open" : "closed";
    el.details.hidden = !open;
    el.detailsToggle.setAttribute("aria-expanded", String(open));
    el.detailsToggleLabel.textContent = open ? "隐藏说明" : "显示说明";
    if (open && compact.matches) setNavigation(false);
    if (open) el.detailsClose.focus({ preventScroll: true });
    if (!open && restoreFocus) el.detailsToggle.focus();
    syncScrim();
  }

  function showStageMessage(title, message, { retry = null, allowManifest = false, busy = false } = {}) {
    el.imageButton.hidden = true;
    el.message.hidden = false;
    el.messageTitle.textContent = title;
    el.messageText.textContent = message;
    el.stage.setAttribute("aria-busy", String(busy));
    el.retry.hidden = !retry;
    el.manifestButton.hidden = !allowManifest;
    state.retry = retry;
  }

  function validateManifest(data) {
    if (!data || typeof data !== "object" || !Array.isArray(data.screens) || !data.screens.length) {
      throw new Error("目录需要包含非空的 screens 数组。");
    }
    const seenIds = new Set();
    const screens = data.screens.map((screen, index) => {
      if (!screen || typeof screen !== "object") throw new Error(`第 ${index + 1} 项不是有效的界面记录。`);
      const id = cleanText(screen.id);
      if (!id) throw new Error(`第 ${index + 1} 项缺少 id。`);
      if (seenIds.has(id)) throw new Error(`界面 id 重复：${id}。`);
      seenIds.add(id);
      return {
        id, title: cleanText(screen.title, id), day: cleanText(String(screen.day ?? "")),
        phase: cleanText(screen.phase), group: cleanText(screen.group, "界面"),
        summary: cleanText(screen.summary, "此界面暂未提供内容说明。"),
        goal: cleanText(screen.goal, "此界面暂未提供玩家目标。"), image: cleanText(screen.image),
        actions: Array.isArray(screen.actions) ? screen.actions.filter((action) => action && typeof action === "object").map((action) => ({ label: cleanText(action.label, "查看界面"), target: cleanText(action.target) })) : [],
        notes: Array.isArray(screen.notes) ? screen.notes.map((note) => cleanText(note)).filter(Boolean) : [],
        status: cleanText(screen.status),
      };
    });
    return { title: cleanText(data.title, "来自月亮的 Z"), version: cleanText(String(data.version ?? "")), scope: cleanText(data.scope, "完整游戏 UI 示意集"), screens };
  }

  function readHash() {
    try { return decodeURIComponent(window.location.hash.slice(1)); }
    catch { return ""; }
  }

  function buildNavigation() {
    el.navigation.replaceChildren();
    const groups = new Map();
    state.screens.forEach((screen, index) => {
      if (!groups.has(screen.group)) groups.set(screen.group, []);
      groups.get(screen.group).push({ screen, index });
    });
    for (const [name, screens] of groups) {
      const group = document.createElement("section");
      group.className = "chapter-group";
      const heading = document.createElement("h2");
      heading.className = "chapter-group-title";
      heading.textContent = name;
      const list = document.createElement("ol");
      list.className = "chapter-list";
      for (const { screen, index } of screens) {
        const item = document.createElement("li");
        const link = document.createElement("a");
        link.className = "chapter-link";
        link.href = `#${encodeURIComponent(screen.id)}`;
        link.dataset.screenId = screen.id;
        const count = document.createElement("span");
        count.className = "chapter-index";
        count.textContent = numberLabel(index + 1);
        const name = document.createElement("span");
        name.className = "chapter-name";
        name.textContent = screen.title;
        link.append(count, name);
        const context = [screen.day, screen.phase].filter(Boolean).join(" · ");
        if (context) {
          const caption = document.createElement("span");
          caption.className = "chapter-context";
          caption.textContent = context;
          link.append(caption);
        }
        link.addEventListener("click", (event) => {
          if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
          event.preventDefault();
          navigate(index);
          if (compact.matches) setNavigation(false, true);
        });
        item.append(link);
        list.append(item);
      }
      group.append(heading, list);
      el.navigation.append(group);
    }
  }

  function resolveImage(path) {
    if (!path) throw new Error("目录中尚未提供图片路径。");
    const url = new URL(path, window.location.href);
    if (!["http:", "https:", "file:"].includes(url.protocol) || (window.location.protocol !== "file:" && url.origin !== window.location.origin) || (window.location.protocol === "file:" && url.protocol !== "file:")) {
      throw new Error("图片必须来自这份本地示意集，不能使用外部图片地址。");
    }
    return url.href;
  }

  function loadImage(screen) {
    const sequence = ++state.imageSequence;
    state.imageReady = false;
    state.imageUrl = "";
    el.image.removeAttribute("src");
    el.image.alt = `${screen.title} — 静态 UI 示意图`;
    showStageMessage("正在载入界面示意图", screen.image || "目录中尚未指定图片。", { busy: true });
    let url;
    try { url = resolveImage(screen.image); }
    catch (error) {
      showStageMessage("此界面尚无可显示的图片", `${error.message}\n这是 UI 示意集，不是正在运行的游戏。`, { retry: () => loadImage(screen) });
      return;
    }
    const pendingImage = new Image();
    pendingImage.onload = () => {
      if (sequence !== state.imageSequence) return;
      state.imageReady = true;
      state.imageUrl = url;
      el.image.src = url;
      el.imageButton.hidden = false;
      el.message.hidden = true;
      el.stage.setAttribute("aria-busy", "false");
      el.imageButton.setAttribute("aria-label", `放大查看 ${screen.title} 的静态 UI 示意图`);
    };
    pendingImage.onerror = () => {
      if (sequence !== state.imageSequence) return;
      showStageMessage("示意图未能载入", `未找到或无法读取：${screen.image}\n请确认图片已放入对应目录。你仍可浏览界面说明和其他页面。`, { retry: () => loadImage(screen) });
    };
    pendingImage.src = url;
  }

  function renderActions(screen) {
    el.actions.replaceChildren();
    el.actionStrip.hidden = screen.actions.length === 0;
    for (const action of screen.actions) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "action-button";
      button.textContent = action.label;
      const targetIndex = state.screens.findIndex((target) => target.id === action.target);
      button.disabled = targetIndex < 0;
      button.title = targetIndex < 0 ? `目标界面尚未收录：${action.target || "未指定"}` : `界面导览：${state.screens[targetIndex].title}`;
      button.setAttribute("aria-label", targetIndex < 0 ? `${action.label}，目标界面尚未收录` : `${action.label}，跳转到 ${state.screens[targetIndex].title} 的界面示意图`);
      button.addEventListener("click", () => {
        if (targetIndex < 0) return;
        navigate(targetIndex);
        el.stage.focus({ preventScroll: true });
      });
      el.actions.append(button);
    }
  }

  function renderScreen() {
    const screen = state.screens[state.index];
    if (!screen) return;
    closeZoom();
    el.title.textContent = screen.title;
    el.context.textContent = [screen.group, screen.day, screen.phase].filter(Boolean).join(" / ");
    el.status.textContent = screen.status;
    el.status.hidden = !screen.status;
    el.goal.textContent = screen.goal;
    el.summary.textContent = screen.summary;
    el.notes.replaceChildren();
    screen.notes.forEach((note) => {
      const item = document.createElement("li");
      item.textContent = note;
      el.notes.append(item);
    });
    el.notesSection.hidden = !screen.notes.length;
    el.count.textContent = `${numberLabel(state.index + 1)} / ${numberLabel(state.screens.length)}`;
    el.previous.disabled = state.index <= 0;
    el.next.disabled = state.index >= state.screens.length - 1;
    document.title = `${screen.title} · ${state.manifest.title} · 界面导览`;
    el.announcement.textContent = `第 ${state.index + 1} 张，共 ${state.screens.length} 张。${screen.title}。静态 UI 示意图。`;
    for (const link of el.navigation.querySelectorAll(".chapter-link")) {
      if (link.dataset.screenId === screen.id) link.setAttribute("aria-current", "page");
      else link.removeAttribute("aria-current");
    }
    renderActions(screen);
    loadImage(screen);
  }

  function navigate(index, replace = false) {
    if (index < 0 || index >= state.screens.length) return;
    const screen = state.screens[index];
    const hash = `#${encodeURIComponent(screen.id)}`;
    if (window.location.hash !== hash) {
      try { window.history[replace ? "replaceState" : "pushState"](null, "", hash); }
      catch { window.location.hash = hash; }
    }
    if (state.index === index) return;
    state.index = index;
    renderScreen();
  }

  function applyManifest(raw) {
    const manifest = validateManifest(raw);
    state.manifest = manifest;
    state.screens = manifest.screens;
    state.index = -1;
    el.projectTitle.textContent = manifest.title;
    el.scope.textContent = manifest.scope;
    el.version.textContent = manifest.version;
    el.total.textContent = `${numberLabel(manifest.screens.length)} 张`;
    buildNavigation();
    const requestedId = readHash();
    let index = state.screens.findIndex((screen) => screen.id === requestedId);
    if (index < 0) {
      index = state.screens.findIndex((screen) => screen.id === "01-title");
      if (index < 0) index = 0;
      if (requestedId) showToast(`未收录界面“${requestedId}”，已打开首张示意图。`);
    }
    navigate(index, true);
  }

  function readEmbeddedManifest() {
    const embedded = byId("embedded-manifest");
    if (!embedded || !embedded.textContent.trim()) throw new Error("页面未包含随包保存的界面目录。");
    return JSON.parse(embedded.textContent);
  }

  async function loadManifest() {
    const sequence = ++state.manifestSequence;
    const localFile = window.location.protocol === "file:";
    showStageMessage("正在载入界面目录", localFile ? "读取随包保存的界面目录。" : "读取同目录的 manifest.json。", { busy: true });
    if (localFile) {
      try {
        applyManifest(readEmbeddedManifest());
      } catch (error) {
        showStageMessage("随包目录未能载入", `${error.message}\n请选择与 index.html 同目录的 manifest.json。文件仅在本机读取，图片仍从 assets 文件夹显示。`, { retry: loadManifest, allowManifest: true });
      }
      return;
    }
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 10000);
    try {
      const response = await fetch(new URL("manifest.json", window.location.href), { cache: "no-store", signal: controller.signal });
      if (!response.ok) throw new Error(`manifest.json 返回 HTTP ${response.status}。`);
      const raw = await response.json();
      if (sequence === state.manifestSequence) applyManifest(raw);
    } catch (error) {
      if (sequence !== state.manifestSequence) return;
      const detail = error.name === "AbortError" ? "读取目录超时。" : error.message;
      try {
        applyManifest(readEmbeddedManifest());
        showToast("目录文件暂时不可读，已载入随包保存的界面目录。");
      } catch (embeddedError) {
        showStageMessage("界面目录未能载入", `${detail}\n随包目录也未能读取：${embeddedError.message}\n请确认 manifest.json 与 index.html 位于同一目录，也可以手动选择目录文件。`, { retry: loadManifest, allowManifest: true });
      }
    } finally { clearTimeout(timeout); }
  }

  function openZoom() {
    if (!state.imageReady) return;
    el.zoomImage.src = state.imageUrl;
    el.zoomImage.alt = el.image.alt;
    el.zoomTitle.textContent = `${numberLabel(state.index + 1)} / ${numberLabel(state.screens.length)}　${state.screens[state.index].title}`;
    if (!el.dialog.open) el.dialog.showModal();
    el.zoomClose.focus({ preventScroll: true });
  }

  function closeZoom() {
    if (el.dialog.open) el.dialog.close();
  }

  function syncFullscreen() {
    const full = Boolean(document.fullscreenElement);
    el.fullscreen.setAttribute("aria-pressed", String(full));
    el.fullscreen.setAttribute("aria-label", full ? "退出全屏" : "进入全屏");
    el.fullscreen.title = full ? "退出全屏" : "进入全屏";
    el.fullscreenLabel.textContent = full ? "退出全屏" : "全屏";
  }

  el.navigationToggle.addEventListener("click", () => setNavigation(el.viewer.dataset.navigation !== "open"));
  document.querySelector(".skip-link").addEventListener("click", (event) => {
    event.preventDefault();
    el.stage.focus({ preventScroll: true });
  });
  el.detailsToggle.addEventListener("click", () => setDetails(el.viewer.dataset.details !== "open"));
  el.detailsClose.addEventListener("click", () => setDetails(false, true));
  el.scrim.addEventListener("click", () => {
    if (el.viewer.dataset.details === "open") setDetails(false, true);
    if (el.viewer.dataset.navigation === "open") setNavigation(false, true);
  });
  el.previous.addEventListener("click", () => navigate(state.index - 1));
  el.next.addEventListener("click", () => navigate(state.index + 1));
  el.retry.addEventListener("click", () => { if (state.retry) state.retry(); });
  el.manifestButton.addEventListener("click", () => el.manifestInput.click());
  el.manifestInput.addEventListener("change", async () => {
    const file = el.manifestInput.files?.[0];
    if (!file) return;
    const sequence = ++state.manifestSequence;
    try {
      const raw = JSON.parse(await file.text());
      if (sequence === state.manifestSequence) applyManifest(raw);
    } catch (error) {
      showStageMessage("目录文件格式不正确", `${error.message}\n请选择这份示意集提供的 manifest.json。`, { allowManifest: true });
    } finally { el.manifestInput.value = ""; }
  });
  el.imageButton.addEventListener("click", openZoom);
  el.zoomClose.addEventListener("click", closeZoom);
  el.dialog.addEventListener("close", () => { if (!el.imageButton.hidden) el.imageButton.focus({ preventScroll: true }); });
  el.fullscreen.addEventListener("click", async () => {
    try {
      if (document.fullscreenElement) await document.exitFullscreen();
      else if (document.documentElement.requestFullscreen) await document.documentElement.requestFullscreen();
      else { showToast("此浏览器不支持页面全屏；点击图片仍可放大查看完整画面。"); return; }
    } catch { showToast("浏览器未允许进入全屏；点击图片仍可放大查看。"); }
  });
  document.addEventListener("fullscreenchange", syncFullscreen);
  window.addEventListener("hashchange", () => {
    if (!state.screens.length) return;
    const requestedId = readHash();
    const index = state.screens.findIndex((screen) => screen.id === requestedId);
    if (index >= 0) navigate(index, true);
    else if (!requestedId) navigate(0, true);
    else showToast(`未收录界面“${requestedId}”。`);
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      if (el.dialog.open) return;
      if (el.viewer.dataset.details === "open") { event.preventDefault(); setDetails(false, true); }
      else if (compact.matches && el.viewer.dataset.navigation === "open") { event.preventDefault(); setNavigation(false, true); }
      return;
    }
    if (el.dialog.open || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || !state.screens.length) return;
    const target = event.target;
    if (target instanceof HTMLElement && (target.isContentEditable || target.closest("input, textarea, select"))) return;
    if (event.code === "Space" && target instanceof HTMLElement && target.closest("button, a")) return;
    if (event.key === "ArrowRight" || event.code === "Space") { event.preventDefault(); navigate(state.index + 1); }
    if (event.key === "ArrowLeft") { event.preventDefault(); navigate(state.index - 1); }
  });
  compact.addEventListener("change", () => {
    setNavigation(!compact.matches);
    if (compact.matches) setDetails(false);
    syncScrim();
  });
  setNavigation(!compact.matches);
  syncFullscreen();
  loadManifest();
})();
