# 发布插件到市场

本文面向插件作者：如何把插件发布到本市场索引，以及更新版本时要做的事。
市场只托管**索引元数据**，安装包仍放在你自己的仓库 Release 里。

## 1. 准备插件包

插件必须是普通 PCL Harness 插件：一个 `.pclplugin`（本质是 zip），根目录（或唯一
一层子目录）包含 `plugin.json` 与清单声明的 DLL。

`plugin.json` 至少需要：

```json
{
  "id": "example-plugin",
  "name": "示例插件",
  "version": "1.2.0",
  "author": "你的名字",
  "entry": "Example.Plugin",
  "dll": "Example.Plugin.dll",
  "minApiVersion": 6,
  "permissions": []
}
```

要求：

- `id` 符合 `^[a-z0-9][a-z0-9_.-]{0,63}$`，全局唯一，**发布后不要改名**；
- `minApiVersion` 不高于你实际测试过的 Host API；市场当前面向 API v6（启动器 3.7.3+）；
- 需要管理其他插件时声明 `plugins`；需要访问固定主机时声明 `net.hosts`；
  需要用户自定义主机时声明 `net.dynamic`；
- 不要在对象式 `permissions` 里写 `"files": true`，宿主会把它误读成字面路径；
  写到插件自己的 data 目录本来就不需要 `files` 权限；
- 单包建议不超过 64 MB，条目数不超过 2000。

编译与打包可参考本仓库的 `build.ps1` / `pack.ps1`，或使用官方 SDK 的
`tools\sdk\build.ps1 -Pack`。

## 2. 发布 Release

1. 在插件仓库打 tag（推荐 `v1.2.0`）。
2. 上传 `.pclplugin` 作为 Release 资产，文件名保持
   `<id>-<version>.pclplugin`。
3. 复制稳定下载地址，例如：

   ```
   https://github.com/you/example-plugin/releases/download/v1.2.0/example-plugin-1.2.0.pclplugin
   ```

4. 计算 SHA-256 与字节数：

   ```powershell
   Get-FileHash .\example-plugin-1.2.0.pclplugin -Algorithm SHA256
   (Get-Item .\example-plugin-1.2.0.pclplugin).Length
   ```

> 不要使用 `latest/download` 这类会漂移的地址；索引记录的是不可变版本。
> 中国大陆网络可自行加 `ghfast.top` / `gh-proxy.com` 前缀，市场也会自动尝试镜像。

## 3. 添加索引条目

在本仓库新建 `index/plugins/<id>.json`：

```json
{
  "id": "example-plugin",
  "name": "示例插件",
  "summary": "一句话说明这个插件做什么。",
  "description": "更完整的介绍，可包含换行。",
  "author": "你的名字",
  "homepage": "https://github.com/you/example-plugin",
  "license": "PCL 分发有限许可",
  "tags": ["工具"],
  "featured": false,
  "latest": {
    "version": "1.2.0",
    "minApiVersion": 6,
    "publishedAt": "2026-11-02T00:00:00Z",
    "size": 12345,
    "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
    "download": "https://github.com/you/example-plugin/releases/download/v1.2.0/example-plugin-1.2.0.pclplugin",
    "notes": "首个公开版本。"
  }
}
```

如果安装包也放在本仓库（不推荐，仓库会变大），可以把包放到 `index/packages/`，
用相对路径让脚本自动填摘要：

```json
"latest": {
  "version": "1.2.0",
  "minApiVersion": 6,
  "download": "https://raw.githubusercontent.com/Badqf/PCL-Harness-Market/main/index/packages/example-plugin-1.2.0.pclplugin",
  "package": "../packages/example-plugin-1.2.0.pclplugin"
}
```

运行：

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-index.ps1
```

脚本会校验字段、自动补全 `size` / `sha256`、按精选与 id 排序，并重写根 `index.json`。
提交时同时包含条目文件与生成的 `index.json`。

## 4. 提交 PR

PR 请包含：

- [ ] `index/plugins/<id>.json`
- [ ] 重新生成的 `index.json`
- [ ] 插件主页 / Release 链接
- [ ] 简短说明插件用途与所需权限

审核会检查：

1. `id` 与包里 `plugin.json` 一致且未与现有条目冲突；
2. `latest.version`、`minApiVersion`、`size`、`sha256` 与资产一致；
3. 下载地址可访问、指向不可变版本；
4. 名称/描述/标签没有误导，权限有合理解释；
5. 不包含恶意、破坏性或明显侵权的代码。

## 5. 更新版本

1. 发布新 Release，上传新包；
2. 修改 `index/plugins/<id>.json` 的 `latest`（版本、时间、大小、SHA-256、地址、notes）；
3. 重新运行 `tools\make-index.ps1`；
4. 提交 PR（标题如 `update: example-plugin 1.3.0`）。

用户端在「插件市场」点刷新即可看到「可更新」。**正在运行的插件不能热更新**：
市场会提示先禁用、关闭并重启启动器，再更新。

## 6. 下架

删除对应 `index/plugins/<id>.json`，重新生成 `index.json`，提交 PR。
已安装的用户不受影响；市场只是不再展示该条目。

## 7. 常见问题

**Q：索引里没有 SHA-256 会怎样？**
A：客户端会展示条目但禁用安装，并提示索引缺少摘要。摘要必填。

**Q：可以用 HTTP 吗？**
A：只接受 `http(s)` 绝对地址，但强烈建议 HTTPS；镜像与 raw 地址都应是 HTTPS。

**Q：镜像挂了怎么办？**
A：客户端按设置顺序回退；用户也可以关闭「优先使用镜像」直连。

**Q：可以放多个版本吗？**
A：当前 schema 只表达 `latest`。需要回滚能力时会在 schema 2 引入 `versions[]`。

**Q：插件需要读取任意路径文件怎么办？**
A：在 `permissions` 里声明 `files` 的对象形式（`read`/`write` 路径列表），不要写
`"files": true`。市场自身不申请 `files`。

## 8. 安全提醒

索引与安装包由不同仓库托管，但摘要写在索引里，**不是作者签名**。本市场依赖 HTTPS、
仓库审核与用户判断。启用任何插件前，请确认来源可信；插件以当前用户权限运行。
