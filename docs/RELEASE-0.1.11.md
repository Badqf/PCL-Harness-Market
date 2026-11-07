# 插件市场 0.1.11

这一版让卸载提示与新宿主的行为一致：卸载插件时，插件目录会直接进入 Windows 回收站，而不是启动器内部的 `Plugins\.trash`。

## 变更

- 卸载确认文案改为「插件目录会被移到系统回收站（旧版启动器为 Plugins\.trash），data 目录与设置保留。」
- 新宿主（PCL Harness 3.9.1+）会把整个插件目录移入 Windows 回收站，可从回收站还原；旧宿主仍回退到 `Plugins\.trash`。
- 卸载后仍会清除该插件的权限授予记录，重装后需要重新确认权限。

## 兼容性

- `minApiVersion` 仍为 6；实际回收站行为由宿主提供，市场只负责提示。
- 旧宿主没有回收站实现时，提示里已注明 `Plugins\.trash` 回退。

## 安装

从本 Release 下载 `pcl-harness-market-0.1.11.pclplugin`，在 PCL Harness 的「插件」页导入；或让已安装的 0.1.10 通过「插件市场」自更新。

| 项目 | 值 |
| --- | --- |
| 文件 | `pcl-harness-market-0.1.11.pclplugin` |
| 大小 | 34459 字节 |
| SHA-256 | `420f92c47498881ccc38ce166b5772726049976dec9c4ed59dc933104f0c8c7d` |
