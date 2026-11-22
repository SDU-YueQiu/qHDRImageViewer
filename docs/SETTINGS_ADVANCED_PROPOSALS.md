# 设置页“高级用户”改造方案（供评审，未实现）

> 状态：**仅方案，未改动任何设置相关代码**。等选定方案后再进入实现。
> 参考对象：ImageGlass 9.x（WebView2 设置页）与 10.x（Avalonia 原生设置框架）。
> 本文所有“现有/硬编码”判断均基于当前仓库代码（1.0.46）核对。

---

## 1. 现状与差距

当前 `Pages/SettingsPage.xaml` 是**单页 + 5 个分组 + 14 个 SettingsCard**：

| 分组 | 现有项 |
|---|---|
| 个性化 | 主题、语言 |
| 交互 | 鼠标滚轮行为（翻页/缩放）、触控板手势 |
| 查看 | 检查器面板、胶片栏、色域映射模式 |
| 性能 | 相邻图预加载、预加载半径（1–12）、云占位文件跳过 |
| 系统 | 打开 Windows 默认应用设置 |

对比 ImageGlass 的差距不在“控件好不好看”，而在**可调项的数量与层级**：

- ImageGlass 9/10 每版约 **95–100 个强类型配置项**，设置窗口按 14–15 个导航页分组；我们的持久化设置只有 11 项。
- ImageGlass 把大量“高级用户才会碰”的参数暴露出来（缓存预算、缩略图策略、缩放档位、滚轮四修饰键映射、色管、工具默认值、配置文件分层/锁定/便携模式），我们对应能力都有，但**写死在代码里**，用户改不了。
- ImageGlass 10 的设置页有 **Ctrl+K 全局搜索 + 面包屑定位 + 单项重置 + admin 锁定 + 两阶段 OK/Apply**；我们只能滚动查找、改完即存、无法恢复默认。
- 好消息：我们的 HDR/色彩能力**强于** ImageGlass（IG 只有 ICC + `EnableHdrToneMapping` 一个开关），所以“高级设置”里最有含金量的一块是 HDR/色彩，目前只存在于 Inspector 浮层的临时控件里，不持久化。

差距清单（按改造价值排序）：

1. **缩放/交互手参数**不可调：缩放步进 1.1、动画 140 ms、渲染防抖 60 ms、悬浮栏 2.2 s 自动隐藏、缩放范围 1%–800%。
2. **HDR/色彩**：`HdrViewMode`、`ColorGamutMappingMode` 已持久化；但 Headroom 来源/手动档位、SDR 白点覆盖、自适应 tone mapping、显示能力覆盖都是 Inspector 内临时状态，重开即丢。
3. **性能**：解码缓存 320 MB、元数据磁盘缓存 200 个文件、目录索引缓存 4 个目录，全是 const。
4. **工具默认值**：裁剪初始选区、导出默认模式/目录策略、批处理重试次数等散落在流程代码里。
5. **配置管理**：无“恢复默认/导入导出/配置文件路径/诊断信息”。

---

## 2. 从 ImageGlass 学什么、不学什么

值得借鉴（已核对 IG 源码行为）：

- **分组导航 + 搜索定位**：IG 10 用 `SettingsRegistry` 注册每个设置项的页面/分组/标签，Ctrl+K 模糊搜索后跳转并高亮。
- **每项一个“说明 + 默认值 + 重置”**：IG 9 用 `title` 提示范围，IG 10 每页有局部 Reset。
- **两阶段提交**：IG 10 的 `SettingsViewModel` 先把改动放在 pending 字典，OK/Apply 才写盘并触发副作用重载（列表、当前图、色管、语言、主题）。
- **高级/受管分层**：`igconfig.default.json`（预置）+ 用户 + `igconfig.admin.json`（锁定）；锁定项在 UI 中禁用并显示警告条。
- **配置即文件**：用户可直接编辑 JSON；便携模式把配置放程序目录；没有独立的“导入/导出设置”UI（我们用文件操作补上即可）。
- **工具级设置**：如 `CropToolConfig` 的 `AspectRatio / InitSelectionType / AutoCenterSelection / CloseToolAfterSaving`。

不建议照搬：

- IG 的 WebView2 HTML 设置页（9.x）：与 WinUI 3 观感不一致，且维护成本高。
- 工具栏拖拽定制、布局编辑器、插件市场、主题包、幻灯片：我们没有对应模型或收益不足。
- Pro 商业化徽章 / 许可证 UI：不适用。

