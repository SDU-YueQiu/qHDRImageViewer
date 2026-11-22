# qHdrImageViewer 1.2.0 — 设置修复、右键 RTX VSR、完整编解码器

本版本修复了设置页的两个实际使用问题，把 RTX VSR 改成右键菜单操作并重做了模式/档位设置，同时把完整编解码器运行时首次打包进安装包与便携版。产物名称仍为 **qHdrImageViewer**。

## 1. 设置页修复

### 1.1 下拉选项“每次打开都回默认值”

原因在显示层而不是保存层：所有「数值型下拉框」（缩放插值、滚轮行为、HDR 显示模式、色域映射、Headroom 来源、主题、VSR 倍率等）在恢复选中项时，代码把两个非字符串值都转成 `null` 再比较字符串，`null == null` 恒为真，于是永远选中第 1 项。实际设置值一直被正确保存，只是界面永远显示第一项。

- 修复后下拉框会正确显示已保存的值，关闭/重开应用后保持一致。
- 新增单元测试覆盖缩放插值 5 个选项、字符串选项、未找到值、类型混用等场景。

### 1.2 输入框无法退出

- 搜索框按 Enter：跳转到第一个搜索结果，并把焦点移到展开的设置卡片上。
- 搜索框按 Esc：清空并关闭搜索结果，焦点回到设置页内容。
- 搜索结果浮层打开时点击结果列表以外的区域：关闭浮层、清空搜索并归还焦点。
- 数字框/文本框按 Enter 或 Esc：提交当前值并退出输入框。
- 在设置页任意非输入区域点击：提交挂起的编辑并把焦点移出输入框。

## 2. RTX VSR：右键菜单 + 顶部工具栏默认隐藏

- RTX VSR 不再占用顶部工具栏按钮。图片右键菜单最上方新增两项：
  - **RTX VSR 超分**：对当前图片应用/关闭 NVIDIA AI 增强。
  - **显示原图**：仅在已激活 VSR 时可用，切换显示原图与增强结果（增强结果保留在内存，切回不需要重新加载模型，约 0.5 秒完成）。
- 顶部工具栏（窗口标题栏内的命令栏与文件名）在浏览图片时**默认隐藏**；鼠标移到窗口顶部边缘时自动滑出，离开后按「悬浮工具栏自动隐藏」设置的延迟再次隐藏（该设置为 0「从不隐藏」时，顶部工具栏仍按默认 2200 毫秒延迟隐藏，因为它是标题栏而不是悬浮控件）。截图按钮、最小化/最大化和窗口拖动区域保持可用；设置页/关于页始终显示标题栏。
- 只在 VSR 激活时，顶部工具栏显示高亮的只读徽章（如 `RTX VSR · 2×` 或 `RTX VSR · 去噪 极高`）；未激活时不显示。
- 缩放模型与 VSR 仍然完全解耦：开关 VSR、显示原图/增强都不会改变缩放模式、比例或滚动位置。

## 3. RTX VSR 设置：四个模式 × 四个档位

「高级」页的实验功能从原来的原始画质数字（1–23）改为符合 NVIDIA VFX SDK 1.3 文档的两级选择：

| 模式 | 档位 | 原生 QualityLevel | 行为 |
|---|---|---|---|
| 标准超分 | 低 / 中 / 高 / 极高 | 1 / 2 / 3 / 4 | AI 放大，按设置输出 2× 或 4× |
| 去噪 | 低 / 中 / 高 / 极高 | 8 / 9 / 10 / 11 | **原分辨率**处理，不放大 |
| 去模糊 | 低 / 中 / 高 / 极高 | 12 / 13 / 14 / 15 | **原分辨率**处理，不放大 |
| 高码率 | 低 / 中 / 高 / 极高 | 16 / 17 / 18 / 19 | 干净/高码率素材的细节还原，2×/4× |

- 默认 `高码率 + 极高`（= 原生 19，与旧版照片默认一致）。
- 「输出倍率 2×/4×」设置只在「标准超分」和「高码率」模式下可编辑；去噪/去模糊模式下自动置灰。
- 切换模式/档位会重新加载模型（约 3 秒），应用时会显示处理中状态；同一模式下重复使用会复用缓存。

## 4. 完整编解码器（首次随包分发）

发布包现在内置固定版本（`eng/codecs.lock.json`，SHA256 校验）的编解码运行时，无需安装 MSYS2 或任何外部工具：

- JPEG XL：`libjxl.dll` 进程内解码/探测 + `cjxl.exe` / `djxl.exe` / `jxlinfo.exe`。
- HEIF/HEIC：`heif-enc.exe` 与 libheif 运行时。
- AVIF：`avifenc.exe` / `avifdec.exe` 与 libavif/AOM/dav1d 运行时。
- Ultra HDR：`ultrahdr_app.exe` 与私有 `libheif-uhdr.dll`。
- OpenEXR：`HdrImageViewer.Native.dll` + OpenEXR 3.5.1 及其依赖（Imath、Iex、libdeflate 等）。
- 许可证文件随包放在 `encoders\x64\licenses`；版本与哈希可用 `eng\verify-codecs.ps1` 复核。

