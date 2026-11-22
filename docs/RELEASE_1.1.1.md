# qHdrImageViewer 1.1.1 — 新功能与安装说明

本版本将产品与全部可见产物更名为 **qHdrImageViewer**（原工程名 HdrImageViewer 仅保留在内部 C# 命名空间、测试工程与原生 OpenEXR 桥 `HdrImageViewer.Native` 中，不出现在安装包、可执行文件、开始菜单或窗口标题里）。

除更名外，本版本包含：Windows 照片式缩放交互、ImageGlass 式导航设置页、缩放插值算法、可选的 NVIDIA RTX VSR 超分，以及可安装的 Release 安装包。

## 1. 缩放

- 右下角缩放百分比左侧的按钮是 **适合窗口 ↔ 100% 实际大小** 切换（与 Windows 照片一致）。按钮图标与提示随状态变化；100% 固定为 1 图像像素 = 1 物理像素，窗口缩放/DPI 改变不会漂移。
- 滚轮与 +/- 按钮每步 **×1.1（约 10%）**，与 Windows 照片实测一致；触控板捏合仍为系统原生的 1:1 手势。
- 「查看器」设置页新增 **缩放插值算法**：自动（默认，保持旧渲染）、最近邻、双线性、双三次、Lanczos。放大查看像素级细节时可切换对比；「自动」路径与升级前完全一致。

## 2. 设置页（ImageGlass 风格）

设置页为左侧导航 + 7 个分区 + 设置注册表驱动：

| 分区 | 内容 |
|---|---|
| 常规 | 启动恢复上次图片、默认应用 |
| 查看器 | 打开时缩放、缩放步进/动画/上限、滚轮映射与限位、状态栏字段、检查器与胶片栏、缩放插值 |
| HDR 与色彩 | 显示模式、色域映射、Headroom 来源/手动档位、SDR 白点、自适应色调映射、分析自动刷新 |
| 性能 | 预加载、解码缓存预算、元数据缓存、目录索引缓存、内存压力自动回收 |
| 工具与导出 | 覆盖确认、批处理重试 |
| 外观与语言 | 主题、语言（切换语言仍会提示重启） |
| 高级 | 配置文件位置、导入/导出设置、恢复默认、诊断信息、实验功能（RTX VSR 开关/画质/倍率） |

支持 **Ctrl+K 搜索并跳转**（结果带面包屑）、单项恢复默认、本页恢复默认、全部恢复默认、设置 JSON 导入/导出。设置与会话数据目录改为 `%LOCALAPPDATA%\qHdrImageViewer`；首次启动会自动迁移旧 `%LOCALAPPDATA%\HdrImageViewer\settings.json`。

## 3. RTX VSR（可选功能）