---

## 3. 可调项总清单（分三档）

“来源”列说明该项现在在哪：
- **已有**＝`AppUserSettings` 已持久化；
- **提升**＝代码里已有能力/常量，只需搬到设置；
- **新派生**＝需要在设置层加开关并把现有逻辑改为读设置；
- **需开发**＝设置背后对应的功能本身还要补。

### 3.1 查看/交互

| 设置项 | 类型/范围 | 默认 | 来源 | 说明 |
|---|---|---|---|---|
| 鼠标滚轮动作 | 导航 / 缩放 | 导航 | 已有 | 现有 `MouseWheelBehavior` |
| Ctrl+滚轮动作 | 缩放 / 不做 | 缩放 | 提升 | 现在 Ctrl 恒为缩放，可做成映射 |
| Shift/Alt+滚轮动作 | 水平平移 / 忽略 / 上一张下一张 | 现行为 | 新派生 | 高级滚轮映射（IG Mouse 页） |
| 触控板手势 | 开/关 | 开 | 已有 | `TouchpadGesturesEnabled` |
| 缩放步进 | 1.02–1.30（按“每格 ×1.1”展示） | 1.1 | 提升 | 已提取为 `Presentation/ViewerZoom.cs` 常量，最近一次改动可直接变成设置 |
| 缩放动画时长 | 0–300 ms | 140 | 提升 | `ViewerMotion.ZoomDurationMilliseconds` |
| 缩放范围 | 最小 % / 最大 % | 1 / 800 | 提升 | `ViewerViewportMath.ClampUserZoomScale` 中的 0.01/8.0 |
| 打开图片时的缩放 | 适合窗口 / 100% / 上次 | 适合窗口 | 新派生 | `HomePage.ResetZoomToFit` 调用点 |
| 滚轮缩小止于“适合窗口” | 开/关 | 关 | 需开发 | Windows 照片的行为；我们目前可自由缩到 1% |
| 打开时恢复上次图片 | 开/关 | 开 | 新派生 | `ViewerSessionState` 已有数据通路 |
| 悬浮工具栏自动隐藏 | 0–10 s，0=常显 | 2.2 s | 提升 | `ViewerChromeAutoHideMilliseconds` |
| 检查器面板 / 胶片栏 | 开/关 | 开 | 已有 | 现有两项 |
| 状态栏显示字段 | 文件名/尺寸/文件大小/HDR 类型 | 全开 | 新派生 | 对应 IG `ImageInfoTags` |

### 3.2 HDR 与色彩（我们的强项，建议做成高级分组核心）

| 设置项 | 类型/范围 | 默认 | 来源 | 说明 |
|---|---|---|---|---|
| 默认 HDR 显示模式 | SDR / 自适应 / 备选图 / 增益图 | 自适应 | 已有 | `HdrViewMode` |
| 色域映射 | 托管 / 裁剪 | 托管 | 已有 | `ColorGamutMappingMode` |
| Headroom 来源 | 系统自适应 / 手动 | 系统自适应 | 提升 | Inspector 临时控件 → 持久化 |
| 手动 Headroom 档位 | -4…+4 stops（显示能力上限） | 自动 | 提升 | `DisplayCapacityOverrideLog2` |
| SDR 白点覆盖 | 开/关 + 80–400 nits | 关 | 提升 | Inspector `SdrWhiteOverride` |
| 自适应 tone mapping | 开/关 | 关 | 提升 | `AdaptiveToneMappingEnabled`（当前硬编码 false） |
| 分析自动刷新 | 开/关 + 防抖 ms | 开/180 | 新派生 | `AnalysisAutoRefreshDebounceMilliseconds` |
| 增益图对比/色度图默认项 | 默认勾选边界等 | 现行为 | 新派生 | 查看器工具页 |
| 显示能力覆盖（调试） | 峰值亮度/色域 | 自动 | 需开发 | 仅在高级/诊断模式暴露 |

### 3.3 性能/缓存

