# 发现页下载量统计方案

> 目标：在「发现」页给每个插件显示可信、可解释的下载量；不伪造数字，不把缺失数据当成 0。
>
> 现状：市场是纯静态仓库，插件包放在 `index/packages/`，由 `raw.githubusercontent.com` 提供。
> 这个位置**没有**任何下载计数接口，所以下载量不能凭空出现，必须先选定「计数发生在哪一层」。

## 先给结论

推荐分两期，不一步到位：

| 阶段 | 做法 | 能拿到什么 | 成本 | 建议 |
| --- | --- | --- | --- | --- |
| 一期（1 天内） | 插件包改挂 GitHub Release，GitHub Action 每天把各 Release 资产的 `download_count` 汇总写进 `index.json` | 每个插件一个累计下载量，日更 | 无服务器；需要一个仓库 Action | **先做这个** |
| 二期（1–2 天 + 运维） | Cloudflare Worker + KV/D1 自建计数接口，客户端在安装成功后上报一次 | 任意托管位置都能计数，可做日/周趋势 | 一个免费 Worker + 少量运维 | 需要趋势/精确计数时再做 |

如果只想要「发现页有个数字」，一期足够；如果要把下载量做成排序、趋势或推荐依据，必须上二期。

---

## 为什么不能在客户端直接查

常见想法是客户端安装前调 GitHub API 查 `download_count`。不推荐：

- GitHub REST API 未认证限流 **60 次/小时/IP**，公共网络下很容易打满；
- GitHub API 在部分网络环境不可达，`ghfast.top` / `gh-proxy.com` 只代理下载，不代理 API；
- 每次刷新都查会让「刷新」变慢，失败时还要处理超时；
- `raw.githubusercontent.com` 的仓库文件**没有**下载计数，GitHub 只统计 Release 资产。

把查询放到 GitHub Action 里，用仓库自带的 `GITHUB_TOKEN`（5000 次/小时）每天跑一次，客户端只读 `index.json`，就绕开了限流和网络问题，代价是数据有一天延迟。

---

## 一期：Release 资产计数 + Action 烘焙进索引

### 数据从哪来

GitHub 对 **Release 资产**（`assets[].download_count`）提供公开累计下载数。
所以一期要求：每个 `.pclplugin` 作为某个 Release 的资产上传，`latest.download` 指向
`https://github.com/Badqf/PCL-Harness-Market/releases/download/<tag>/<file>.pclplugin`。

仓库里的 `index/packages/*.pclplugin` 可以保留作为源码/镜像，但**不计入**下载量；
要计数就必须以 Release 资产为准。示例插件和未来的插件都按这个规则发布。

### 索引字段

在条目顶层增加可选字段（不影响旧客户端）：

```json
{
  "id": "hello-market",
  "downloads": {
    "total": 128,
    "updatedAt": "2026-11-02T10:00:00Z",
    "source": "github-release"
  }
}
```

- `total`：所有版本 Release 资产的累计和；缺失表示「未知」，UI 不显示，不显示 0。
- `updatedAt`：统计时间，用于提示「数据可能滞后」。
- `source`：`github-release` / `worker`，便于以后合并两种来源。

### 生成流程

1. `tools/update-downloads.ps1`
   - 调 `GET /repos/Badqf/PCL-Harness-Market/releases?per_page=100`（带 `GITHUB_TOKEN`）；
   - 按资产文件名里的 `<id>-<version>.pclplugin` 归组，累加 `download_count`；
   - 输出 `downloads.json`（临时映射），不直接改作者条目。
2. `tools/make-index.ps1`
   - 读取 `index/plugins/*.json`；
   - 若存在 `downloads.json`，把对应 id 的 `total/updatedAt` 合并进条目；
   - 生成根 `index.json`。
3. `.github/workflows/update-downloads.yml`
   - `schedule: cron: "17 3 * * *"`（每天 UTC 03:17，北京时间 11:17）；
   - `workflow_dispatch` 手动触发；
   - 跑脚本 → `make-index.ps1` → 若 `index.json` 有变化则提交（`github-actions[bot]`）；
   - 用 `GITHUB_TOKEN`，不需要额外 secret。

