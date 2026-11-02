# 设计说明

## 目标

为 PCL Harness 提供一个**可独立迭代**的插件市场：用户能发现、安装、更新、禁用与卸载
第三方插件；插件作者能通过提交 JSON 条目上架。市场以普通插件形式交付，不修改启动器，
不占用启动器版本节奏。

非目标：回滚、评分/评论、账号体系、自动签名验证、跨插件点名安装、服务端搜索。

## 为什么是独立插件

启动器在 3.7.3 已经提供了市场所需的全部原语：

- `Host.Plugins`：`List` / `Find` / `ImportPackage` / `SetEnabled` / `Uninstall`；
- `plugins` 权限：把「管理其他插件」从普通插件能力里单独拆出；
- `net.dynamic`：索引主机可配置，未知主机运行时申请；
- `IPclPluginUi`：自定义页面与标题栏入口。

因此市场不需要进入启动器主干。独立插件可以自行发版、自带索引与界面，出问题也能被
用户直接禁用或卸载，不会把启动器拖进崩溃链。

## 分层

```
MarketPlugin.vb   插件生命周期：读缓存、订阅插件安装/卸载、设置项、后台任务调度
MarketPage.vb     UI：搜索、刷新、卡片状态、确认框、忙状态
MarketClient.vb   网络与安装：镜像回退、缓存、SHA-256、Host.Plugins 调用
MarketModel.vb    数据模型：索引解析、字段校验、版本比较、格式化
```

状态只有一个来源：`MarketState`（索引、抓取时间、来源、警告、错误、是否加载中）。
页面每次 `RenderAll()` 都从 `Owner.GetState()` 与 `Host.Plugins.List()` 重新取值，
**不缓存 `PluginSummary`**，因为宿主在 `SetEnabled` / `Uninstall` 后会 `Refresh(True)`。

## 索引

- 根索引：`index.json`，`schema: 1`，字段见 [`index/schema.md`](../index/schema.md)。
- 源条目：`index/plugins/<id>.json`，一个文件一个插件，便于 PR 审核与冲突收敛。
- 生成：`tools/make-index.ps1` 校验并排序（精选优先、id 升序），原子写入根索引。
- 兼容：客户端忽略未知字段；只接受 `schema: 1`；重复 id 直接判索引无效。
- 空索引合法，页面显示「索引里还没有插件」。

下载量走一期方案：`tools/update-downloads.ps1` 分页读取 GitHub Releases，把
`assets[].download_count` 按 `<id>-<version>.pclplugin` 归组写入 `downloads.json`；
`make-index.ps1` 合并为条目的 `downloads: {total, updatedAt, source}`。客户端只读索引，
不直连 GitHub API，避免限流与网络不可达；缺失字段不显示，不补 0。

不引入分片与服务端：市场条目量级小，单文件索引便于 GitHub Pages / raw 直接托管，
也便于用户自建镜像。未来若条目超过数千，可在 `schema: 2` 引入分片与增量。

## 网络与缓存

默认索引：

```
https://raw.githubusercontent.com/Badqf/PCL-Harness-Market/main/index.json
```

插件清单声明固定主机：`raw.githubusercontent.com`、`github.com`、
`objects.githubusercontent.com`、`ghfast.top`、`gh-proxy.com`，并开启 `net.dynamic`，
因此用户自定义索引主机时会触发一次授权弹窗，写入 `trust.json` 后长期有效。

镜像策略：

- `preferMirror = true`：`ghfast.top` → `gh-proxy.com` → 原地址；
- `preferMirror = false`：原地址 → 镜像；
- 回环地址与镜像主机本身不再套镜像，避免递归与本地调试被改写。

缓存位置（无需 `files` 权限，均在 `Host.DataFolder` 内）：

| 路径 | 内容 |
| --- | --- |
| `cache\index.cache` | 最近一次成功解析的索引 JSON |
| `cache\index.meta` | 索引 URL 与抓取时间 |
| `packages\<id>-<version>.pclplugin` | 下载成功的安装包 |

索引 10 分钟内非强制刷新直接回缓存；网络失败时回退缓存并附加警告，无缓存才报错。

> 缓存文件名刻意不使用 `.json`。宿主的 `FileSystemWatcher("*.json")` 会递归监听插件
> 根目录，任何 `data\**\*.json` 写入都会触发热重载，导致市场自己重载自己。改用
> `.cache` / `.meta` 后刷新不再触发重载。

## 安装 / 更新 / 卸载

1. **安装**：校验条目 `sha256` 是否存在 → 下载（镜像回退）→ 计算 SHA-256 →
   `Host.Plugins.ImportPackage(path, overwrite:=True)`。
2. **新装**：宿主导入后保持禁用，市场提示「已安装，默认未启用」，由用户显式启用。
3. **更新**：覆盖安装保留 data 与设置。3.7.4 起，若旧版本正在运行，宿主把新目录放进
   `Plugins\.pending\<id>` 并返回 `NeedRestart`，重启时替换；未运行时直接替换。
4. **卸载**：宿主先禁用，再把插件目录移到 `Plugins\.trash\<id>-<timestamp>`，保留
   data 与设置。

### 加载锁与待生效队列

宿主用 `Assembly.LoadFrom` 加载插件 DLL，文件在进程退出前被锁定。3.7.4 之前，对
**已加载**插件覆盖安装会返回 `ERR_WRITE_FAILED`，卸载会被 `IOException` 拒绝；禁用只改
启用表，不会卸载程序集，所以「禁用后立刻更新/卸载」在同一进程内仍会失败。

