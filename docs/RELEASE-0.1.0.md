# 插件市场 0.1.0

首个公开版本，面向 PCL Harness 3.7.3+（插件 API v6）。

## 这个版本做了什么

- 一个独立的「插件市场」插件：标题栏入口、插件页面、搜索、仅看已安装、刷新、卡片与详情。
- 索引驱动的发现与安装：schema 1 JSON、`featured` 与标签、更新说明；索引地址可在插件设置里修改。
- 网络：默认按 `ghfast.top` → `gh-proxy.com` → GitHub 原址回退；索引与安装包缓存在插件自己的 data 目录，断网时回退上次索引。
- 安装安全：下载后强制校验索引声明的 SHA-256；校验失败或索引缺摘要都不安装；安装完成**默认不启用**。
- 生命周期：安装、启用、禁用、卸载、更新；更新已启用插件会被 `PluginSummary.Loaded` 拦截，提示先禁用、关闭启动器再更新。
- 权限边界：`plugins` 管理其他插件；没有 `files` 权限，所有文件只写 `data\`；未知主机通过 `net.dynamic` 首次弹窗授权，之后长期有效。
- 仓库工具：`tools/make-index.ps1`（校验条目并生成根索引、支持 `-Check`）、`tools/dev-server.ps1`（本地回环静态服务器）、GitHub Actions 索引校验。
- 示例插件 `samples/hello-market`：一个可构建、可打包、可安装的最小示例，同时作为索引里的第一条内容。

## 安装

1. 下载本 Release 的 `pcl-harness-market-0.1.0.pclplugin`。
2. 打开启动器 →「插件」→「导入插件…」，选择该文件。
3. 导入后插件默认未启用，在插件列表里启用「插件市场」。
4. 标题栏出现「插件市场」入口；首次拉取 GitHub 索引时按提示允许网络访问。

也可以把 `plugin.json` 与 `PCLHarness.Market.dll` 手动放进
`PCLHarness\Plugins\pcl-harness-market\`，再在插件页启用。

## 发布资产

| 资产 | 大小 | SHA-256 |
| --- | ---: | --- |
| `pcl-harness-market-0.1.0.pclplugin` | 26711 字节 | `216087b79b8a9ba882157991b263c96799a068b37526908c82a5ab98b03095d7` |

## 已知限制

- 运行中的插件无法热更新/热卸载：宿主用 `Assembly.LoadFrom` 加载 DLL，文件在进程退出前被锁定。市场会拦截并给出重启指引。
- 没有回滚、评分、评论、签名验证；索引由仓库维护者审核。SHA-256 只保证字节一致，不保证来源可信。
- 镜像由第三方提供，可用性不受本项目控制。
- 只支持 PCL Harness 3.7.3+；旧版没有 `plugins` 权限与管理 API。