NVIDIA VSR 运行库因 NVIDIA 许可限制**不随包分发**，仍由程序在 `QHDR_VSR_DIR`、程序目录 `vsr\`、`%LOCALAPPDATA%\qHdrImageViewer\vsr`、NVIDIA VFX SDK/ComfyUI `nvvfx\libs` 等位置自动查找。

## 5. 安装

安装包位于 `artifacts\release\1.2.0.0\`（MSIX 108.1 MB，便携 zip 65.0 MB）：

```
qHdrImageViewer_1.2.0.0_x64.msix              安装包（自签名，包标识 LinYuSen.qHdrImageViewer）
qHdrImageViewer-signing.cer                   自签名证书
install.ps1                                   一键信任证书并安装（需要一次管理员授权）
INSTALL.md                                    说明
qHdrImageViewer-1.2.0.0-win-x64-portable.zip  免安装便携版
BUILD_INFO.txt                                版本/签名/编解码器摘要
screenshots\                                  本机验证截图
```

最简单的方式：右键 `install.ps1` →「使用 PowerShell 运行」。脚本会请求一次管理员权限，把证书放进「本地计算机 → 受信任人」存储，然后安装 MSIX；之后从开始菜单启动 **qHdrImageViewer**。也可以手动双击 `.cer` 按 `INSTALL.md` 把证书装到「本地计算机 → 受信任人」，再双击 MSIX。卸载：设置 → 应用 → qHdrImageViewer → 卸载，或 `Get-AppxPackage LinYuSen.qHdrImageViewer | Remove-AppxPackage`。

升级说明：包标识 `LinYuSen.qHdrImageViewer` 未变，可以直接覆盖升级到 1.2.0.0；设置文件仍在 `%LOCALAPPDATA%\qHdrImageViewer\settings.json`，旧版本的 `RtxVsrQualityLevel` 字段会被忽略并回落到新的默认 `高码率 + 极高`。

## 6. 本机验证

**构建与测试**（Windows 11 build 26300 / RTX 3060 Laptop / NVIDIA 驱动 621.14 / VFX SDK 1.3.0.0）：

- `dotnet build qHdrImageViewer.csproj -c Debug -p:Platform=x64` → 0 警告 0 错误；WindowsTests、IntegrationTests 同样 0/0。
- 单元测试：472 通过 / 3 跳过 / 0 失败（含新增的 SettingChoiceMatcher 与 RtxVsrMapping 测试）。
- `eng\verify-codecs.ps1` → 68 个运行时文件校验通过（cjxl 0.12.0、avifenc 1.4.2、heif-enc 1.23.5、ultrahdr_app v2.0.2）。
- 16 种「模式 × 档位」组合用真实 NVIDIA 运行库逐项验证：标准/高码率输出 2×，去噪/去模糊输出原分辨率；4×（两次 2×）正常。

**安装**

- 旧包 `LinYuSen.qHdrImageViewer 1.1.1.0` 已卸载，新包 `1.2.0.0` 安装成功：`Status=Ok`、`SignatureKind=Developer`，开始菜单/窗口标题为 qHdrImageViewer。
- 安装包内容抽查：398 个条目，`encoders/x64` 124 个文件（82.4 MB 未压缩）、`HdrImageViewer.Native.dll` 与 OpenEXR 运行时均在包内；清单 Identity `LinYuSen.qHdrImageViewer 1.2.0.0`、DisplayName `qHdrImageViewer`。

**安装后的应用行为（UIA 实测，截图见 `screenshots\`）**

- JPEG XL：状态栏 `JPEG XL · 1600 × 1000 · 78.6 KB`，解码路径显示 `libjxl in-process`（`installed-01-jxl-decode.png`）。
- OpenEXR：状态栏 `OpenEXR · 1024 × 640 · 7.5 MB · SingleLayerHdr`，解码路径显示 `HdrImageViewer.Native OpenEXR`（`installed-02-exr-decode.png`）。
- 顶部工具栏：`ViewerChromeAutoHideMilliseconds = 0`（用户原设置）时仍默认隐藏，鼠标移到顶部才显示（`installed-03-toolbar-hidden.png`）。
- 右键菜单：最上方为「RTX VSR 超分」「显示原图」+ 分隔线（`installed-04-context-menu.png`）。
- VSR：对 EXR 应用后 1024×640 → 2048×1280，状态栏含 `RTX VSR 2×（SDR）`，工具栏徽章高亮显示 `RTX VSR · 2×`（`installed-05-vsr-active.png`）；关闭后恢复 1024×640。本机 NVIDIA VSR 运行库由程序从 ComfyUI 的 `nvvfx\libs` 目录自动发现（安装包不包含这些 DLL）。
- 设置页：诊断显示 `版本：1.2.0.0`、设置文件路径正确；高级页 VSR 模式/档位显示 `高码率` / `极高`（第 4 项而非第一项，验证下拉显示修复）。
- 设置持久化：缩放插值原值 `最近邻` 正确显示 → 改为 `Lanczos` → 设置文件写入 `ZoomInterpolation: 4` → 关闭并重启应用后仍显示 `Lanczos（Lanczos-2）`（`installed-07-settings-zoom.png`、`installed-08-settings-persisted.png`）。
- 聚焦：搜索框按 Enter 后焦点移动到展开的设置控件；Esc / 点击浮层外部会关闭搜索并把焦点交还页面；数字框 Enter 提交并退出（详见 `artifacts\verify\settings-doctor\results.txt`）。
- 验证结束后已把本机 `%LOCALAPPDATA%\qHdrImageViewer\settings.json` 恢复为用户原值；无 `crash.log`。

