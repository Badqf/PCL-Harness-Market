# 插件市场 0.1.8

这一版收录首个第三方插件「AI 助手」，并修正 API 版本门控在 3.8.3 上的误判。

## 新收录

- **AI 助手（`aichat`）** v0.1.0，作者 XYM：在「启动」页与 AI 聊天，可调用工具查看实例、插件、模组与日志；支持 DeepSeek / OpenAI 兼容接口 / Ollama，需要 PCL Harness 3.8.3+（API v7）。
- 索引条目包含完整摘要、描述、标签、SHA-256 与 GitHub Release 下载地址，可直接在市场内安装。

## 修复

- 0.1.7 及更早版本把「当前宿主 API 版本」写死为 6，导致 3.8.3（API v7）上的 AI 助手被误判为「需要 API v7」而无法安装。
- 0.1.8 改为运行时反射读取 `PCL.ModPlugin.ApiVersionUi`：3.8.3 得到 7，旧宿主得到 6，缺少字段时回退 6。因此市场自身仍保持 `minApiVersion: 6`，可在 3.7.0+ 运行，同时正确识别 API v7 插件。

## 验证

- 3.8.3 实机：从市场安装 AI 助手 → 下载 → SHA-256 校验 → 导入成功 → 「插件」页确认权限风险 → 启用并加载，日志显示 `AI 助手已加载：模型 deepseek-chat`，「启动」页出现聊天卡片。
- 3.7.4 上 API v7 插件仍会正确显示不兼容。

## 兼容性

- `minApiVersion` 仍为 6；下载结果分类、回滚、签名状态、待生效队列与自更新能力与 0.1.7 一致。
- 旧宿主自动隐藏签名状态并回退到旧的 `HttpDownload`。

## 安装

从本 Release 下载 `pcl-harness-market-0.1.8.pclplugin`，在 PCL Harness 的「插件」页导入；或让已安装的 0.1.7 通过「插件市场」自更新。

| 项目 | 值 |
| --- | --- |
| 文件 | `pcl-harness-market-0.1.8.pclplugin` |
| 大小 | 34240 字节 |
| SHA-256 | `6a0a6311a13d3d57c953716ce9dd0f58625f81c3849aef746035e17bf2a86eae` |
