# qHdrImageViewer 1.2.3 — 缩小插值的专用算法

本版本针对“缩小（<100%）插值应该用不同于放大的算法”这一需求，新增了一组**收缩到小尺寸时专用**的滤波算法，并根据论文/ImageMagick 等实现的公式重新设计了下采样路径。产物为 **qHdrImageViewer 1.2.3.0**。

## 为什么缩小需要单独算法

放大是在已知采样点之间“重建”连续信号，目标是细节和锐度；缩小则是一个输出像素覆盖多个源像素，必须**先预滤波（prefilter）再采样**——否则超过输出 Nyquist 频率的成分会折叠成摩尔纹、锯齿、闪烁。所以缩小的滤波器支撑宽度、振铃容忍度、能量守恒要求都和放大不同，不能直接复用“最近邻/双线性/双三次/Lanczos”这一套放大算法。详细研究见 [docs/DOWNSCALING_FILTERS.md](DOWNSCALING_FILTERS.md)。

## 新增的缩小算法（设置页「缩小插值（<100%）」）

| 选项 | 算法 | 特点 / 适用 |
|---|---|---|
| 自动 | 旧的 mip 三线性路径 | 保持默认渲染不变 |
| 面积平均（Box） | 输出像素覆盖区域的盒式平均 | 能量守恒、无负瓣、几乎无振铃/摩尔纹；适合棋盘格、噪点、织物、文字 |
| 双线性 | 硬件双线性 + mip | 快速、柔和，轻微混叠 |
| Catmull-Rom | Keys 三次（B=0, C=0.5） | 最锐利的三次，有负瓣 |
| Mitchell | Mitchell-Netravali（B=C=1/3） | 平衡，振铃明显低于 Catmull-Rom |
| Robidoux | Keys 三次（B≈0.3782, C≈0.3109，缩图专用参数） | 比 Mitchell 更锐利、比 Catmull-Rom 更平静，适合照片/壁纸缩小 |
| Lanczos-2 | sinc(x)·sinc(x/2) | 经典高质量缩小，边缘锐利、有负瓣 |
| Kaiser-2 | sinc × Kaiser 窗（β=3.5） | 比 Lanczos-2 保留更多通带细节，负瓣略强 |
| 最近邻 | 点采样 + mip | 像素风/调试用 |

实现要点：

- 像素着色器按采样率判断放大/缩小；缩小时从**经过预滤波的 mip 层级**取 4×4 taps（LOD 按输出像素足迹计算，而不是从 mip0 取点），因此真正起到抗混叠作用；负瓣核再套用 2×2 邻域 clamp 抑制振铃。
- 纹理上传为线性光（HDR float / sRGB 硬件线性过滤），mip 链在线性光中生成，因此缩小平均不会出现 sRGB 空间平均导致的变暗。
- 两档插值仍是独立的：放大档保持原来的 5 个选项，不受缩小档影响。

## 迁移

- 1.2.2 的文件里有 `"ZoomInterpolationDownscale": 0..4`（旧枚举）：按名称映射到新算法（Bicubic→Catmull-Rom，Lanczos→Lanczos-2 等）。
- 1.2.1 及更早的单个 `"ZoomInterpolation": N`：同时迁移到放大档与缩小档，旧键在保存后消失。
- 升级后默认两档都是「自动」，渲染与 1.2.2 逐像素一致（已用旧版 portable 在 50%/25% 下做像素比对）。

## 本机验证

- 构建/测试：app/WindowsTests/IntegrationTests 0 警告 0 错误；单元测试 **598 通过 / 1 跳过**（新增 80 项：枚举/迁移/核函数权重/着色器常量）。
- 着色器：`eng\CompileShaders.ps1` 重编译并通过 `-VerifyOnly` 字节级校验。
- 渲染质量（4096×4096 棋盘格、zone plate、chirp、细节图、阶梯图，50%/25%/37%/61%）：Area 的混叠/摩尔纹最低且无过冲；Mitchell/Robidoux 介于两者之间；Lanczos-2/Kaiser-2/Catmull-Rom 细节保留最高但负瓣更明显。核函数分析表见 `artifacts\verify\downscale\analysis\kernel-analysis-results.txt`，截图与逐模式结果在 `artifacts\verify\downscale\`。
- 安装包：`LinYuSen.qHdrImageViewer 1.2.3.0`，`Status=Ok`；诊断页版本 1.2.3.0。
- 安装后实测：设置页缩小插值下拉显示新算法列表；旧设置 `ZoomInterpolation=4` 升级后放大=Lanczos、缩小=Lanczos-2，改为「面积平均（Box）」后持久化为 `ZoomDownscaleMode=8`（截图 `installed-1.2.3-01/02`）。
- 验证后已把本机设置恢复为用户原值；无 `crash.log`。

## 安装

`artifacts\release\1.2.3.0\`：MSIX（108.1 MB，含完整编解码器）+ portable zip（65 MB）+ `install.ps1` / `INSTALL.md` / `BUILD_INFO.txt`。包标识不变，可直接覆盖升级。

## 已知限制

- 受 4×4 taps 与 GPU mip 链限制，没有实现 CPU 版的 EWA/Jinc 等更重型滤波；如需可后续再加（成本与收益不成比例）。
- 100% 恰好等于 1:1 时走“缩小”档（与 1.2.2 一致）。
- 仅缩小档有专用算法；放大档仍是 Auto/Nearest/Bilinear/Bicubic/Lanczos。