3.7.4 的 `StagePendingUpdate` 本应接管运行中插件的覆盖安装，但 3.7.4 初始实现没有在
`InstallFromFolder` 里接线；本项目验证时补上了该调用（`Existing.Loaded` 时直接
`Return StagePendingUpdate(...)`），随后排队更新、`NeedRestart` 与重启替换全部通过。
市场侧通过反射读取 `NeedRestart`，旧版则按 `Message = NEED_RESTART` 兜底；页面只在
存在待生效更新时显示「需要重启以生效 / 立即重启」。

### 自我更新与保护

市场不会对自己执行「安装」「卸载」，但允许**自更新**：从索引同级的 `update.json`
读取新版本，下载并交给宿主导入；3.7.4 下同样进入待生效队列。这样既避免卸载自己触发
`InvalidOperationException`，也不会覆盖正在加载的 DLL。快速重启只在 `NeedRestart`
为真时显示。

### 错误映射

`PluginImportResult.Code` 被映射为中文可操作提示；典型：

| Code | 提示 |
| --- | --- |
| `ERR_MIN_API` | 需要更高版本的启动器 / 插件 API |
| `ERR_SHA256` | 摘要不符，包可能损坏或被替换 |
| `ERR_EXISTS` | 已安装且未允许覆盖 |
| `ERR_MISSING_DEP` | 缺少依赖插件 |
| `ERR_WRITE_FAILED` | 旧版本可能在运行，禁用并重启后再试 |

## UI

- 页首：标题、副标题（市场里已安装数量）；
- 控制卡：分类标签、搜索框、刷新、索引状态收进同一张 `MyCard`，避免顶部元素散落；
- 卡片：名称/版本/精选、作者/大小/日期/下载量/最低 API/标签、摘要；展开后显示描述、更新
  说明、许可证、摘要与主页；
- 右侧：状态文本 + 下载进度条 + 安装/更新、启用/禁用、卸载、详情；收藏为右下角文字链；
- 自更新卡：控制卡下方，仅在有新版本或待重启更新时出现；显示「更新」或「立即重启」。
- 分类与计数只针对索引条目：发现=索引总数，收藏=索引中已收藏，已安装/已禁用=索引中
  已安装条目的启用状态；索引外的本地插件不在市场里出现，交给「插件」页；
- 操作均经 `Host.Ask` 确认；进行中的条目用 `BusyIds` 禁用按钮，完成后统一重渲染。

设置项由宿主渲染并自动持久化：`indexUrl`、`preferMirror`、`autoRefresh`。

## 热重载与回调

宿主导入/启用/卸载插件后会 `Refresh(True)` 并热重载所有已启用插件，包括市场自己。
旧的市场实例会收到 `OnUnload`，但**已经打开的页面仍绑定旧实例**，宿主只重建标题栏
入口，不重建当前页面。因此旧实例的后台任务回调必须继续有效：

- `NotifyChanged` / `RunOnUi` / `FetchAsync` / `InstallAsync` 不再因 `_unloaded` 提前
  返回；
- 仅 `OnUnload` 退订事件，避免重复订阅；
- 回调统一 `Host.RunInUi`，内层 `Try` 吞掉页面关闭后的异常。

这样安装/启用触发的自身热重载不会让页面卡在「进行中」。

## 安全模型

- **摘要不是签名**：`sha256` 只能证明下载字节与索引一致。索引与包同源，仓库被攻破时
  两者可一起被替换。当前依赖 HTTPS 与仓库维护者审核，签名验证列在后续工作。
- **执行即信任**：启用插件等于运行任意 .NET 代码。UI 在启用前明确提示，只启用信任来源。
- **权限**：市场仅声明 `ui`、`events`、`plugins`、`net`（固定主机）、`net.dynamic`；
  不申请 `files`，所有文件写入限制在自己的 data 目录。
- **包校验**：宿主自身还会校验 zip 路径、清单、DLL 存在性、SHA-256（若清单声明）、
  依赖与最低 API，市场不绕过这些检查。
- **镜像**：第三方镜像可以看到请求，但无法伪造内容而不破坏 SHA-256；仍建议对高敏感
  用户关闭镜像。

## 测试矩阵（3.7.4 实机）

| 场景 | 结果 |
| --- | --- |
| 首次拉取索引，回环地址 | 触发 net.dynamic 授权，允许后成功 |
| 安装新插件 | 成功，默认禁用，页面即时更新 |
| 启用 / 禁用 | 成功，状态与按钮同步 |
| 更新（未启用） | 成功覆盖，版本号更新，保留 data |
| 更新（已加载） | 宿主进入 `.pending` 队列，显示待重启卡 |
| 重启后应用待生效更新 | 成功替换，版本号与日志一致 |
| 市场自更新 | 成功入队，重启后生效 |
| 下载进度条 | 慢速服务器下可见，按钮禁用，进度推进 |
| 下载量展示 | `downloads.total` 存在时显示「下载 N」 |
| 卸载（未加载） | 成功，目录移入 `.trash` |
| 卸载（已加载） | 重启后成功 |
| 断网刷新 | 回退缓存并显示警告 |
| 刷新索引 | 不再触发热重载（缓存改名后） |

## 后续工作

1. 索引签名（minisign / Ed25519）与公钥固定，缓解索引与包同源被替换的问题。
2. 多版本与回滚：条目携带 `versions[]`，允许固定旧版本。
3. 依赖插件的可视化与一键安装。
4. 索引分片 / CDN 镜像健康检查。
5. 插件权限差异提示（升级时新增权限需要重新确认）。
6. 下载量二期：可选上报服务、趋势与热门排序。
