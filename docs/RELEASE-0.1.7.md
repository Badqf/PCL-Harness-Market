# 插件市场 0.1.7

这一版修正 0.1.6 在旧宿主上的一个签名显示问题，并保持与之完全相同的功能与兼容范围。

## 修复

- 0.1.6 在缺少 `PluginSummary.SignatureStatus` 的旧宿主上，会把「宿主未提供签名状态」显示成「未签名」。0.1.7 改为隐藏该项，只在宿主提供状态时展示「未签名 / 已签名 / 签名无效 / 未知发布者」。
- 3.8.3 与 3.7.4 的行为不变：宿主提供 `SignatureStatus` 时照常显示。

## 兼容性

- `minApiVersion` 仍为 6，适配 PCL Harness 3.7.0+；新 API 继续通过反射探测并按宿主能力降级。
- 0.1.6 已有的下载结果分类、回滚、签名状态、待生效队列与自更新能力全部保留。

## 安装

从本 Release 下载 `pcl-harness-market-0.1.7.pclplugin`，在 PCL Harness 的「插件」页导入；或让已安装的 0.1.6 通过「插件市场」自更新。

| 项目 | 值 |
| --- | --- |
| 文件 | `pcl-harness-market-0.1.7.pclplugin` |
| 大小 | 34114 字节 |
| SHA-256 | `454b581890bede02056605414fdeadb91e39fbca8af7df4ee0a7aa9f4cd9553e` |