- 顶部工具栏的 **RTX VSR** 开关：对当前图片做 NVIDIA AI 超分（默认 2×，可选 4×），显示分辨率提升为 2 倍/4 倍的 SDR 画面，**与缩放完全解耦**（开关前后画面尺寸不变，缩放模式保留）。
- 首次使用需加载模型（约 3–4 秒），之后同一进程内换图直接处理（数十毫秒）；结果按图片/画质/倍率缓存，可随时关闭回到原图。
- 画质档位：默认 19 (HIGHBITRATE_ULTRA，适合干净照片)；可在「高级」页改为 4 (ULTRA，更锐利)、16–18 等。
- 需要：NVIDIA RTX 20 系及以上显卡 + 已安装 NVIDIA 驱动；NVIDIA VFX 运行库 DLL（`NVVideoEffects.dll`、`nvVFXVideoSuperRes.dll`、`nvngx_vsr.dll`、`nvngxruntime.dll`、`NVCVImage.dll`、`cudart64_12.dll`）。程序会按以下顺序自动查找，**不随安装包分发**（NVIDIA 许可限制）：
  1. 环境变量 `QHDR_VSR_DIR`
  2. 程序目录下 `vsr\`
  3. `%LOCALAPPDATA%\qHdrImageViewer\vsr`（兼容旧 `HdrImageViewer\vsr` 与 `LatentFolio\vsr` 缓存）
  4. NVIDIA Video Effects SDK 安装目录、常见 ComfyUI `nvvfx\libs` 目录
  找不到时按钮会给出提示而不会崩溃。
- 限制：VSR 通路是 8 位 SDR；对 HDR/gain-map 图片会先转成 SDR 再超分，状态栏会标注「（SDR）」。

## 4. 安装

安装包由 `eng\publish-release.ps1` 生成，输出在 `artifacts\release\1.1.1.0\`：

```
qHdrImageViewer_1.1.1.0_x64.msix              安装包（自签名，包标识 LinYuSen.qHdrImageViewer）
qHdrImageViewer-signing.cer                   自签名证书
install.ps1                                   一键信任证书并安装（需要一次管理员授权）
INSTALL.md                                    说明
qHdrImageViewer-1.1.1.0-win-x64-portable.zip  免安装便携版（qHdrImageViewer.exe）
```

最简单的安装方式：右键 `install.ps1` →「使用 PowerShell 运行」。脚本会请求一次管理员权限，把证书放进「本地计算机 → 受信任人」存储，然后安装 MSIX；之后从开始菜单启动 **qHdrImageViewer**。
也可以手动双击 `.cer` 按 `INSTALL.md` 安装到「本地计算机 → 受信任人」，再双击 MSIX。卸载：设置 → 应用 → qHdrImageViewer → 卸载，或 `Get-AppxPackage LinYuSen.qHdrImageViewer | Remove-AppxPackage`。

> 从 1.1.0 升级：包标识已从 `LinYuSen.HDRImageViewer` 改为 `LinYuSen.qHdrImageViewer`，新旧包可共存。建议先卸载旧包避免开始菜单出现两个入口；设置会自动迁移。

**本机验证结果**：新包已安装（`LinYuSen.qHdrImageViewer_1.1.1.0_x64`，`Status=Ok`），开始菜单显示 `qHdrImageViewer`，启动后窗口标题为 `qHdrImageViewer`，旧设置已从 `%LOCALAPPDATA%\HdrImageViewer\settings.json` 迁移到 `%LOCALAPPDATA%\qHdrImageViewer\settings.json`，无崩溃日志。窗口截图见 `artifacts\release\1.1.1.0\screenshots\installed-app-window-title.png`。

要求 Windows 11 24H2（26100）或更新版本；运行时依赖 .NET 10 Desktop Runtime 与 Windows App Runtime 2.5（缺失时 Windows 会提示从微软商店/官网安装）。

## 5. 命名范围说明

- 已更名：项目/解决方案文件（`qHdrImageViewer.csproj/.slnx`）、程序集与可执行文件（`qHdrImageViewer.exe/.dll/.pri`）、MSIX 包名与显示名、开始菜单项、窗口标题与关于页、安装包/证书/压缩包文件名、`%LOCALAPPDATA%\qHdrImageViewer` 数据目录、README/文档标题。
- 保留原名（内部实现，不影响产物）：C# 命名空间 `HdrImageViewer.*`、测试工程名称、原生 OpenEXR 桥 `HdrImageViewer.Native`、嵌入式着色器/资源逻辑名。如需要，可作为后续重构统一改名。

## 6. 已知限制

- 本机构建环境没有 `external\encoders` 与本地 `HdrImageViewer.Native.dll`，因此 JXL 原生解码、OpenEXR 等需要外部编解码器的格式不可用或回退；标准格式、HEIF/AVIF 与 RTX VSR 正常。需要完整编解码器时先运行 `eng\verify-codecs.ps1` / `eng\build-native.ps1` 再打包。
- RTX VSR 输出为 SDR；HDR 显示模式对 VSR 画面不生效。
- 切换语言需要重启应用（设置页会提示）。
