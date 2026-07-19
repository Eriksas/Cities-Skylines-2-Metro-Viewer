# CS2 Metro Diagram

Turn your **Cities: Skylines II** metro network into a clean, readable metro map.

**English** · [中文](#cs2-地铁线路图)

Current version: `v0.1.0-beta.9` · Beta software — read-only preview and export; it does not change your city. Back up saves before testing any mod.

The project has three parts:

- a **CS2 code mod** with an in-game read-only preview and JSON/SVG export,
- a **Windows Viewer** that opens the JSON and renders the map,
- a **command-line renderer** for batch generation.

## Layout modes

- **`schematic-anneal`** (default) — an octilinear metro map laid out by global optimization. Lines run horizontal/vertical/45°, shared tracks render as parallel lines, and the map fills the canvas. Recommended for most maps.
- **`geographic`** — the most faithful render of the real route geometry; use it when you want true shapes rather than a schematic.
- `schematic-map` / `schematic-v2` — earlier schematic / diagnostic modes, kept for comparison.

## Quick start

1. Install the mod, launch CS2, and load a city.
2. Use the top-right metro-map button for the in-game preview. It opens in geographic mode and supports refresh, layout/label controls, zoom/pan, JSON export, and SVG save.
3. Open **Options › CS2 Metro Diagram › Main** to choose Auto / English / Simplified Chinese and an export folder (defaults to `Documents\CS2MetroDiagram`).
4. For the full product map, export JSON and open **MetroDiagram.Viewer.exe**, then click **Open Default Export** (or **Open JSON**).
5. The Viewer renders in `schematic-anneal`. Switch layout/size if you like, then **Save Map** as SVG, PNG, or PDF.

If the Viewer does not start on a clean PC, install the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).

Manual edit mode lets you drag stations, labels, and segments; edits are saved to a sidecar file next to the JSON and never touch the export.

## Command line

```powershell
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- `
  D:\CS2MetroDiagram\metro-export.json map.svg --layout schematic-anneal --size poster
```

Run with `--help` for all options (layouts, sizes, label filters, `--emit-layout-score`).

## Build

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\preflight.ps1
```

Restores the offline solution, builds it, and runs the tests (also run by CI). The CS2 mod project additionally needs the local CS2 modding toolchain — see [docs/DEV_NOTES.md](docs/DEV_NOTES.md).

## Known limitations

- Metro/subway only; export from a loaded city (no offline save parsing).
- Multi-city validation of `schematic-anneal` is ongoing.

## Feedback — share your city!

Does your city's map not look quite right? [Open a "Share your city" issue](https://github.com/Eriksas/Cities-Skylines-2-Metro-Viewer/issues/new?template=city-export.yml) and attach your `metro-export.json` (zipped). Submitted networks join the layout test corpus, so the next version is tuned against *your* city. Bugs go [here](https://github.com/Eriksas/Cities-Skylines-2-Metro-Viewer/issues/new?template=bug-report.yml).

More: [docs/](docs/README.md) · [project state](docs/PROJECT_STATE.md) · [JSON schema](docs/JSON_SCHEMA.md) · [changelog](docs/CHANGELOG.md) · [known issues](docs/KNOWN_ISSUES.md) · [license](LICENSE.txt)

---

# CS2 地铁线路图

把你的 **Cities: Skylines II** 地铁网络变成一张干净、易读的地铁线路图。

[English](#cs2-metro-diagram) · **中文**

当前版本：`v0.1.0-beta.9` · Beta 阶段软件 —— 只读预览和导出，不会改动你的城市。测试任何 mod 前请先备份存档。

项目分三部分：

- 一个带**游戏内只读预览**及 JSON/SVG 导出的 CS2 mod，
- 一个 **Windows 查看器**，打开 JSON 并渲染线路图，
- 一个**命令行渲染器**，用于批量生成。

## 布局模式

- **`schematic-anneal`**（默认）—— 通过全局优化排布的八方向线路图。线段横平竖直或走 45°，共享轨道渲染成平行线，且铺满画布。多数城市推荐使用。
- **`geographic`** —— 最忠实还原真实线路几何；想要真实形状而非示意图时用它。
- `schematic-map` / `schematic-v2` —— 早期的示意/诊断模式，保留用于对比。

## 快速上手

1. 从 Paradox Mods 安装 **CS2 Metro Diagram** mod，启动 CS2 并载入城市。GitHub Releases / 百度网盘提供的是 Windows Viewer（百度网盘：https://pan.baidu.com/s/5RspPYzFb0dPoOE0aZu7d4A）。
2. 点击右上角地铁图按钮打开游戏内预览。默认显示地理图，并支持刷新、布局/标签控制、缩放/拖动、导出 JSON 和保存 SVG。
3. 打开 **选项 › CS2 Metro Diagram › Main**，可跟随游戏语言或固定为 English / 简体中文，并设置导出文件夹（默认 `Documents\CS2MetroDiagram`）。
4. 若要生成完整产品级线路图，导出 JSON 后打开 **MetroDiagram.Viewer.exe**，点 **Open Default Export**（或 **Open JSON**）。
5. Viewer 以 `schematic-anneal` 渲染。可切换布局/尺寸，然后 **保存地图** 为 SVG、PNG 或 PDF。

若在干净的电脑上查看器无法启动，请安装 [Microsoft Edge WebView2 运行时](https://developer.microsoft.com/microsoft-edge/webview2/)。

手动编辑模式可拖动站点、标签和线段；编辑保存在 JSON 旁边的 sidecar 文件里，绝不改动导出数据。

## 命令行

```powershell
dotnet run --project src\MetroDiagram.Cli\MetroDiagram.Cli.csproj --no-restore -- `
  D:\CS2MetroDiagram\metro-export.json map.svg --layout schematic-anneal --size poster
```

加 `--help` 查看全部选项（布局、尺寸、标签过滤、`--emit-layout-score`）。

## 构建

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts\preflight.ps1
```

还原离线方案、构建并运行测试（CI 也跑同一脚本）。游戏 mod 项目另需本机 CS2 modding 工具链，见 [docs/DEV_NOTES.md](docs/DEV_NOTES.md)。

## 已知限制

- 仅支持地铁/subway；需在已载入的城市中导出（不解析离线存档）。
- `schematic-anneal` 的多城市验证仍在进行中。

## 反馈——投稿你的城市！

觉得你的城市图排得不够好？[发一个"城市导出投稿" issue](https://github.com/Eriksas/Cities-Skylines-2-Metro-Viewer/issues/new?template=city-export.yml)，附上打包成 ZIP 的 `metro-export.json`。投稿的城市会加入布局测试语料库——下个版本就是拿**你的城市**调优的。Bug 请走[这里](https://github.com/Eriksas/Cities-Skylines-2-Metro-Viewer/issues/new?template=bug-report.yml)。

更多：[docs/](docs/README.md) · [项目状态](docs/PROJECT_STATE.md) · [JSON 格式](docs/JSON_SCHEMA.md) · [更新日志](docs/CHANGELOG.md) · [已知问题](docs/KNOWN_ISSUES.md) · [许可证](LICENSE.txt)
