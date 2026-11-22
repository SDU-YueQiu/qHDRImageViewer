# qHdrImageViewer 1.2.1 — HDR JXL 修复、小图拖拽、工具栏与 VSR 细节

本版本修复上一版（1.2.0）用户反馈的四类问题，并重新打包/安装。产物仍为 **qHdrImageViewer 1.2.1.0**。

## 1. JPEG XL HDR 显示错误

原因有三处，均已修复：

1. **带 ICC 配置文件的 HDR JXL 被误判为“linear sRGB”**：探测逻辑只看 `JXL_COLOR_PROFILE_TARGET_DATA`，没有检查原始 ICC 配置文件里的 PQ/HLG 传递函数；解码时也没有像线性图那样固定 libjxl 的输出色彩编码，导致 PQ/HLG 数据被当作线性/SDR 处理（画面接近全黑）。现在探测会读取原始 ICC 并正确报告 `PQ / SMPTE ST 2084` 或 `HLG`、`Rec.2100 / BT.2020`；解码时对 XYB 图像显式请求 PQ/HLG+BT.2020 输出。
2. **`JXL_TYPE_FLOAT16` 常量写错（4 应为 5）**：所有 scene-linear JXL 的原生解码都会失败并回退到外部 `djxl`+临时 EXR，速度慢且色彩路径容易出错；现在原生 float16 解码正常，与 `djxl` 参考输出逐像素一致。
3. **`jhgm` gain map 的 `brob`（Brotli 压缩）盒与打包的备用色彩编码无法读取**：改为通过 libjxl 的盒 API 读取并解压，备用编码字段跳过而非报错；另修正 `djxl` 回退在 PQ/HLG 时的 `--color_space` 参数。

旧版本的元数据缓存会让同一文件继续复用错误的 “linear” 探测结果，因此缓存版本号已升级（旧缓存自动失效）。

**验证**：与 `djxl` 参考输出对比——结构化 PQ/HLG 与线性 float 最大差值 0；ICC-PQ 最大差值 0 u16、ICC-HLG ≤1 u16；渲染从修复前的 p95≈32 nits（接近全黑）变为 p95≈400 nits / max≈409 nits。安装包实测打开 `pq16-icc.jxl` 显示 `JPEG XL HDR · SingleLayerHdr`，HDR 信息为 `PQ / SMPTE ST 2084`、`Rec.2100 / BT.2020`，画面亮度正常。

## 2. 小图默认位置与拖拽

- **默认打开显示位置错误**：适应窗口模式不再把小图放大到超过 100 %。400×300 的图片现在以 **100%**、居中打开（上一版是 275% 铺满窗口），与 Windows 照片一致。
- **缩小到小于窗口后无法拖动**：现在任何尺寸/任何格式（PNG/JPEG、JXL、EXR/HDR）的图片在小于视口时都可以用鼠标拖动到任意方向，拖到边缘会被限制在预览区内、不会完全移出视野；同时修复了 HDR/EXR/JXL 的 D3D 交换链图层不跟随拖动的问题。
- 大图的适应窗口、滚轮缩放锚点、实际大小模式、窗口缩放/DPI 变化以及 VSR 显示时的取景保持不变；新增了视口数学单元测试。

## 3. 工具栏自动隐藏设置（0 = 不隐藏）

「悬浮工具栏自动隐藏」设置现在对顶部标题栏工具栏同样生效：

- 设为 **0**：顶部工具栏始终显示，不再自动隐藏（用户原设置即 0，验证通过）。
- 保持默认 **2200 ms**：浏览图片时默认隐藏，鼠标移到窗口顶部自动出现，离开后隐藏。

## 4. RTX VSR 徽章位置与反馈

- 徽章从标题栏最左侧移到**主命令区**，与「打开 / 裁切 / 导出 / 信息 / 分析」等工具同级（`RTX VSR · 2×`），仅在 VSR 激活时出现。
- 应用 VSR 时显示“处理中…”提示，完成后显示成功提示与对比方法：`RTX VSR 已启用：3200×2000，…ms` + “放大图片，或右键「显示原图」可对比增强前后的细节。”
- 右键「显示原图」状态下，徽章和状态栏会追加 **“显示原图”**，明确当前正在对比。
- VSR 功能本身实测有效：1600×1000 → 3200×2000（2×），EXR 1024×640 → 2048×1280，状态栏与徽章同步更新；关闭后恢复原图。注意原始分辨率的去噪/去模糊模式不会放大，这是 NVIDIA 文档规定的行为。

## 5. 安装

安装包位于 `artifacts\release\1.2.1.0\`（MSIX 108.1 MB，便携 zip 65.0 MB）：

```
qHdrImageViewer_1.2.1.0_x64.msix              安装包（自签名，包标识 LinYuSen.qHdrImageViewer）
qHdrImageViewer-signing.cer                   自签名证书
install.ps1                                   一键信任证书并安装（需要一次管理员授权）
INSTALL.md                                    说明
qHdrImageViewer-1.2.1.0-win-x64-portable.zip  免安装便携版（内置完整编解码器）
BUILD_INFO.txt                                版本/签名摘要
screenshots\                                  安装后实测截图
```

安装方式：右键 `install.ps1` →「使用 PowerShell 运行」（请求一次管理员权限信任证书后安装），或按 `INSTALL.md` 手动信任 `.cer` 后双击 MSIX。包标识未变，可直接覆盖 1.2.0.0 升级。

## 6. 本机验证

- 构建：app / WindowsTests / IntegrationTests 均 **0 警告 0 错误**；单元测试 **511 通过 / 1 跳过 / 0 失败**（新增 JXL HDR 色彩路径与视口数学测试）。
- 编解码器：`eng\verify-codecs.ps1` 68 个运行时文件校验通过。
- 安装包：`LinYuSen.qHdrImageViewer 1.2.1.0`，`Status=Ok`、`SignatureKind=Developer`，开始菜单/窗口标题 qHdrImageViewer；诊断页显示 `版本：1.2.1.0`。
- 安装后实测：
  - HDR JXL（ICC-PQ 512×256）：HDR 类型 `SingleLayerHdr`、曲线 `PQ / SMPTE ST 2084`、色彩 `Rec.2100 / BT.2020`，画面正常（`installed-1.2.1-01-jxl-hdr-pq.png`）。
  - 小图（400×300 PNG）：打开即 **100%** 居中，可滚动/拖动到左上/右下边缘并回中（`installed-1.2.1-02…05`）。
  - 顶部工具栏：默认隐藏、顶部悬停出现（`installed-1.2.1-06/07`）。
  - VSR：右键「RTX VSR 超分」→ 提示 + 状态 `RTX VSR 2×`，徽章 `RTX VSR · 2×` 位于其他工具同级（`installed-1.2.1-08`）。
- 验证结束后已把本机 `%LOCALAPPDATA%\qHdrImageViewer\settings.json` 恢复为用户原值；无 `crash.log`。
