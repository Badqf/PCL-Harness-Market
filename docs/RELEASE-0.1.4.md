# 插件市场 0.1.4

面向 PCL Harness 3.7.4+（插件 API v6）的更新版本。0.1.3 未发布，本版本包含其全部改动。

## 这个版本做了什么

- **待生效队列**：更新正在运行的插件时不再报「文件被占用」。宿主把新版本放进 `Plugins\.pending\<id>`，市场显示「更新已就绪，重启后生效」；需要 3.7.4 的排队更新支持。
- **市场自更新**：市场从索引同级的 `update.json` 检查新版本，出现更新卡；自更新同样进入待生效队列，避免覆盖正在加载的 DLL。
- **快速重启**：存在待生效更新时才显示「立即重启」，确认后调用宿主 `RestartLauncher()`；没有更新时不占位置。
- **下载进度**：安装或更新时在卡片右侧显示进度条，覆盖下载、SHA-256 校验、导入三个阶段。
- **下载量（一期）**：索引新增可选 `downloads.total`，卡片显示「下载 N」。新增 `tools/update-downloads.ps1` 与每日 GitHub Action，从 Release 资产的 `download_count` 汇总并重新生成索引；缺失字段不显示，不伪造 0。
- **兼容 3.7.3**：没有 `NeedRestart` 时按 `Message = NEED_RESTART` 兜底；旧版仍走「禁用后重启再更新」的提示。

## 安装

1. 下载本 Release 的 `pcl-harness-market-0.1.4.pclplugin`。
2. 打开启动器 →「插件」→「导入插件…」，选择该文件。
3. 导入后插件默认未启用，在插件列表里启用「插件市场」。
4. 标题栏出现「插件市场」入口；首次拉取 GitHub 索引时按提示允许网络访问。

已经安装 0.1.2 的用户：可直接用 0.1.4 包覆盖导入；若 0.1.2 正在运行且宿主为 3.7.4，会进入待生效队列，重启后生效。

## 发布资产

| 资产 | 大小 | SHA-256 |
| --- | ---: | --- |
| `pcl-harness-market-0.1.4.pclplugin` | 32152 字节 | `c39744088528bec8e474dc3eaf2959f1cb44b52dba319277016adb633bc8b9d2` |

## 宿主前置修复

3.7.4 的 `StagePendingUpdate` 已实现但未接入 `InstallFromFolder`，导致排队更新永远不会触发。本版本验证所用的宿主补丁在 `InstallFromFolder` 中、`SetEnabled` 之前插入：

```vb
If Existing IsNot Nothing AndAlso Existing.Loaded Then
    Return StagePendingUpdate(TempRoot, Info, Existing)
End If
```

该补丁已提交到 `F:\PCL2-Source`，需要随 3.7.5 或补丁版启动器发布；没有此补丁时，运行中插件更新仍会返回 `ERR_WRITE_FAILED`。

## 已知限制

- 下载量为 Release 资产累计值，不含仓库直链与镜像流量；新插件需要把包挂到 Release 才会被统计。
- 没有回滚、评分、评论、签名验证；SHA-256 只保证字节一致，不保证来源可信。
- 镜像由第三方提供，可用性不受本项目控制。
- 进度条为分段估算（下载 15%–60%、校验、导入），不是逐字节实时进度。