| 设置项 | 类型/范围 | 默认 | 来源 | 说明 |
|---|---|---|---|---|
| 相邻图预加载 / 半径 | 开/关 + 1–12 | 开 / 5 | 已有 | 现有两项 |
| 云占位文件跳过 | 开/关 | 开 | 已有 | 现有 |
| 解码像素缓存预算 | 64–1024 MB | 320 MB | 提升 | `ImagePreloadCache.MaxDecodedPixelCacheBytes` |
| 元数据磁盘缓存条目 | 50–1000 | 200 | 提升 | `DirectoryMetadataCache.MaxDiskCacheFiles` |
| 目录索引缓存数 | 1–16 | 4 | 提升 | `FolderImageIndexCache.MaximumCachedDirectories` |
| 内存压力自动回收 | 开/关 | 开 | 新派生 | `ImageMemoryPressureService` 阈值策略 |
| 胶片缩略图预载窗口 | 小/中/大 | 中 | 需开发 | 当前固定窗口 |

### 3.4 导出/裁剪/批处理

| 设置项 | 类型/范围 | 默认 | 来源 | 说明 |
|---|---|---|---|---|
| 默认导出模式 | SDR / Ultra HDR / 保留增益图 / 单层 HDR | 按源文件推荐 | 新派生 | `SaveAsExportMode` 选择 |
| 导出目录策略 | 同目录 / 上次目录 / 固定目录 | 同目录 | 新派生 | 现有导出流程 |
| 覆盖确认 | 开/关 | 开 | 新派生 | `ExportFileTransaction` 之上 |
| JPEG/AVIF 质量 | 60–100 | 需确认现状 | 需开发 | 需核对导出后端 |
| 裁剪默认比例 | 自由 / 1:1 / 4:3 / 16:9 … | 自由 | 需开发 | 对应 IG `CropToolConfig` |
| 裁剪初始选区 | 50% / 全图 / 上次 | 50% | 需开发 | 同上 |
| 裁剪保存后关闭 | 开/关 | 关 | 需开发 | 同上 |
| 批处理失败重试 | 0–5 次 | 1 | 新派生 | `BatchExportQueue` |

### 3.5 系统/高级/诊断

| 设置项 | 类型 | 默认 | 来源 | 说明 |
|---|---|---|---|---|
| 默认应用设置 | 按钮 | — | 已有 | 现有按钮 |
| 配置文件夹位置 | 只读 + 打开按钮 | `%LocalAppData%\qHdrImageViewer` | 新派生 | `AppSettingsService` |
| 导出/导入设置 JSON | 按钮 | — | 新派生 | 便于迁移与排障 |
| 恢复默认设置 | 按钮（二次确认） | — | 新派生 | IG 的 Reset/Quick Setup |
| 设置文件 schema 版本 | 内部 | 1 | 新派生 | 迁移与兼容 |
| 诊断信息 | 只读面板 | — | 新派生 | 渲染器状态、显示器 HDR 能力、缓存命中、日志路径 |
| 管理员锁定项 | 隐藏/禁用 | — | 需开发（C 方案） | 对应 IG `admin.json` |
| 便携模式配置 | 开/关 | 关 | 需开发（C 方案） | 配置写程序目录 |
| 快捷键总览/重绑定 | 列表 | 现行为 | 需开发（C 方案） | 现有加速键散落在 HomePage |

### 3.6 明确不建议做

幻灯片、插件/外部工具、主题包、布局编辑器、工具栏按钮拖拽排序、多语言包安装器、Pro/许可证、文件格式关联注册（已由 MSIX 清单接管）。

---

## 4. 三套方案

### 方案 A｜轻量增强版：单页“高级”折叠区（最小改动）

**定位**：不动导航结构，只在现有 `SettingsPage` 里把高级项“列全、说明清楚、可恢复默认”。适合“只想把藏起来的参数放出来”的诉求。

**信息架构**（仍是单页，5 组 → 7 组）：

```
设置
├─ 个性化        主题、语言
├─ 查看与交互    滚轮、Ctrl+滚轮、触控板、缩放步进、动画时长、缩放范围、
│                打开时缩放、悬浮栏隐藏、检查器、胶片、状态栏字段
├─ HDR 与色彩    显示模式、色域映射、Headroom 来源/档位、SDR 白点、
│                自适应 tone mapping、分析刷新
├─ 性能与缓存    预加载/半径、云占位、解码缓存、元数据缓存、目录索引缓存
├─ 导出与工具    默认导出模式、导出目录策略、覆盖确认、裁剪默认值
├─ 系统          默认应用、配置文件夹、导入/导出、恢复默认
└─ 高级（默认折叠）  实验项 + 诊断信息 + 设置 schema 版本
```

