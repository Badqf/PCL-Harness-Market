# 市场索引格式（schema 1）

插件市场拉取一个 JSON 索引，按其中的条目展示、下载和安装插件。本仓库根目录的
[`index.json`](../index.json) 就是默认索引；插件作者在 `index/plugins/` 下提交条目，
维护者运行脚本生成根索引。

默认地址：

```
https://raw.githubusercontent.com/Badqf/PCL-Harness-Market/main/index.json
```

客户端会忽略无法识别的字段，因此向后兼容地增加字段是安全的；但删除或改变已有字段的
语义需要提升 `schema`。

## 顶层结构

```json
{
  "schema": 1,
  "generatedAt": "2026-11-02T05:59:55Z",
  "plugins": []
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `schema` | integer | 是 | 当前固定为 `1`。客户端遇到未知版本会拒绝解析并提示更新。 |
| `generatedAt` | string | 是 | ISO 8601 UTC 时间，生成索引的时间，仅用于展示和缓存判断。 |
| `plugins` | array | 是 | 插件条目数组，可以为空。空索引会显示「索引里还没有插件」。 |

## 插件条目

```json
{
  "id": "example-plugin",
  "name": "示例插件",
  "summary": "一句话说明这个插件做什么。",
  "description": "更完整的介绍，可包含换行。",
  "author": "Badqf",
  "homepage": "https://github.com/Badqf/PCL-Harness-Market",
  "license": "PCL 分发有限许可",
  "tags": ["工具", "示例"],
  "featured": false,
  "latest": {
    "version": "1.2.0",
    "minApiVersion": 6,
    "publishedAt": "2026-11-02T00:00:00Z",
    "size": 12345,
    "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
    "download": "https://github.com/Badqf/Example-Plugin/releases/download/v1.2.0/example-plugin-1.2.0.pclplugin",
    "notes": "本版本修复了……"
  }
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `id` | string | 是 | 与插件 `plugin.json` 的 `id` 完全一致，`^[a-z0-9][a-z0-9_.-]{0,63}$`，全索引唯一。 |
| `name` | string | 是 | 展示名称，建议与 `plugin.json` 的 `name` 一致。 |
| `summary` | string | 否 | 列表页的一句话摘要。 |
| `description` | string | 否 | 详情页正文，可多行。 |
| `author` | string | 否 | 作者署名。 |
| `homepage` | string | 否 | 项目主页或仓库地址。 |
| `license` | string | 否 | 许可证名称，例如「PCL 分发有限许可」。 |
| `tags` | string[] | 否 | 搜索标签，建议 1–5 个。 |
| `featured` | boolean | 否 | 是否精选；精选条目排在列表前面。 |
| `latest` | object | 是 | 当前推荐版本。 |

### `latest`

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `version` | string | 是 | 点分版本号，可带预发布后缀，如 `1.2.0`、`1.2.0-beta.1`。 |
| `minApiVersion` | integer | 是 | 插件要求的最低 Host API 版本，必须与包内 `plugin.json` 一致。 |
| `publishedAt` | string | 否 | 发布时间，ISO 8601；缺省时生成脚本使用条目文件的修改时间。 |
| `size` | integer | 是 | 安装包字节数，必须与实际文件一致。 |
| `sha256` | string | 是 | 安装包的 SHA-256（64 位小写十六进制）。客户端**不会安装没有摘要的包**。 |
| `download` | string | 是 | 安装包的 http(s) 绝对地址。推荐使用 GitHub Release 的稳定下载地址。 |
| `notes` | string | 否 | 本版本更新说明，展示在详情里。 |

> 生成脚本额外支持 `latest.package`：填一个相对于条目文件的本地 `.pclplugin` 路径，
> 脚本会自动计算并校验 `size` / `sha256`，生成根索引时会移除这个字段。`index.json`
> 本身不接受 `package` 字段。

## 生成与校验

```powershell
# 校验 index/plugins/*.json 并生成根 index.json
powershell -ExecutionPolicy Bypass -File tools\make-index.ps1

# 只校验（CI 用）
powershell -ExecutionPolicy Bypass -File tools\make-index.ps1 -Check
```

`index/plugins/` 下每个 `<id>.json` 是一个完整条目；生成脚本按 `featured` 优先、
`id` 升序排序，写入根 `index.json`（UTF-8，无 BOM）。

## 兼容与安全

- 客户端只接受 `schema: 1`；解析失败时会回退到本地缓存并展示警告。
- 条目缺少 `sha256`、`download` 或版本号非法时，该条目仍会显示，但安装按钮会被禁用并说明原因。
- 摘要只保证「下载到的字节与索引声明一致」，**不提供签名或作者身份验证**。启用插件等于运行它的代码，请只启用信任来源。
- `minApiVersion` 高于当前启动器 API 版本时，客户端会拒绝安装并提示升级启动器。
