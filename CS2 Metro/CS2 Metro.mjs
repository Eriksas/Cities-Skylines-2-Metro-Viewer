const React = window.React;
const api = window["cs2/api"];
const ui = window["cs2/ui"];

const GROUP = "CS2MetroPreview";
const panelOpenBinding = api.bindValue(GROUP, "panelOpen");
const stateBinding = api.bindValue(GROUP, "stateJson");
const svgBinding = api.bindValue(GROUP, "svg");
const localeBinding = api.bindValue("app", "activeLocale");
const h = React.createElement;

function isChinese(locale) {
  return typeof locale === "string" && locale.toLowerCase().startsWith("zh");
}

function copy(locale) {
  return isChinese(locale)
    ? {
        title: "CS2 地铁线路图",
        subtitle: "当前城市的地铁图预览",
        open: "打开地铁图预览",
        description: "预览并导出当前城市的地铁线路图",
        refresh: "刷新城市数据",
        close: "关闭",
        schematic: "示意图",
        geographic: "地理图",
        generic: "显示默认站名",
        crowded: "隐藏拥挤标签",
        exportJson: "导出 JSON",
        saveSvg: "保存当前 SVG",
        fit: "适应窗口",
        zoomIn: "放大",
        zoomOut: "缩小",
        idle: "打开一座城市后刷新预览",
        loading: "正在读取当前城市的地铁数据…",
        rendering: "正在生成地铁图…",
        exporting: "正在导出 JSON…",
        saving: "正在保存 SVG…",
        noCity: "当前没有可读取的城市。请先加载城市。",
        noMetro: "当前城市没有可导出的地铁线路。",
        previewPayloadMissing: "地图已生成，但游戏界面没有收到 SVG 内容。请关闭面板后重新打开；若仍出现，请附上游戏日志。",
        error: "预览失败",
        copyError: "复制错误详情",
        lines: "线路",
        stations: "站点",
        rendered: "渲染",
        cached: "已使用缓存预览",
        saved: "文件已保存",
        exported: "JSON 已导出",
        hint: "滚轮缩放，按住并拖动地图平移",
        schematicViewerHint: "示意图预览仍在优化；需要完整效果时建议使用桌面 Viewer。",
        path: "输出",
      }
    : {
        title: "CS2 Metro Diagram",
        subtitle: "Preview the current city's metro network",
        open: "Open metro map preview",
        description: "Preview and export the current city's metro diagram",
        refresh: "Refresh city data",
        close: "Close",
        schematic: "Schematic",
        geographic: "Geographic",
        generic: "Show default station names",
        crowded: "Hide crowded labels",
        exportJson: "Export JSON",
        saveSvg: "Save current SVG",
        fit: "Fit",
        zoomIn: "Zoom in",
        zoomOut: "Zoom out",
        idle: "Load a city, then refresh the preview",
        loading: "Reading metro data from the current city…",
        rendering: "Rendering the metro diagram…",
        exporting: "Exporting JSON…",
        saving: "Saving SVG…",
        noCity: "No readable city is loaded. Load a city first.",
        noMetro: "The current city has no metro lines to preview.",
        previewPayloadMissing: "The map was rendered, but the UI did not receive the SVG payload. Close and reopen the panel; attach the game log if it persists.",
        error: "Preview failed",
        copyError: "Copy error details",
        lines: "lines",
        stations: "stations",
        rendered: "rendered",
        cached: "Cached preview restored",
        saved: "File saved",
        exported: "JSON exported",
        hint: "Use the wheel to zoom; drag the map to pan",
        schematicViewerHint: "Schematic preview is still being refined; use the desktop Viewer for the most reliable result.",
        path: "Output",
      };
}

function parseState(value) {
  if (!value) return { status: "idle", layout: "geographic", lineCount: 0, stationCount: 0, interfaceLanguage: "auto" };
  try {
    return JSON.parse(value);
  } catch (error) {
    console.warn("CS2 Metro Diagram state payload could not be parsed", error);
    return { status: "error", notice: "invalid-state", error: String(error), layout: "geographic", interfaceLanguage: "auto" };
  }
}