**交互细节**：
- `SettingsCard` 保持现有风格；每张卡片增加 `Description`（一行说明）与“↺”单项重置按钮；折叠分组用 `Expander`。
- 数字型（缓存 MB、动画 ms、缩放步进）用 `NumberBox` 或 `Slider` + 值标签；布尔用 `ToggleSwitch`；枚举用 `ComboBox`。
- 页顶加一行“搜索设置”过滤框（只过滤当前页静态列表，不实现跨页跳转）。
- 保存仍为现有“改动即存（150 ms 防抖）”；语言继续走重启 InfoBar。
- 页底“恢复默认”（二次确认）、“导出设置/导入设置”（文件选择器 + JSON）。

**技术设计**：
- `AppUserSettings` 扩到约 25–30 个属性，新增 `SchemaVersion` 字段与 `Normalize`/`Copy` 逻辑（沿用现有 `AppUserSettings.Normalize*` 模式）。
- 新增 `AppSettingsService.SetXxx` 方法 + `SettingsChanged` 事件消费方改造：缩放步进/动画/缓存预算等在对应服务加一个“从设置读取”的入口（`ImagePreloadCache` 需要支持运行时修改预算）。
- 不引入注册表/搜索框架；设置项直接写在 XAML 里。
- 改动面：`SettingsPage.xaml(.cs)`、`AppSettingsService`、`Strings/*/Resources.resw`（新增约 20 个 key × 13 语言）、2–3 个服务的常量读取点。

**工作量**：约 1.5–2.5 人日（含 13 语言本地化与回归）。
**风险**：低。主要风险是“设置项太多导致单页过长”，用折叠区缓解。
**适合**：想尽快拿到可调项，不介意滚动查找。
**验收**：
- 所有新增项改完立即生效（语言除外）；
- 重启后保留；
- 单项重置与全局恢复默认可用；
- 导入/导出往返一致；旧 `settings.json` 可无损升级。

---

### 方案 B｜ImageGlass 导航版：注册表驱动 + 分组导航 + 搜索（推荐）

**定位**：把 ImageGlass 10 的设置骨架搬过来：**声明式设置注册表**驱动 UI、搜索与定位，左侧导航分页。工作量适中，是“像 ImageGlass 的高级设置”最接近的形态。

**信息架构**（`SettingsPage` 改为“导航 + 内容宿主”，复用现有 `SettingsCard` 外观）：

```
设置（左侧 NavigationView）
├─ 常规        语言、主题、启动恢复、默认应用
├─ 查看器      打开时缩放、缩放步进/动画/范围、滚轮映射、触控板、
│              悬浮栏、检查器、胶片、状态栏字段
├─ HDR 与色彩  显示模式、色域映射、Headroom、SDR 白点、tone mapping
├─ 性能        预加载、缓存预算、元数据/索引缓存、内存回收
├─ 工具与导出  导出默认值、覆盖确认、裁剪默认值、批处理重试
├─ 外观语言    （语言/主题的独立页，含重启提示）
└─ 高级        配置文件、导入导出、恢复默认、诊断、实验项（默认折叠）
```

**交互细节**：
- 标题栏右侧 240px 搜索框，**Ctrl+K 聚焦**；输入即从注册表模糊匹配“本地化标签 / 设置 Id / 关键词”，下拉结果带面包屑（如 `性能 › 解码缓存预算`）；回车跳转对应页并定位/高亮卡片（`StartBringIntoView` + 短暂描边）。
- 每张卡片：标题 + 一行说明 + 值控件 + 悬浮“重置为默认（默认值）”按钮。
- 页底：`恢复本页默认`；高级页：`恢复全部默认 / 导出 / 导入 / 打开配置文件夹`。
- “需要重启”仅在语言项使用现有 InfoBar；其余即时生效。
- 数值项可选中直接输入，带范围校验（越界自动夹取并提示）。

**技术设计**：
- 新增 `Settings/SettingsRegistry.cs`：
  - `SettingDefinition(Id, PageId, GroupId, LabelResourceKey, DescriptionResourceKey, DefaultValue, ValueKind, Min/Max/Step, Unit, IsAdvanced, RequiresRestart, ApplyAction)`
  - `SettingsRegistry` 注册全部定义，提供 `Search(query)`、`GetPage(pageId)`、`CreateCard(def)`。