### 客户端展示

- 卡片 meta 增加 `下载 128` / `下载 1.2k`；数字来自 `entry.Downloads.Total`。
- 无 `downloads` 字段：不显示，不显示「0」。
- 卡片 detail 里可写「下载量统计自 GitHub Release，日更」。
- 不把下载量作为唯一排序键；「发现」仍按 `featured` + 名称，后续可加「热门」排序但只作参考。
- 下载量是**参考信号**，不是签名/可信度证明；页脚安全提示保持不变。

### 一期边界

- 只能统计 Release 资产；仓库直链、第三方镜像的下载不计入。
- 日更，最多滞后约 24 小时；GitHub 自身统计也可能延迟。
- 迁移到 Release 后，历史仓库直链下载无法追溯。
- Action 需要仓库 `Actions` 权限；公开仓库免费额度足够。

---

## 二期：自建轻量计数（需要趋势/精确时）

### 架构

```text
PCL Harness 插件市场
   │  安装成功后 POST /v1/hit  {id, version, installId}
   ▼
Cloudflare Worker  ──►  KV / D1（按 id + 日期聚合）
   ▲
   │  GET /v1/stats  → {id: {total, last7d, last30d}}
   │
index.json 里的 stats 配置（可选）
```

- Worker 域名例如 `https://market-stats.example.workers.dev`，加入市场 `net.hosts`
  （或依赖 `net.dynamic` 首次授权）。
- 计数表：`plugin_id, version, day, count`；总量由 SQL 聚合，不存原始 IP。
- 上报去重：客户端为每个 `(id, version)` 生成一次性 `installId`，成功后写
  `data/stats-pending.txt`，确认 2xx 后删除；失败最多重试 3 次。
- 防刷：按 IP 哈希 + 分钟窗口限流（Worker KV 计数）；单 IP 每日上限；异常增量告警。
- 隐私：不存 IP、不存设备标识；只存 `id/version/installId` 的哈希或随机串；文档写明。
- 关闭开关：插件设置增加「匿名上报安装量」（默认开）和「显示下载量」（默认开）。
- 降级：接口超时/失败不阻塞安装；UI 显示上次缓存值 + 「统计暂不可用」。

### 成本

- Cloudflare Workers 免费版 10 万请求/天；KV 免费 10 万读/天、1000 写/天；
  如果写量大，用 D1 免费额度（500 万行读/天、10 万行写/天）或按量付费。
- 一个 Worker + 一张表，维护量很小；但需要有人持有账号、处理滥用。

### 二期边界

- 需要服务端和域名，和「纯静态仓库」的定位不同；是否值得取决于是否要做趋势/热门。
- 计数仍是客户端自报，无法完全防刷；只能做「参考热度」，不能当权威数据。
- 首次访问新域名会触发 `net.dynamic` 授权弹窗（除非提前加入 `net.hosts`）。

---

## 不推荐的做法

- **客户端直查 GitHub API**：限流 + 网络不可达 + 刷新变慢。
- **把下载量写死进索引**：作者可以随便填，失去意义。
- **用 raw 文件访问日志**：GitHub 不提供公开的按文件访问计数。
- **本地安装次数当社区下载量**：只对自己有意义，会误导。
- **把下载量当签名/可信度**：下载多 ≠ 安全，页脚的安全提示必须保留。

---

## 建议的落地顺序

1. 确认是否接受「插件包迁到 GitHub Release」——这是一期的前提。
2. 实现 `tools/update-downloads.ps1` + `update-downloads.yml`，先在示例插件上跑通。
3. `MarketModel` 增加可选 `Downloads`，卡片显示；旧索引无字段时静默跳过。
4. 观察一两周数据质量；如果只需要「有个数」，到此为止。
5. 若要做趋势/热门/更准确，再上 Worker，并把 `source` 标为 `worker`。

需要你拍板的两件事：

- 一期是否把 `index/packages/*.pclplugin` 迁到 Releases（并让 `latest.download` 指向 Release 资产）？
- 下载量只做「累计展示」，还是也要「周趋势 / 热门排序」？后者才需要二期。