function controlButton(label, onClick, disabled, selected, title, extraStyle) {
  return h(
    ui.Button,
    {
      variant: selected === true ? "primary" : "flat",
      onClick,
      disabled: disabled === true,
      selected: selected === true,
      title: title || label,
      "aria-pressed": selected === true,
      style: Object.assign(
        {
          minWidth: "76rem",
          minHeight: "32rem",
          marginLeft: "8rem",
          paddingLeft: "12rem",
          paddingRight: "12rem",
          borderRadius: "4rem",
          color: disabled ? "#7d929f" : "#eef7fb",
          opacity: disabled ? 0.55 : 1,
        },
        extraStyle || {},
      ),
    },
    label,
  );
}

function StatusOverlay({ state, text }) {
  let message = "";
  if (state.status === "idle") message = text.idle;
  else if (state.status === "loading") message = text.loading;
  else if (state.status === "rendering") message = text.rendering;
  else if (state.status === "exporting") message = text.exporting;
  else if (state.status === "saving") message = text.saving;
  else if (state.status === "no-city") message = text.noCity;
  else if (state.status === "no-metro") message = text.noMetro;
  else if (state.status === "error") message = `${text.error}: ${state.error || state.notice || "Unknown error"}`;
  else if (state.status === "ready" && state.hasSvg && state.previewPayloadMissing) message = text.previewPayloadMissing;
  if (!message) return null;

  return h(
    "div",
    {
      style: {
        position: "absolute",
        inset: 0,
        zIndex: 5,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        flexDirection: "column",
        padding: "30rem",
        color: state.status === "error" ? "#ffd5d1" : "#dcecf4",
        background: "rgba(13, 28, 40, 0.80)",
        fontSize: "17rem",
        fontWeight: 600,
        textAlign: "center",
        userSelect: "text",
      },
    },
    h("div", null, message),
    state.status === "error"
      ? h(ui.Button, {
          onClick: () => {
            const details = `${state.notice || "preview-error"}: ${state.error || "Unknown error"}`;
            if (navigator.clipboard && navigator.clipboard.writeText) {
              navigator.clipboard.writeText(details).catch(() => console.error(details));
            } else {
              console.error(details);
            }
          },
          style: { marginTop: "14rem", minWidth: "140rem" },
        }, text.copyError)
      : null,
  );
}