- 新增 `Settings/SettingsViewModel.cs`（轻量）：把 `AppUserSettings` 与 `SettingDefinition` 绑定，负责类型转换、校验、默认值比较、写入 `AppSettingsService`。
- 设置项从 11 扩到 **约 35–40 个**；覆盖 3.1–3.5 中“已有/提升/新派生”项（不含“需开发”的裁剪/质量等）。
- 侧栏用 `NavigationView`（`PaneDisplayMode=Left`，与主窗口导航风格一致）；页面用 `UserControl` 或按 `PageId` 动态生成 `SettingsCard`。
- 本地化：按“功能 Id → 资源 key”映射，新增约 60–80 个 key × 13 语言；可用 `FallbackResources` 兜底，分批补翻译。
- 兼容：`AppUserSettings` 增加 `SchemaVersion=2` 与迁移逻辑（缺失字段取默认值；非法值夹取；未知字段丢弃）。
- 预计新增文件：`Settings/SettingsRegistry.cs`、`Settings/SettingDefinition.cs`、`Settings/SettingsViewModel.cs`、`Settings/SettingsPageHost.xaml(.cs)`、若干 `SettingsPages/*.xaml`；改动 `SettingsPage`、`AppSettingsService`、相关服务读取点。

**工作量**：约 4–6 人日。
**风险**：中。主要是设置项“接通”到运行时（缓存预算、渲染参数）需要逐项验证；搜索定位与 13 语言本地化是额外工作量。
**适合**：想要真正接近 ImageGlass 的体验，且可以接受一次中等规模改造。
**验收**：
- 导航 7 页 + 搜索跳转高亮可用；
- 注册表增加一个设置项后，UI/搜索/重置/保存自动出现（无需改页面代码）；
- 所有项即时生效或明确标注需重启；
- 旧设置文件自动迁移；恢复默认不影响语言以外的偏好（可选保留语言）。
- 键盘可达：Tab/方向键可遍历；每项有 AutomationProperties。

---

### 方案 C｜专业工作室版：B + 两阶段提交 + 配置分层 + 诊断

**定位**：完全对齐 ImageGlass 10 的“高级用户/受管环境”能力，再加上我们 HDR 专长。属于一次性投入大、但后续扩展最省的方案。

**在 B 之上新增**：

1. **两阶段编辑（OK/Apply/Cancel）**：`SettingsViewModel` 持 pending 字典，底部按钮 `应用 / 取消`；应用时批量写盘并统一触发副作用重载（渲染器、缓存、语言、主题），未应用的改动在关闭时提示。导航切换保留未提交改动。
2. **预设方案（Standard / Pro）与“恢复默认”向导**：对应 IG Quick Setup；一键切“日常看图”与“HDR 工作流”两套参数。
3. **配置分层与受管锁定**：`settings.default.json` / 用户 `settings.json` / `settings.admin.json`（只读、企业部署用）；admin 中出现的键在 UI 禁用并显示“由管理员锁定”警告条；支持便携模式（配置写程序目录）。对应 IG `igconfig.*.json` 与 `IsConfigLocked`。
4. **诊断页**：渲染器/显示器 HDR 能力（峰值、色域、EDID 来源）、缓存命中与占用、当前文件解码路径、日志/崩溃日志路径、一键复制诊断报告；实验性开关单独红色警示区（如 ABL soft proof 占位、显示能力覆盖）。
5. **快捷键总览 + 重绑定**：先做只读总览（现有 Ctrl+O/Ctrl+S/Ctrl+1/0/+/-/F 等），再逐步支持重绑定并持久化（对应 IG Keyboard 页）。
6. **工具级默认值**：裁剪（比例/初始选区/自动居中/保存后关闭）、色度图（默认边界/采样）、导出（模式/目录/质量/覆盖确认）、批处理（并发/重试）；每类工具一个设置子页。
7. **HDR/色彩进阶持久化**：Headroom 手动档位、SDR 白点覆盖、自适应 tone mapping、显示能力覆盖、分析像素采样策略（全部写入 `HdrRendererSettings` 并从设置装载）。

**信息架构**：B 的 7 页 + `诊断`、`快捷键`、`工具默认值` 3 页；共 10 页。设置项约 **55–65 个**。

