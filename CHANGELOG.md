# 更新日志

## 0.1.0 — 2026-11-02

首个公开版本。

- 市场页面：搜索、仅看已安装、刷新、插件卡片、详情展开、索引状态与空态提示。
- 索引：schema 1，支持 `featured`、标签、更新说明；未知字段容错，缺摘要拒绝安装。
- 网络：固定索引地址可配置；`ghfast.top` / `gh-proxy.com` 镜像回退；断网回退缓存。
- 安装：强制 SHA-256 校验；安装完成默认不启用；更新保留 data 与设置。
- 管理：启用、禁用、卸载；`PluginSummary.Loaded` 时拦截并提示先禁用并重启启动器。
- 权限：`plugins`、`net.dynamic`；不申请 `files`，文件仅写自己的 data 目录。
- 工具：`tools/make-index.ps1` 校验并生成索引，`tools/dev-server.ps1` 本地预览。
- 索引缓存改用 `.cache` / `.meta` 后缀，避免写入 `.json` 触发起动器热重载。
