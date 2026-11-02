# 示例插件：你好，市场

市场索引里 `hello-market` 条目的源码。它只写一条日志并弹一个提示，用来演示：

- `plugin.json` 的最小字段；
- 不带任何权限的插件也能正常加载；
- 打包后的 `.pclplugin` 如何被索引引用、下载、校验与安装。

## 构建与打包

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Project .\samples\hello-market
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -Project .\samples\hello-market `
  -Output .\index\packages\hello-market-1.0.0.pclplugin
```

仓库已经提交了打好包的 `index/packages/hello-market-1.0.0.pclplugin`，索引条目里的
`latest.package` 会在生成 `index.json` 时自动计算大小与 SHA-256。

> 重新打包会改变 zip 内的时间戳，从而改变 SHA-256；提交新包后必须重新运行
> `tools\make-index.ps1`。