export function prepareInlineSvg(svg) {
  if (typeof svg !== "string" || !svg.includes("<svg")) return "";

  const withoutDocumentHeaders = svg
    .replace(/<\?xml[\s\S]*?\?>/gi, "")
    .replace(/<!doctype[\s\S]*?>/gi, "")
    .replace(/\sfont-family=("[^"]*"|'[^']*')/gi, "")
    .trim();

  return withoutDocumentHeaders.replace(/<svg\b([^>]*)>/i, (_match, attributes) => {
    const responsiveAttributes = attributes
      .replace(/\swidth=("[^"]*"|'[^']*')/i, "")
      .replace(/\sheight=("[^"]*"|'[^']*')/i, "")
      .replace(/\spreserveAspectRatio=("[^"]*"|'[^']*')/i, "")
      .replace(/\soverflow=("[^"]*"|'[^']*')/i, "")
      .replace(/\stext-rendering=("[^"]*"|'[^']*')/i, "")
      .replace(/\sstyle=("[^"]*"|'[^']*')/i, "");
    return `<svg${responsiveAttributes} width="100%" height="100%" preserveAspectRatio="xMidYMid meet" overflow="hidden" text-rendering="geometricPrecision" style="display:block;width:100%;height:100%;font-family:var(--fontFamily)">`;
  });
}

function parseSvgViewBox(svg) {
  if (typeof svg !== "string") return null;
  const match = svg.match(/\bviewBox\s*=\s*["']\s*(-?[\d.]+)[ ,]+(-?[\d.]+)[ ,]+([\d.]+)[ ,]+([\d.]+)\s*["']/i);
  if (!match) return null;
  const values = match.slice(1).map(Number);
  if (values.some((value) => !Number.isFinite(value)) || values[2] <= 0 || values[3] <= 0) return null;
  return { x: values[0], y: values[1], width: values[2], height: values[3] };
}

function clamp(value, minimum, maximum) {
  return Math.max(minimum, Math.min(maximum, value));
}

// Keeps the SVG point under the cursor fixed while the scale changes.
// The sheet is meet-fit inside the host, so its on-screen box is constant
// across zoom levels; only the visible viewBox window moves. Exported as a
// pure function so the math is testable outside the game runtime.
export function computeCursorAnchoredPan(baseViewBox, currentScale, nextScale, currentPan, hostBounds, clientX, clientY) {
  if (!baseViewBox || !hostBounds || !(currentScale > 0) || !(nextScale > 0)) return currentPan;
  const k = Math.min(hostBounds.width / baseViewBox.width, hostBounds.height / baseViewBox.height);
  if (!(k > 0)) return currentPan;
  const contentLeft = hostBounds.left + ((hostBounds.width - (baseViewBox.width * k)) / 2);
  const contentTop = hostBounds.top + ((hostBounds.height - (baseViewBox.height * k)) / 2);
  const currentWidth = baseViewBox.width / currentScale;
  const currentHeight = baseViewBox.height / currentScale;
  const maxPanX = Math.max(0, (baseViewBox.width - currentWidth) / 2);
  const maxPanY = Math.max(0, (baseViewBox.height - currentHeight) / 2);
  const viewX = baseViewBox.x + ((baseViewBox.width - currentWidth) / 2) + clamp(currentPan.x, -maxPanX, maxPanX);
  const viewY = baseViewBox.y + ((baseViewBox.height - currentHeight) / 2) + clamp(currentPan.y, -maxPanY, maxPanY);
  const unitX = viewX + ((clientX - contentLeft) / (k * currentScale));
  const unitY = viewY + ((clientY - contentTop) / (k * currentScale));
  const nextWidth = baseViewBox.width / nextScale;
  const nextHeight = baseViewBox.height / nextScale;
  return {
    x: unitX - ((clientX - contentLeft) / (k * nextScale)) - baseViewBox.x - ((baseViewBox.width - nextWidth) / 2),
    y: unitY - ((clientY - contentTop) / (k * nextScale)) - baseViewBox.y - ((baseViewBox.height - nextHeight) / 2),
  };
}

function MapViewport({ svg, state, text }) {
  const [scale, setScale] = React.useState(1);
  const [pan, setPan] = React.useState({ x: 0, y: 0 });
  const [dragging, setDragging] = React.useState(false);
  const drag = React.useRef(null);
  const svgHost = React.useRef(null);
  // CS2's embedded browser can silently reject long data:image/svg+xml URLs.
  // The SVG is generated by our own renderer and XML-escapes all city text, so
  // render it inline instead of routing it through a fragile data URI.
  const inlineSvg = React.useMemo(() => prepareInlineSvg(svg), [svg]);
  const baseViewBox = React.useMemo(() => parseSvgViewBox(svg), [svg]);
  const displayState = React.useMemo(
    () => ({ ...state, previewPayloadMissing: state.status === "ready" && state.hasSvg && !inlineSvg }),
    [state, inlineSvg],
  );

  const fit = React.useCallback(() => {
    setScale(1);
    setPan({ x: 0, y: 0 });
  }, []);

  React.useEffect(() => fit(), [svg, state.layout, fit]);

  const zoom = React.useCallback((factor) => {
    setScale((value) => Math.max(0.65, Math.min(4, value * factor)));
  }, []);

  const zoomAt = React.useCallback((factor, clientX, clientY) => {
    const next = Math.max(0.65, Math.min(4, scale * factor));
    if (next === scale) return;
    if (baseViewBox && svgHost.current && typeof clientX === "number" && typeof clientY === "number") {
      setPan(computeCursorAnchoredPan(baseViewBox, scale, next, pan, svgHost.current.getBoundingClientRect(), clientX, clientY));
    }
    setScale(next);
  }, [scale, pan, baseViewBox]);

  const visibleViewBox = React.useMemo(() => {
    if (!baseViewBox) return null;
    const width = baseViewBox.width / scale;
    const height = baseViewBox.height / scale;
    const maxPanX = Math.max(0, (baseViewBox.width - width) / 2);
    const maxPanY = Math.max(0, (baseViewBox.height - height) / 2);
    return {
      x: baseViewBox.x + ((baseViewBox.width - width) / 2) + clamp(pan.x, -maxPanX, maxPanX),
      y: baseViewBox.y + ((baseViewBox.height - height) / 2) + clamp(pan.y, -maxPanY, maxPanY),
      width,
      height,
    };
  }, [baseViewBox, scale, pan]);

  React.useEffect(() => {
    if (!svgHost.current || !visibleViewBox) return;
    const svgElement = svgHost.current.querySelector("svg");
    if (!svgElement) return;
    svgElement.setAttribute("viewBox", `${visibleViewBox.x} ${visibleViewBox.y} ${visibleViewBox.width} ${visibleViewBox.height}`);
  }, [inlineSvg, visibleViewBox]);

  const onWheel = (event) => {
    event.preventDefault();
    zoomAt(event.deltaY < 0 ? 1.12 : 0.89, event.clientX, event.clientY);
  };

  const moveDrag = React.useCallback((clientX, clientY) => {
    if (!drag.current || !svgHost.current || !baseViewBox) return;
    const bounds = svgHost.current.getBoundingClientRect();
    const visibleWidth = baseViewBox.width / drag.current.scale;
    const visibleHeight = baseViewBox.height / drag.current.scale;
    const pixelsPerUnit = Math.max(0.0001, Math.min(bounds.width / visibleWidth, bounds.height / visibleHeight));
    setPan({
      x: drag.current.originX - ((clientX - drag.current.x) / pixelsPerUnit),
      y: drag.current.originY - ((clientY - drag.current.y) / pixelsPerUnit),
    });
  }, [baseViewBox]);

  const endDrag = React.useCallback(() => {
    drag.current = null;
    setDragging(false);
  }, []);

  React.useEffect(() => {
    if (!dragging) return undefined;

    const onMouseMove = (event) => {
      event.preventDefault();
      moveDrag(event.clientX, event.clientY);
    };
    const onMouseUp = () => endDrag();
    window.addEventListener("mousemove", onMouseMove);
    window.addEventListener("mouseup", onMouseUp);
    return () => {
      window.removeEventListener("mousemove", onMouseMove);
      window.removeEventListener("mouseup", onMouseUp);
    };
  }, [dragging, moveDrag, endDrag]);

  const onMouseDown = (event) => {
    if (!svg || event.button !== 0) return;
    event.preventDefault();
    drag.current = { x: event.clientX, y: event.clientY, originX: pan.x, originY: pan.y, scale };
    setDragging(true);
  };

  return h(
    "div",
      {
        onWheel,
        onMouseDown,
        onDragStart: (event) => event.preventDefault(),
      style: {
        position: "relative",
        flex: "1 1 auto",
        minWidth: 0,
        minHeight: 0,
        overflow: "hidden",
          cursor: dragging ? "grabbing" : svg ? "grab" : "default",
        background: "#eef2f4",
        borderRadius: "5rem",
        boxShadow: "inset 0 0 0 1rem rgba(23, 44, 57, 0.18)",
        touchAction: "none",
      },
    },
    inlineSvg
      ? h("div", {
          ref: svgHost,
          role: "img",
          "aria-label": text.title,
          dangerouslySetInnerHTML: { __html: inlineSvg },
          style: {
            position: "absolute",
            top: "14rem",
            right: "14rem",
            bottom: "14rem",
            left: "14rem",
            overflow: "hidden",
            objectFit: "contain",
            userSelect: "none",
            pointerEvents: "none",
            fontFamily: "var(--fontFamily)",
          },
        })
      : null,
    h(StatusOverlay, { state: displayState, text }),
    h(
      "div",
      {
        style: {
          position: "absolute",
          right: "14rem",
          bottom: "12rem",
          zIndex: 7,
          display: "flex",
          alignItems: "center",
          padding: "6rem 8rem",
          borderRadius: "4rem",
          color: "#dcecf4",
          background: "rgba(12, 27, 40, 0.78)",
          fontSize: "11rem",
        },
      },
      h("span", { style: { marginRight: "6rem" } }, `${Math.round(scale * 100)}%`),
      controlButton("−", () => zoom(0.85), !svg, false, text.zoomOut, { minWidth: "32rem", marginLeft: "6rem", paddingLeft: "8rem", paddingRight: "8rem" }),
      controlButton("+", () => zoom(1.18), !svg, false, text.zoomIn, { minWidth: "32rem", marginLeft: "6rem", paddingLeft: "8rem", paddingRight: "8rem" }),
      controlButton(text.fit, fit, !svg, false, text.fit, { minWidth: "48rem", marginLeft: "6rem" }),
    ),
    svg ? h(
      "div",
      {
        style: {
          position: "absolute",
          left: "14rem",
          bottom: "12rem",
          zIndex: 7,
          maxWidth: "70%",
          color: "#526675",
          fontSize: "10rem",
        },
      },
      h("span", null, text.hint),
      state.layout !== "geographic"
        ? h("span", { style: { marginLeft: "10rem", color: "#8195a2", opacity: 0.82 } }, `· ${text.schematicViewerHint}`)
        : null,
    ) : null,
  );
}

function Toggle({ checked, onChange, label, disabled }) {
  return h(
    ui.Button,
    {
      variant: checked === true ? "primary" : "flat",
      selected: checked === true,
      disabled: disabled === true,
      onClick: () => onChange(checked !== true),
      title: label,
      "aria-pressed": checked === true,
      style: {
        display: "flex",
        alignItems: "center",
        minHeight: "30rem",
        marginLeft: "14rem",
        paddingLeft: "8rem",
        paddingRight: "10rem",
        borderRadius: "4rem",
        color: disabled ? "#6f8796" : "#cce0eb",
        fontSize: "12rem",
        whiteSpace: "nowrap",
        opacity: disabled ? 0.55 : 1,
      },
    },
    h(
      "span",
      {
        "aria-hidden": "true",
        style: {
          position: "relative",
          display: "inline-block",
          flex: "0 0 auto",
          width: "30rem",
          height: "16rem",
          marginRight: "8rem",
          border: checked ? "1rem solid rgba(116, 226, 255, 0.82)" : "1rem solid rgba(174, 205, 220, 0.48)",
          borderRadius: "8rem",
          background: checked ? "rgba(29, 170, 218, 0.88)" : "rgba(4, 17, 28, 0.72)",
          boxShadow: checked ? "0 0 8rem rgba(48, 191, 235, 0.32)" : "none",
        },
      },
      h("span", {
        style: {
          position: "absolute",
          top: "2rem",
          left: checked ? "16rem" : "2rem",
          width: "10rem",
          height: "10rem",
          borderRadius: "50%",
          background: checked ? "#f3fbff" : "#9bb1bd",
          boxShadow: "0 1rem 3rem rgba(0, 0, 0, 0.42)",
        },
      }),
    ),
    h("span", null, label),
  );
}

function PreviewPanel() {
  const locale = api.useValue(localeBinding) || "en-US";
  const state = parseState(api.useValue(stateBinding));
  const effectiveLocale = state.interfaceLanguage === "en"
    ? "en-US"
    : state.interfaceLanguage === "zh-HANS"
      ? "zh-HANS"
      : locale;
  const text = copy(effectiveLocale);
  const svg = api.useValue(svgBinding) || "";
  const busy = state.busy === true;

  const notice = state.notice === "cached-preview"
    ? text.cached
    : state.notice === "svg-saved"
      ? text.saved
      : state.notice === "json-exported"
        ? text.exported
        : state.status === "ready"
          ? `${text.rendered} ${state.renderMs || 0} ms`
          : "";

  return h(
    "div",
    {
      style: {
        position: "absolute",
        top: "54rem",
        right: "48rem",
        bottom: "78rem",
        left: "48rem",
        zIndex: 1100,
        display: "flex",
        flexDirection: "column",
        minWidth: 0,
        minHeight: 0,
        overflow: "hidden",
        pointerEvents: "auto",
        color: "#eef7fb",
        background: "rgba(12, 27, 40, 0.94)",
        border: "1rem solid rgba(180, 220, 239, 0.24)",
        borderRadius: "8rem",
        boxShadow: "0 18rem 60rem rgba(0, 0, 0, 0.38)",
        backdropFilter: "blur(18px) saturate(125%)",
      },
    },
    h(
      "div",
      {
        style: {
          flex: "0 0 auto",
          minHeight: "54rem",
          display: "flex",
          alignItems: "center",
          padding: "0 18rem",
          borderBottom: "1rem solid rgba(180, 220, 239, 0.16)",
        },
      },
      h("div", { style: { flex: "1 1 auto", minWidth: 0 } },
        h("div", { style: { fontSize: "19rem", lineHeight: "24rem", fontWeight: 700 } }, text.title),
        h("div", { style: { marginTop: "2rem", color: "#9fb6c5", fontSize: "11rem" } },
          state.cityName
            ? `${state.cityName} · ${state.lineCount || 0} ${text.lines} · ${state.stationCount || 0} ${text.stations}`
            : text.subtitle,
        ),
      ),
      controlButton(text.close, () => api.trigger(GROUP, "setPanelOpen", false), false, false, text.close),
    ),
    h(
      "div",
      {
        style: {
          flex: "0 0 auto",
          minHeight: "42rem",
          display: "flex",
          alignItems: "center",
          flexWrap: "wrap",
          padding: "5rem 18rem",
          background: "rgba(18, 37, 51, 0.86)",
          borderBottom: "1rem solid rgba(180, 220, 239, 0.12)",
        },
      },
      h("div", { style: { display: "flex", alignItems: "center", padding: "3rem", borderRadius: "5rem", background: "rgba(4, 17, 28, 0.42)" } },
        controlButton(text.geographic, () => api.trigger(GROUP, "setLayout", "geographic"), busy, state.layout === "geographic", text.geographic),
        controlButton(text.schematic, () => api.trigger(GROUP, "setLayout", "schematic"), busy, state.layout !== "geographic", text.schematic),
      ),
      h("div", { style: { display: "flex", alignItems: "center", marginLeft: "auto" } },
        controlButton(text.refresh, () => api.trigger(GROUP, "refresh"), busy, false, text.refresh),
        controlButton(text.exportJson, () => api.trigger(GROUP, "exportJson"), busy, false, text.exportJson),
        controlButton(text.saveSvg, () => api.trigger(GROUP, "saveSvg"), busy || !state.hasSvg, false, text.saveSvg),
      ),
    ),
    h(
      "div",
      {
        style: {
          flex: "0 0 auto",
          minHeight: "38rem",
          display: "flex",
          alignItems: "center",
          padding: "0 18rem",
          background: "rgba(22, 43, 57, 0.70)",
          borderBottom: "1rem solid rgba(180, 220, 239, 0.12)",
        },
      },
      h(Toggle, {
        checked: state.showGenericStationNames,
        disabled: busy,
        label: text.generic,
        onChange: (value) => api.trigger(GROUP, "setShowGenericStationNames", value),
      }),
      h(Toggle, {
        checked: state.hideCrowdedLabels,
        disabled: busy,
        label: text.crowded,
        onChange: (value) => api.trigger(GROUP, "setHideCrowdedLabels", value),
      }),
      h("div", { style: { marginLeft: "auto", color: "#9fb6c5", fontSize: "11rem" } }, notice),
    ),
    h("div", { style: { flex: "1 1 auto", minHeight: 0, display: "flex", padding: "14rem" } },
      h(MapViewport, { svg, state, text }),
    ),
    h(
      "div",
      {
        style: {
          flex: "0 0 auto",
          minHeight: "28rem",
          display: "flex",
          alignItems: "center",
          padding: "0 18rem 8rem",
          color: "#8fa8b7",
          fontSize: "10rem",
        },
      },
      h("span", null, state.updatedAt || "--:--:--"),
      state.lastPath ? h("span", { title: state.lastPath, style: { marginLeft: "14rem", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" } }, `${text.path}: ${state.lastPath}`) : null,
    ),
  );
}

function PreviewRoot() {
  return api.useValue(panelOpenBinding) === true ? h(PreviewPanel) : null;
}

function TopButton() {
  const locale = api.useValue(localeBinding) || "en-US";
  const text = copy(locale);
  const selected = api.useValue(panelOpenBinding) === true;
  const icon = h(
    "svg",
    { viewBox: "0 0 48 48", style: { width: "32rem", height: "32rem", display: "block" }, "aria-hidden": "true" },
    h("path", { d: "M7 34 L17 18 L25 28 L34 11 L41 20", fill: "none", stroke: "white", strokeWidth: 4, strokeLinecap: "round", strokeLinejoin: "round" }),
    h("circle", { cx: 17, cy: 18, r: 3, fill: "white" }),
    h("circle", { cx: 25, cy: 28, r: 3, fill: "white" }),
    h("circle", { cx: 34, cy: 11, r: 3, fill: "white" }),
    h("path", { d: "M7 39 H41", stroke: "white", strokeWidth: 3, strokeLinecap: "round" }),
  );

  return h(
    ui.Tooltip,
    { tooltip: h("div", null, h("div", { style: { fontWeight: 700 } }, text.open), h("div", { style: { marginTop: "4rem" } }, text.description)) },
    h(ui.Button, {
      variant: "floating",
      selected,
      onClick: () => api.trigger(GROUP, "setPanelOpen", !selected),
      style: { display: "flex", alignItems: "center", justifyContent: "center" },
    }, icon),
  );
}

export default function registerPreview(context) {
  context.append("GameTopRight", TopButton);
  context.append("Game", PreviewRoot);
}

export const hasCSS = false;
