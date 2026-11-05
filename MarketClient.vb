Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Net
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading

Namespace PCLHarness.Market

    ''' <summary>一次索引读取的结果。</summary>
    Friend Class MarketFetchResult
        Public Property Index As MarketIndex
        Public Property FetchedAt As DateTime = DateTime.MinValue
        Public Property FromCache As Boolean
        Public Property SourceUrl As String = ""
        Public Property Warnings As New List(Of String)
    End Class

    ''' <summary>一次安装的结果。</summary>
    Friend Class MarketInstallResult
        Public Property Import As PCL.PluginImportResult
        Public Property PackagePath As String = ""
    End Class

    ''' <summary>索引读取、镜像回退、下载校验与安装。</summary>
    Friend Class MarketClient

        Public Const DefaultIndexUrl As String = "https://raw.githubusercontent.com/Badqf/PCL-Harness-Market/main/index.json"
        Private Const FreshMinutes As Integer = 10
        Private Const MirrorHostFast As String = "https://ghfast.top/"
        Private Const MirrorHostProxy As String = "https://gh-proxy.com/"

        Private ReadOnly Host As PCL.PluginHost
        Private ReadOnly CacheDir As String
        Private ReadOnly CacheFile As String
        Private ReadOnly MetaFile As String
        Private ReadOnly PackageDir As String
        Private ReadOnly InstallGate As New Object()

        Public Sub New(Host As PCL.PluginHost)
            Me.Host = Host
            CacheDir = Path.Combine(Host.DataFolder, "cache")
            ' 文件名刻意不用 .json：本体会监视插件目录下的所有 *.json 并触发热重载，
            ' 市场自己写缓存不应导致整个插件列表被重载。
            CacheFile = Path.Combine(CacheDir, "index.cache")
            MetaFile = Path.Combine(CacheDir, "index.meta")
            PackageDir = Path.Combine(Host.DataFolder, "packages")
        End Sub

        ''' <summary>读取本地缓存；没有或损坏时返回 Nothing。</summary>
        Public Function ReadCached() As MarketFetchResult
            Try
                If Not File.Exists(CacheFile) Then Return Nothing
                Dim Raw = File.ReadAllText(CacheFile, Encoding.UTF8)
                Dim Index = MarketIndex.Parse(Raw)
                Dim Result As New MarketFetchResult With {
                    .Index = Index,
                    .FromCache = True,
                    .SourceUrl = ReadMetaUrl()
                }
                Dim Stamped = ReadMetaTime()
                If Stamped.HasValue Then Result.FetchedAt = Stamped.Value
                Return Result
            Catch
                Return Nothing
            End Try
        End Function

        ''' <summary>联网读取索引，失败时回退到本地缓存。</summary>
        Public Function Fetch(IndexUrl As String, Force As Boolean, PreferMirror As Boolean, Ctx As PCL.PluginTaskContext) As MarketFetchResult
            If Not Uri.IsWellFormedUriString(IndexUrl, UriKind.Absolute) Then
                Throw New MarketException("索引地址不是有效的 URL：" & IndexUrl)
            End If
            Dim Cached = ReadCached()
            If Not Force AndAlso Cached IsNot Nothing AndAlso Cached.FetchedAt <> DateTime.MinValue AndAlso
               (DateTime.UtcNow - Cached.FetchedAt.ToUniversalTime()).TotalMinutes < FreshMinutes Then
                Return Cached
            End If

            Dim Errors As New List(Of String)
            Dim Candidates = BuildCandidates(IndexUrl, PreferMirror)
            For I = 0 To Candidates.Count - 1
                Dim Url = Candidates(I)
                Ctx.ThrowIfCancelled()
                Ctx.Report(0.1 + 0.7 * I / Math.Max(1, Candidates.Count), "正在读取索引：" & HostOf(Url))
                Try
                    Dim Raw = Host.HttpGetString(Url)
                    Dim Index = MarketIndex.Parse(Raw)
                    SaveCache(Raw, Url)
                    Return New MarketFetchResult With {
                        .Index = Index,
                        .FetchedAt = DateTime.UtcNow,
                        .FromCache = False,
                        .SourceUrl = Url
                    }
                Catch ex As MarketException
                    Errors.Add(HostOf(Url) & "：" & ex.Message)
                Catch ex As Exception
                    Errors.Add(HostOf(Url) & "：" & Friendly(ex))
                End Try
            Next

            If Cached IsNot Nothing Then
                Cached.Warnings.Insert(0, "网络不可用，显示的是本地缓存。")
                If Errors.Count > 0 Then Cached.Warnings.Add("连接失败：" & String.Join("；", Errors.ToArray()))
                Return Cached
            End If
            Throw New MarketException("无法获取插件索引。" & vbCrLf & String.Join(vbCrLf, Errors.ToArray()))
        End Function

        Public Function FetchSelfUpdate(IndexUrl As String, PreferMirror As Boolean, Ctx As PCL.PluginTaskContext) As MarketRelease
            If String.IsNullOrWhiteSpace(IndexUrl) Then Return Nothing
            Dim Base As Uri = Nothing
            If Not Uri.TryCreate(IndexUrl, UriKind.Absolute, Base) Then Return Nothing
            Dim Target As Uri = Nothing
            If Not Uri.TryCreate(Base, "update.json", Target) Then Return Nothing
            For Each Url In BuildCandidates(Target.AbsoluteUri, PreferMirror)
                Ctx.ThrowIfCancelled()
                Try
                    Dim Raw = Host.HttpGetString(Url)
                    Dim Dict = MarketJson.AsDict(MarketJson.DeserializeObject(Raw))
                    Dim Release = MarketRelease.Parse(Dict)
                    If Release IsNot Nothing AndAlso Release.HasHash AndAlso Release.HasDownload Then Return Release
                Catch
                End Try
            Next
            Return Nothing
        End Function

        ''' <summary>下载并安装一个版本；返回底层导入结果。</summary>
        Public Function Install(Entry As MarketPluginEntry, Release As MarketRelease, IndexUrl As String, PreferMirror As Boolean, Ctx As PCL.PluginTaskContext, Optional Progress As Action(Of Double) = Nothing) As MarketInstallResult
            If Entry Is Nothing OrElse Release Is Nothing Then Throw New MarketException("插件信息不完整")
            If Not Release.HasHash Then Throw New MarketException("索引没有提供这个版本的 SHA-256，按安全策略不能安装。")
            If Not Release.HasDownload Then Throw New MarketException("索引里的下载地址无效。")

            SyncLock InstallGate
                Dim PackagePath = Path.Combine(PackageDir, SafeFileName(Entry.Id & "-" & Release.Version) & ".pclplugin")
                Dim TmpPath = PackagePath & ".part"
                Directory.CreateDirectory(PackageDir)

                If File.Exists(PackagePath) AndAlso HashMatches(PackagePath, Release.Sha256) Then
                    Ctx.Report(0.55, "使用已下载的安装包")
                Else
                    Dim Errors As New List(Of String)
                    Dim Downloaded = False
                    Dim Candidates = BuildCandidates(Release.Download, PreferMirror)
                    For I = 0 To Candidates.Count - 1
                        Dim Url = Candidates(I)
                        Ctx.ThrowIfCancelled()
                        Try
                            If File.Exists(TmpPath) Then File.Delete(TmpPath)
                            Ctx.Report(0.15 + 0.4 * I / Math.Max(1, Candidates.Count), "正在下载：" & HostOf(Url))
                            DownloadWithProgress(Url, TmpPath, Release.Size, Progress, Ctx)
                            If Not File.Exists(TmpPath) Then Throw New MarketException("下载失败")
                            Ctx.Report(0.6, "正在校验 SHA-256…")
                            If Progress IsNot Nothing Then Progress(0.6)
                            Dim Actual = ComputeSha256(TmpPath)
                            If Not String.Equals(Actual, Release.Sha256, StringComparison.OrdinalIgnoreCase) Then
                                Throw New MarketException("SHA-256 不匹配（期望 " & MarketText.ShortHash(Release.Sha256) & "，实际 " & MarketText.ShortHash(Actual) & "）")
                            End If
                            If File.Exists(PackagePath) Then File.Delete(PackagePath)
                            File.Move(TmpPath, PackagePath)
                            If Progress IsNot Nothing Then Progress(0.82)
                            Downloaded = True
                            Exit For
                        Catch ex As MarketException
                            Errors.Add(HostOf(Url) & "：" & ex.Message)
                        Catch ex As Exception
                            Errors.Add(HostOf(Url) & "：" & Friendly(ex))
                        End Try
                    Next
                    If Not Downloaded Then
                        Throw New MarketException("安装包下载失败。" & vbCrLf & String.Join(vbCrLf, Errors.ToArray()))
                    End If
                End If

                Ctx.ThrowIfCancelled()
                Ctx.Report(0.85, "正在安装 " & Entry.DisplayName & " v" & Release.Version & "…")
                If Progress IsNot Nothing Then Progress(0.85)
                Dim Import As PCL.PluginImportResult = Nothing
                Try
                    Import = Host.Plugins.ImportPackage(PackagePath, True)
                Catch ex As Exception
                    Throw New MarketException("安装时出错：" & Friendly(ex))
                End Try
                If Import Is Nothing OrElse Not Import.Success Then
                    Throw New MarketException("安装失败：" & DescribeImport(Import))
                End If
                If Progress IsNot Nothing Then Progress(1.0)
                Return New MarketInstallResult With {.Import = Import, .PackagePath = PackagePath}
            End SyncLock
        End Function

        Public Function Uninstall(Id As String) As Boolean
            Return Host.Plugins.Uninstall(Id)
        End Function

        Public Function SetEnabled(Id As String, Value As Boolean) As Boolean
            Return Host.Plugins.SetEnabled(Id, Value)
        End Function

        Public Function CanRollback(Id As String) As Boolean
            Return HostCompat.CanRollback(Host, Id)
        End Function

        Public Function Rollback(Id As String) As Boolean
            Return HostCompat.Rollback(Host, Id)
        End Function

        Public Function ListInstalled() As List(Of PCL.PluginSummary)
            Try
                Return Host.Plugins.List()
            Catch
                Return New List(Of PCL.PluginSummary)()
            End Try
        End Function

        Public Function FindInstalled(Id As String) As PCL.PluginSummary
            Try
                Return Host.Plugins.Find(Id)
            Catch
                Return Nothing
            End Try
        End Function

        Private Shared Function BuildCandidates(Original As String, PreferMirror As Boolean) As List(Of String)
            Dim Result As New List(Of String)
            If Not IsMirrorable(Original) Then
                Result.Add(Original)
                Return Result
            End If
            Dim Fast = MirrorHostFast & Original
            Dim Proxy = MirrorHostProxy & Original
            If PreferMirror Then
                Result.Add(Fast)
                Result.Add(Proxy)
                Result.Add(Original)
            Else
                Result.Add(Original)
                Result.Add(Fast)
                Result.Add(Proxy)
            End If
            Return Result
        End Function

        Private Shared Function IsMirrorable(Url As String) As Boolean
            Dim UriValue As Uri = Nothing
            If Not Uri.TryCreate(Url, UriKind.Absolute, UriValue) Then Return False
            If UriValue.Scheme <> Uri.UriSchemeHttps AndAlso UriValue.Scheme <> Uri.UriSchemeHttp Then Return False
            Dim HostName = UriValue.Host.ToLowerInvariant()
            If HostName = "localhost" OrElse HostName = "127.0.0.1" OrElse HostName = "::1" Then Return False
            If HostName = "ghfast.top" OrElse HostName = "gh-proxy.com" Then Return False
            Return True
        End Function

        Public Shared Function HostOf(Url As String) As String
            Dim UriValue As Uri = Nothing
            If Uri.TryCreate(Url, UriKind.Absolute, UriValue) Then Return UriValue.Host
            Return Url
        End Function

        Private Sub DownloadWithProgress(Url As String, Target As String, ExpectedSize As Long, Progress As Action(Of Double), Ctx As PCL.PluginTaskContext)
            Dim Failure As Exception = Nothing
            Dim Worker As New Thread(Sub()
                                         Try
                                             Dim Outcome = HostCompat.Download(Host, Url, Target)
                                             If Not Outcome.Ok Then
                                                 Failure = New MarketException(If(String.IsNullOrWhiteSpace(Outcome.Message), "下载失败", Outcome.Message))
                                             End If
                                         Catch Ex As Exception
                                             Failure = Ex
                                         End Try
                                     End Sub)
            Worker.IsBackground = True
            Worker.Start()
            While Worker.IsAlive
                Dim Fraction As Double = 0
                Try
                    If File.Exists(Target) AndAlso ExpectedSize > 0 Then
                        Fraction = Math.Min(1.0, CDbl(New FileInfo(Target).Length) / CDbl(ExpectedSize))
                    End If
                Catch
                End Try
                If Progress IsNot Nothing Then Progress(0.15 + 0.45 * Fraction)
                Ctx.Report(0.15 + 0.45 * Fraction, "正在下载 " & CInt(Fraction * 100) & "%…")
                Worker.Join(150)
            End While
            Worker.Join()
            Ctx.ThrowIfCancelled()
            If Failure IsNot Nothing Then Throw Failure
        End Sub

        Private Shared Function Friendly(Ex As Exception) As String
            If TypeOf Ex Is UnauthorizedAccessException Then Return "没有网络权限：" & Ex.Message
            If TypeOf Ex Is WebException Then
                Dim Web = CType(Ex, WebException)
                If Web.Status = WebExceptionStatus.Timeout Then Return "连接超时"
                If Web.Response IsNot Nothing Then Return "HTTP 错误（" & CStr(CInt(DirectCast(Web.Response, HttpWebResponse).StatusCode)) & "）"
                Return "网络错误：" & Ex.Message
            End If
            Return Ex.Message
        End Function

        Public Shared Function DescribeImport(Result As PCL.PluginImportResult) As String
            If Result Is Nothing Then Return "没有返回结果"
            Dim Code = If(Result.Code, "")
            Dim Message = If(Result.Message, "")
            If Code.Length = 0 Then Return If(Message.Length > 0, Message, "未知错误")
            Select Case Code
                Case "ERR_NOT_ARCHIVE" : Return "文件不是有效的插件包"
                Case "ERR_NO_MANIFEST" : Return "包里没有 plugin.json"
                Case "ERR_AMBIGUOUS_MANIFEST" : Return "包里有多个 plugin.json"
                Case "ERR_BAD_JSON" : Return "plugin.json 不是合法 JSON"
                Case "ERR_BAD_ID" : Return "插件 id 不合法"
                Case "ERR_MIN_API" : Return "需要更新的启动器版本"
                Case "ERR_SHA256" : Return "包内 DLL 的 SHA-256 校验失败"
                Case "ERR_MISSING_DEP" : Return "缺少依赖插件"
                Case "ERR_EXISTS" : Return "插件已存在（未允许覆盖）"
                Case "ERR_UNSAFE_PATH" : Return "包内包含不安全的路径"
                Case "ERR_WRITE_FAILED" : Return "写入插件目录失败（旧版本可能正在运行，请禁用后重启启动器再更新）"
            End Select
            If Message.Length > 0 Then Return Message & "（" & Code & "）"
            Return Code
        End Function

        Private Shared Function ComputeSha256(FilePath As String) As String
            Using Sha = SHA256.Create()
                Using Stream = File.OpenRead(FilePath)
                    Return BitConverter.ToString(Sha.ComputeHash(Stream)).Replace("-", "").ToLowerInvariant()
                End Using
            End Using
        End Function

        Private Shared Function HashMatches(FilePath As String, Expected As String) As Boolean
            Try
                Return String.Equals(ComputeSha256(FilePath), Expected, StringComparison.OrdinalIgnoreCase)
            Catch
                Return False
            End Try
        End Function

        Private Shared Function SafeFileName(Value As String) As String
            Return Regex.Replace(Value, "[^0-9A-Za-z_\-\.]", "_")
        End Function

        Private Sub SaveCache(Raw As String, SourceUrl As String)
            Try
                Directory.CreateDirectory(CacheDir)
                Dim Tmp = CacheFile & ".tmp"
                File.WriteAllText(Tmp, Raw, New UTF8Encoding(False))
                If File.Exists(CacheFile) Then File.Delete(CacheFile)
                File.Move(Tmp, CacheFile)
                Dim Meta As New Dictionary(Of String, Object) From {
                    {"url", SourceUrl},
                    {"fetchedAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)}
                }
                File.WriteAllText(MetaFile, MarketJson.Serialize(Meta), New UTF8Encoding(False))
            Catch ex As Exception
                Host.LogWarn("写入索引缓存失败：" & ex.Message)
            End Try
        End Sub

        Private Function ReadMetaUrl() As String
            Try
                Dim Dict = MarketJson.AsDict(MarketJson.DeserializeObject(File.ReadAllText(MetaFile, Encoding.UTF8)))
                Return MarketJson.AsString(Dict, "url")
            Catch
                Return ""
            End Try
        End Function

        Private Function ReadMetaTime() As DateTime?
            Try
                Dim Dict = MarketJson.AsDict(MarketJson.DeserializeObject(File.ReadAllText(MetaFile, Encoding.UTF8)))
                Dim Text = MarketJson.AsString(Dict, "fetchedAtUtc")
                Dim Parsed As DateTime
                If DateTime.TryParse(Text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, Parsed) Then
                    Return Parsed
                End If
            Catch
            End Try
            Return Nothing
        End Function

    End Class

End Namespace