**技术设计**：B 的注册表扩展到“值来源分层 + 锁定标记 + 应用副作用”；`SettingsViewModel` 增加 pending/commit；`AppSettingsService` 支持 default/admin 分层与逐键锁定查询；配置写盘增加版本迁移与备份（`settings.json.bak`）。风险点在“副作用重载”的顺序与幂等。

**工作量**：约 10–15 人日（可拆 3 期：B → 诊断/工具默认值 → 分层/快捷键重绑定）。
**风险**：中高。受管配置/portable 需要额外测试矩阵；两阶段提交会改变“改完即存”的用户习惯；快捷键重绑定要防冲突。
**适合**：把这作为长期主力看图工具、需要企业部署/多机同步/深度 HDR 调试。
**验收**：
- Apply/Cancel 语义正确，取消后运行时无残留；
- admin 锁定项不可改且提示来源；便携模式配置写程序目录；
- 诊断报告可复制；
- 快捷键总览与实际绑定一致（重绑定阶段再验冲突检测）。

---

## 5. 方案对比

| 维度 | A 轻量增强 | B 导航版（推荐） | C 工作室版 |
|---|---|---|---|
| 页面形态 | 单页 + 折叠分组 | NavigationView 7 页 + 搜索 | 10 页 + 分层 + diagnose |
| 可调项数量 | 约 25–30 | 约 35–40 | 约 55–65 |
| 与 ImageGlass 相似度 | 40%（只学“列全”） | 80%（导航/搜索/重置/注册表） | 95%（含 Apply/锁定/诊断） |
| 搜索定位 | 页内过滤 | Ctrl+K 跨页跳转 | Ctrl+K + 面包屑 |
| 提交模型 | 即时保存 | 即时保存 | Apply/Cancel |
| 配置分层/便携 | 无 | 单文件 + 导入导出 | default/admin/user + 便携 |
| 新增代码量 | 小（~500 行） | 中（~2000–2500 行） | 大（~4500–6000 行） |
| 本地化新增 key | ~20 × 13 | ~60–80 × 13 | ~100+ × 13 |
| 工作量 | 1.5–2.5 人日 | 4–6 人日 | 10–15 人日（可分 3 期） |
| 风险 | 低 | 中 | 中高 |
| 后续扩展成本 | 每加一项要改 XAML | 注册表加一行 | 注册表加一行 + 可选副作用 |

**建议**：选 **B**。理由：B 用一次中等改造拿到 ImageGlass 的核心体验（分组导航、搜索、单项重置、注册表驱动），后续加设置项几乎零边际成本；A 会很快遇到“页太长、找不到项”的问题，C 的受管/两阶段能力对个人使用收益低于成本。也可以 **B 起步、C 分期**：B 交付后，把 C 的“诊断页/工具默认值/配置导入导出”作为第二期，按需决定是否上 Apply/Cancel 与 admin 锁定。

---

## 6. 若选 B：实施拆分（供排期）

1. **第 1 步（基础设施）**：`SettingDefinition` + `SettingsRegistry` + `SettingsViewModel`；`AppUserSettings` 扩字段与 `SchemaVersion`/迁移；旧配置兼容测试。
2. **第 2 步（页面骨架）**：`NavigationView` 宿主 + 7 页 + `SettingsCard` 工厂；搜索框/Ctrl+K/定位高亮；每项重置。
3. **第 3 步（接通运行时）**：
   - 交互/缩放：`ViewerZoom` 步进、动画时长、打开时缩放、悬浮栏延时 → `HomePage` 读设置；
   - 性能：缓存预算/缓存数量 → 服务启动时注入并可热更新；
   - HDR/色彩：Inspector 临时值提升为持久化 → `HdrRendererSettings`；
   - 工具/导出：默认值读取点。
4. **第 4 步（本地化与回归）**：13 语言 key、`LocalizationTests` 覆盖、UI 冒烟；`docs/ARCHITECTURE.md` 与 `docs/ZOOM_HANDOFF.md` 更新。

---

## 7. 附：不做的部分（明确边界）

- 不引入 ImageGlass 的 WebView2/HTML 设置页；
- 不做插件/外部工具/主题包/幻灯片/布局编辑器；
- 不在本次改造中新增“设置同步到云”；
- 不改变现有“语言切换需重启”的策略（沿用 InfoBar）。
