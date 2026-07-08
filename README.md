# CS2 Metro Diagram

Turn your **Cities: Skylines II** metro network into a clean, readable metro map.

**English** · [中文](#cs2-地铁线路图)

Current version: `v0.1.0-alpha.6` · Alpha software — export only, it does not change your city. Back up saves before testing any mod.

The project has three parts:

- a **CS2 code mod** that exports your metro/subway network to JSON,
- a **Windows Viewer** that opens the JSON and renders the map,
- a **command-line renderer** for batch generation.

## Layout modes

- **`schematic-anneal`** (default) — an octilinear metro map laid out by global optimization. Lines run horizontal/vertical/45°, shared tracks render as parallel lines, and the map fills the canvas. Recommended for most maps.
- **`geographic`** — the most faithful render of the real route geometry; use it when you want true shapes rather than a schematic.
- `schematic-map` / `schematic-v2` — earlier schematic / diagnostic modes, kept for comparison.

## Quick start

1. Install the mod, launch CS2, and load a city.
2. Open **Options › CS2 Metro Diagram › Main**. Optionally set an export folder (defaults to `Documents\CS2MetroDiagram`).
3. Click **Export Real Metro JSON**. This writes `metro-export.json` (plus diagnostics and timestamped snapshots) to the export folder.
4. Open **MetroDiagram.Viewer.exe** and click **Open Default Export** (or **Open JSON**).
5. The map renders in `schematic-anneal`. Switch layout/size if you like, then **Save SVG**.

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
- No in-game preview, and no PNG/PDF export yet.
- Multi-city validation of `schematic-anneal` is ongoing.

More: [docs/](docs/README.md) · [project state](docs/PROJECT_STATE.md) · [JSON schema](docs/JSON_SCHEMA.md) · [changelog](docs/CHANGELOG.md) · [known issues](docs/KNOWN_ISSUES.md) · [license](LICENSE.txt)

---

# CS2 地铁线路图

把你的 **Cities: Skylines II** 地铁网络变成一张干净、易读的地铁线路图。

[English](#cs2-metro-diagram) · **中文**

当前版本：`v0.1.0-alpha.6` · Alpha 阶段软件 —— 只导出数据，不会改动你的城市。测试任何 mod 前请先备份存档。

项目分三部分：

- 一个**游戏内 mod**，把地铁网络导出为 JSON，
- 一个 **Windows 查看器**，打开 JSON 并渲染线路图，
- 一个**命令行渲染器**，用于批量生成。

## 布局模式

- **`schematic-anneal`**（默认）—— 通过全局优化排布的八方向线路图。线段横平竖直或走 45°，共享轨道渲染成平行线，且铺满画布。多数城市推荐使用。
- **`geographic`** —— 最忠实还原真实线路几何；想要真实形状而非示意图时用它。
- `schematic-map` / `schematic-v2` —— 早期的示意/诊断模式，保留用于对比。

## 快速上手

1. 安装 mod（在此仅需要下载releases打包好的包解压即可），百度网盘链接https://pan.baidu.com/s/1uI0N-nFja45WqAwB4-vsug?pwd=475u，启动 CS2，载入城市。
2. 打开 **选项 › CS2 Metro Diagram › Main**。可选设置导出文件夹（默认 `Documents\CS2MetroDiagram`）。
3. 点击 **Export Real Metro JSON**，会把 `metro-export.json`（及诊断文件、带时间戳的快照）写入导出文件夹。
4. 打开 **MetroDiagram.Viewer.exe**，点 **Open Default Export**（或 **Open JSON**）。
5. 线路图以 `schematic-anneal` 渲染。可切换布局/尺寸，然后 **Save SVG** 保存。

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
- 暂无游戏内预览，暂不支持 PNG/PDF 导出。
- `schematic-anneal` 的多城市验证仍在进行中。

更多：[docs/](docs/README.md) · [项目状态](docs/PROJECT_STATE.md) · [JSON 格式](docs/JSON_SCHEMA.md) · [更新日志](docs/CHANGELOG.md) · [已知问题](docs/KNOWN_ISSUES.md) · [许可证](LICENSE.txt)
