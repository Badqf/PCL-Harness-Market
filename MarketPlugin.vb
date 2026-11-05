Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Windows
Imports PCL

Namespace PCLHarness.Market

    ''' <summary>市场页看到的一份状态快照。</summary>
    Friend Class MarketState
        Public Property Index As MarketIndex
        Public Property FetchedAt As DateTime = DateTime.MinValue
        Public Property SourceUrl As String = ""
        Public Property FromCache As Boolean
        Public Property Warnings As New List(Of String)
        Public Property ErrorText As String = ""
        Public Property Loading As Boolean
    End Class

    ''' <summary>插件市场入口：注册页面、设置，负责后台读取索引与安装。</summary>
    Public Class MarketPlugin
        Inherits PclPluginBase
        Implements IPclPluginUi

        Private Const MarketIcon As String =
            "M3,4 L21,4 L21,9 L19,9 L19,21 L5,21 L5,9 L3,9 Z M7,9 L7,17 L17,17 L17,9 Z M9,4 L9,2 L15,2 L15,4"

        Private ReadOnly StateGate As New Object()
        Private ReadOnly FavoritesGate As New Object()
        Private ReadOnly _favorites As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private _state As New MarketState()
        Private _fetching As Boolean
        Private _unloaded As Boolean
        Private _pageInfo As PCL.PluginPageInfo
        Private ReadOnly ProgressGate As New Object()
        Private ReadOnly _progress As New Dictionary(Of String, Double)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly _installing As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private _selfUpdate As MarketRelease
        Private _selfProgress As Double
        Private _needRestart As Boolean

        Private ReadOnly Property FavoritesPath As String
            Get
                Return System.IO.Path.Combine(DataFolder, "favorites.txt")
            End Get
        End Property

        Friend Property Client As MarketClient

        Friend ReadOnly Property SelfUpdate As MarketRelease
            Get
                SyncLock StateGate
                    Return _selfUpdate
                End SyncLock
            End Get
        End Property

        Friend ReadOnly Property SelfProgress As Double
            Get
                SyncLock ProgressGate
                    Return _selfProgress
                End SyncLock
            End Get
        End Property

        Friend ReadOnly Property NeedRestart As Boolean
            Get
                SyncLock ProgressGate
                    Return _needRestart
                End SyncLock
            End Get
        End Property

        Friend Event ProgressChanged As EventHandler

        Friend Function IsInstalling(Id As String) As Boolean
            If String.IsNullOrEmpty(Id) Then Return False
            SyncLock ProgressGate
                Return _installing.Contains(Id)
            End SyncLock
        End Function

        Friend Function GetInstallProgress(Id As String) As Double
            SyncLock ProgressGate
                Dim Value As Double = 0
                If _progress.TryGetValue(Id, Value) Then Return Value
                Return 0
            End SyncLock
        End Function

        Public Overrides Sub OnLoad(Host As PCL.PluginHost)
            MyBase.OnLoad(Host)
            _unloaded = False
            Client = New MarketClient(Host)
            LoadFavorites()

            Dim Cached = Client.ReadCached()
            If Cached IsNot Nothing Then
                _state = New MarketState With {
                    .Index = Cached.Index,
                    .FetchedAt = Cached.FetchedAt,
                    .SourceUrl = Cached.SourceUrl,
                    .FromCache = True,
                    .Warnings = Cached.Warnings
                }
                DevLog("已载入索引缓存：" & Cached.Index.Plugins.Count & " 个插件")
            End If

            Host.Events.On("插件安装", New Action(Of String, String)(AddressOf OnPluginChanged))
            Host.Events.On("插件卸载", New Action(Of String, String)(AddressOf OnPluginChanged))

            If Host.GetSetting(Of Boolean)("autoRefresh", True) Then
                FetchAsync(False)
            End If
            CheckSelfUpdateAsync()
            DevLog("插件市场已加载")
        End Sub

        Public Overrides Sub OnUnload()
            _unloaded = True
            Try
                If Host IsNot Nothing Then
                    Host.Events.Off("插件安装", New Action(Of String, String)(AddressOf OnPluginChanged))
                    Host.Events.Off("插件卸载", New Action(Of String, String)(AddressOf OnPluginChanged))
                End If
            Catch
            End Try
            MyBase.OnUnload()
        End Sub

        Private Sub OnPluginChanged(Id As String, Version As String)
            If String.Equals(Id, Host.Info.Id, StringComparison.OrdinalIgnoreCase) Then Return
            NotifyChanged()
        End Sub

        ' ==================== 收藏 ====================

        Friend Function IsFavorite(Id As String) As Boolean
            If String.IsNullOrEmpty(Id) Then Return False
            SyncLock FavoritesGate
                Return _favorites.Contains(Id)
            End SyncLock
        End Function

        Friend Sub ToggleFavorite(Id As String)
            If String.IsNullOrEmpty(Id) Then Return
            SyncLock FavoritesGate
                If Not _favorites.Remove(Id) Then _favorites.Add(Id)
            End SyncLock
            SaveFavorites()
            NotifyChanged()
        End Sub

        Private Sub LoadFavorites()
            Try
                If Not Host.FileExists(FavoritesPath) Then Return
                Dim Text = Host.ReadTextFile(FavoritesPath)
                If String.IsNullOrEmpty(Text) Then Return
                For Each Line In Text.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    Dim Value = Line.Trim()
                    If Value.Length > 0 Then
                        SyncLock FavoritesGate
                            _favorites.Add(Value)
                        End SyncLock
                    End If
                Next
            Catch Ex As Exception
                DevLog("读取收藏失败：" & Ex.Message)
            End Try
        End Sub

        Private Sub SaveFavorites()
            Try
                Dim Lines As String()
                SyncLock FavoritesGate
                    Lines = _favorites.OrderBy(Function(X) X, StringComparer.OrdinalIgnoreCase).ToArray()
                End SyncLock
                Host.WriteTextFile(FavoritesPath, String.Join(vbCrLf, Lines))
            Catch Ex As Exception
                DevLog("保存收藏失败：" & Ex.Message)
            End Try
        End Sub

        ' ==================== 页与设置 ====================

        Public Function GetPages() As IEnumerable(Of PCL.PluginPageInfo) Implements PCL.IPclPluginUi.GetPages
            Return New List(Of PCL.PluginPageInfo) From {EnsurePage()}
        End Function

        Public Function GetNavItems() As IEnumerable(Of PCL.PluginNavItem) Implements PCL.IPclPluginUi.GetNavItems
            Return New List(Of PCL.PluginNavItem) From {
                New PCL.PluginNavItem With {.Text = "插件市场", .Icon = MarketIcon, .Page = EnsurePage()}
            }
        End Function

        Public Function GetSettings() As IEnumerable(Of PCL.PluginSetting) Implements PCL.IPclPluginUi.GetSettings
            Return New List(Of PCL.PluginSetting) From {
                New PCL.PluginSetting With {
                    .Key = "indexUrl",
                    .Title = "索引地址",
                    .Description = "市场索引 JSON 的 URL。默认使用 GitHub 上的官方索引；也可以填自建镜像。",
                    .SettingType = PCL.PluginSettingType.Text,
                    .DefaultValue = MarketClient.DefaultIndexUrl,
                    .Handler = Sub(Value As Object) OnIndexUrlChanged(Value)
                },
                New PCL.PluginSetting With {
                    .Key = "preferMirror",
                    .Title = "优先使用镜像",
                    .Description = "先走 ghfast.top / gh-proxy.com 镜像，失败再直连 GitHub。国内网络建议开启。",
                    .SettingType = PCL.PluginSettingType.Boolean,
                    .DefaultValue = True
                },
                New PCL.PluginSetting With {
                    .Key = "autoRefresh",
                    .Title = "打开时自动刷新",
                    .Description = "进入插件市场时后台更新索引；关闭后只读本地缓存，需手动点「刷新」。",
                    .SettingType = PCL.PluginSettingType.Boolean,
                    .DefaultValue = True
                }
            }
        End Function

        Private Function EnsurePage() As PCL.PluginPageInfo
            If _pageInfo Is Nothing Then
                _pageInfo = New PCL.PluginPageInfo With {
                    .Id = "market",
                    .Title = "插件市场",
                    .Icon = MarketIcon,
                    .Factory = Function() New MarketPage(Me)
                }
            End If
            Return _pageInfo
        End Function

        Private Sub OnIndexUrlChanged(Value As Object)
            Dim Url = Convert.ToString(Value)
            If String.IsNullOrWhiteSpace(Url) Then
                Url = MarketClient.DefaultIndexUrl
                Try
                    Host.SetSetting(Of String)("indexUrl", Url)
                Catch
                End Try
            End If
            FetchAsync(True)
        End Sub

        ' ==================== 状态与事件 ====================

        Friend Function GetState() As MarketState
            SyncLock StateGate
                Return _state
            End SyncLock
        End Function

        Public Event Changed As EventHandler

        Friend Sub NotifyChanged()
            ' 注意：插件在安装/卸载时会被宿主热重载，旧实例的 _unloaded 会变 True。
            ' 但旧页面仍绑定旧实例，这里不能提前返回，否则页面永远停在“进行中”。
            Try
                Host.RunInUi(Sub()
                                 RaiseEvent Changed(Me, EventArgs.Empty)
                             End Sub)
            Catch
            End Try
        End Sub

        ' ==================== 读取索引 ====================

        Friend Sub FetchAsync(Force As Boolean)
            If Client Is Nothing Then Return
            If Not Force AndAlso Not Host.GetSetting(Of Boolean)("autoRefresh", True) Then Return

            SyncLock StateGate
                If _fetching Then Return
                _fetching = True
                _state = New MarketState With {
                    .Index = _state.Index,
                    .FetchedAt = _state.FetchedAt,
                    .SourceUrl = _state.SourceUrl,
                    .FromCache = _state.FromCache,
                    .Warnings = _state.Warnings,
                    .Loading = True
                }
            End SyncLock
            NotifyChanged()

            Dim Url = Host.GetSetting(Of String)("indexUrl", MarketClient.DefaultIndexUrl)
            Dim Prefer = Host.GetSetting(Of Boolean)("preferMirror", True)
            Try
                Host.Tasks.Run("读取插件市场索引", Sub(Ctx) FetchWorker(Url, Force, Prefer, Ctx))
            Catch ex As Exception
                SyncLock StateGate
                    _fetching = False
                End SyncLock
                Dim Old = _state
                _state = New MarketState With {
                    .Index = Old.Index,
                    .FetchedAt = Old.FetchedAt,
                    .SourceUrl = Old.SourceUrl,
                    .FromCache = Old.FromCache,
                    .Warnings = Old.Warnings,
                    .ErrorText = "无法启动后台任务：" & ex.Message
                }
                NotifyChanged()
            End Try
        End Sub

        Private Sub FetchWorker(Url As String, Force As Boolean, Prefer As Boolean, Ctx As PCL.PluginTaskContext)
            Dim Result As MarketFetchResult = Nothing
            Dim Failure As String = ""
            Try
                Result = Client.Fetch(Url, Force, Prefer, Ctx)
            Catch ex As MarketException
                Failure = ex.Message
            Catch ex As Exception
                If TypeOf ex Is OperationCanceledException Then
                    Failure = "已取消读取索引。"
                Else
                    Failure = "读取索引失败：" & ex.Message
                End If
            End Try

            SyncLock StateGate
                _fetching = False
                If Result IsNot Nothing Then
                    _state = New MarketState With {
                        .Index = Result.Index,
                        .FetchedAt = Result.FetchedAt,
                        .SourceUrl = Result.SourceUrl,
                        .FromCache = Result.FromCache,
                        .Warnings = Result.Warnings,
                        .Loading = False
                    }
                Else
                    Dim Old = _state
                    _state = New MarketState With {
                        .Index = Old.Index,
                        .FetchedAt = Old.FetchedAt,
                        .SourceUrl = Old.SourceUrl,
                        .FromCache = Old.FromCache,
                        .Warnings = Old.Warnings,
                        .ErrorText = Failure
                    }
                End If
            End SyncLock

            If Result IsNot Nothing Then
                Try
                    Host.LogInfo("插件索引已更新：" & Result.Index.Plugins.Count & " 个插件，来源 " & MarketClient.HostOf(Result.SourceUrl))
                Catch
                End Try
            End If
            NotifyChanged()
        End Sub

        ' ==================== 市场自更新 ====================

        Friend Sub CheckSelfUpdateAsync()
            If _unloaded Then Return
            Try
                Host.Tasks.Run("检查插件市场更新", Sub(Ctx) CheckSelfWorker(Ctx))
            Catch
            End Try
        End Sub

        Private Sub CheckSelfWorker(Ctx As PCL.PluginTaskContext)
            Dim Url = Host.GetSetting(Of String)("indexUrl", MarketClient.DefaultIndexUrl)
            Dim Prefer = Host.GetSetting(Of Boolean)("preferMirror", True)
            Dim Release As MarketRelease = Nothing
            Try
                Release = Client.FetchSelfUpdate(Url, Prefer, Ctx)
            Catch
                Return
            End Try
            If Release Is Nothing Then Return
            If MarketText.CompareVersion(Host.Info.Version, Release.Version) >= 0 Then Return
            SyncLock StateGate
                _selfUpdate = Release
            End SyncLock
            Try
                Host.LogInfo("发现插件市场新版本：" & Release.Version)
            Catch
            End Try
            NotifyChanged()
        End Sub

        Friend Sub UpdateSelfAsync(Done As Action(Of Boolean, String))
            Dim Release = SelfUpdate
            If Release Is Nothing Then Return
            Dim Entry As New MarketPluginEntry With {
                .Id = Host.Info.Id,
                .Name = Host.Info.Name,
                .Author = Host.Info.Author,
                .Latest = Release
            }
            InstallAsync(Entry, Release, Done)
        End Sub

        Private Sub SetInstalling(Id As String, Value As Boolean)
            If String.IsNullOrEmpty(Id) Then Return
            SyncLock ProgressGate
                If Value Then
                    _installing.Add(Id)
                    If String.Equals(Id, Host.Info.Id, StringComparison.OrdinalIgnoreCase) Then _selfProgress = 0
                Else
                    _installing.Remove(Id)
                    _progress.Remove(Id)
                End If
            End SyncLock
            RaiseProgress()
        End Sub

        Private Sub ReportProgress(Id As String, Value As Double)
            If String.IsNullOrEmpty(Id) Then Return
            SyncLock ProgressGate
                _progress(Id) = Math.Max(0, Math.Min(1, Value))
                _selfProgress = If(String.Equals(Id, Host.Info.Id, StringComparison.OrdinalIgnoreCase), _progress(Id), _selfProgress)
            End SyncLock
            RaiseProgress()
        End Sub

        Private Sub RaiseProgress()
            Try
                Host.RunInUi(Sub()
                                 Try
                                     RaiseEvent ProgressChanged(Me, EventArgs.Empty)
                                 Catch
                                 End Try
                             End Sub)
            Catch
            End Try
        End Sub

        Private Shared Function ImportNeedsRestart(Import As PCL.PluginImportResult) As Boolean
            If Import Is Nothing Then Return False
            Try
                Dim Prop = Import.GetType().GetProperty("NeedRestart")
                If Prop IsNot Nothing Then Return CBool(Prop.GetValue(Import, Nothing))
            Catch
            End Try
            Return String.Equals(Import.Message, "NEED_RESTART", StringComparison.OrdinalIgnoreCase)
        End Function

        ' ==================== 安装 / 卸载 / 启用 ====================

        Friend Sub InstallAsync(Entry As MarketPluginEntry, Release As MarketRelease, Done As Action(Of Boolean, String))
            If Entry Is Nothing OrElse Release Is Nothing Then Return
            Dim IsSelf = String.Equals(Entry.Id, Host.Info.Id, StringComparison.OrdinalIgnoreCase)
            Dim Url = Host.GetSetting(Of String)("indexUrl", MarketClient.DefaultIndexUrl)
            Dim Prefer = Host.GetSetting(Of Boolean)("preferMirror", True)
            Dim Existing = Client.FindInstalled(Entry.Id)
            SetInstalling(Entry.Id, True)
            Try
                Host.Tasks.Run("安装 " & Entry.DisplayName, Sub(Ctx)
                                                                   Dim Ok As Boolean = False
                                                                   Dim Message As String = ""
                                                                   Try
                                                                       Dim Result = Client.Install(Entry, Release, Url, Prefer, Ctx, Sub(Value) ReportProgress(Entry.Id, Value))
                                                                       Ok = True
                                                                       Dim Import = If(Result Is Nothing, Nothing, Result.Import)
                                                                       If ImportNeedsRestart(Import) Then
                                                                           MarkNeedRestart(IsSelf)
                                                                           Message = Entry.DisplayName & " 更新已就绪，重启启动器后生效。"
                                                                       ElseIf Import IsNot Nothing Then
                                                                           If Existing IsNot Nothing Then
                                                                               Message = Entry.DisplayName & " 已更新到 v" & Import.Version & "。"
                                                                           Else
                                                                               Message = Entry.DisplayName & " v" & Import.Version & " 已安装，默认未启用。"
                                                                           End If
                                                                       Else
                                                                           Message = Entry.DisplayName & " v" & Release.Version & " 已安装。"
                                                                       End If
                                                                   Catch ex As MarketException
                                                                       Message = ex.Message
                                                                   Catch ex As Exception
                                                                       Message = "安装失败：" & ex.Message
                                                                   Finally
                                                                       SetInstalling(Entry.Id, False)
                                                                   End Try
                                                                   RunOnUi(Sub()
                                                                               If Ok Then
                                                                                   Host.Hint(Message, PCL.HintType.Green)
                                                                               Else
                                                                                   Host.Hint(Message, PCL.HintType.Red)
                                                                               End If
                                                                               Done(Ok, Message)
                                                                               NotifyChanged()
                                                                           End Sub)
                                                               End Sub)
            Catch ex As Exception
                SetInstalling(Entry.Id, False)
                Host.Hint("无法启动安装任务：" & ex.Message, PCL.HintType.Red)
                Done(False, ex.Message)
            End Try
        End Sub

        Private Sub MarkNeedRestart(IsSelf As Boolean)
            SyncLock ProgressGate
                _needRestart = True
            End SyncLock
            If IsSelf Then
                SyncLock StateGate
                    _selfUpdate = Nothing
                End SyncLock
            End If
            RaiseProgress()
        End Sub
        Friend Sub UninstallAsync(Id As String, DisplayName As String, Done As Action(Of Boolean, String))
            Try
                Host.Tasks.Run("卸载 " & DisplayName, Sub(Ctx)
                                                          Dim Ok As Boolean = False
                                                          Dim Message As String = ""
                                                          Try
                                                              Ctx.Report(0.3, "正在卸载 " & DisplayName & "…")
                                                              Ok = Client.Uninstall(Id)
                                                              If Ok Then
                                                                  Message = DisplayName & " 已卸载，数据目录保留。"
                                                              Else
                                                                  Message = "卸载失败：插件文件可能正被启动器占用。请先在插件页禁用该插件，关闭并重新启动启动器后再卸载。"
                                                              End If
                                                          Catch ex As Exception
                                                              Message = "卸载失败：" & ex.Message
                                                          End Try
                                                          RunOnUi(Sub()
                                                                      Host.Hint(Message, If(Ok, PCL.HintType.Green, PCL.HintType.Red))
                                                                      Done(Ok, Message)
                                                                      NotifyChanged()
                                                                  End Sub)
                                                      End Sub)
            Catch ex As Exception
                Host.Hint("无法启动卸载任务：" & ex.Message, PCL.HintType.Red)
                Done(False, ex.Message)
            End Try
        End Sub

        Friend Function CanRollback(Id As String) As Boolean
            Try
                Return Client IsNot Nothing AndAlso Client.CanRollback(Id)
            Catch
                Return False
            End Try
        End Function

        Friend Sub RollbackAsync(Id As String, DisplayName As String, Done As Action(Of Boolean, String))
            Try
                Host.Tasks.Run("回滚 " & DisplayName, Sub(Ctx)
                                                         Dim Ok As Boolean = False
                                                         Dim Message As String = ""
                                                         Try
                                                             Ctx.Report(0.3, "正在回滚 " & DisplayName & "…")
                                                             Ok = Client.Rollback(Id)
                                                             If Ok Then
                                                                 MarkNeedRestart(False)
                                                                 Message = DisplayName & " 已回滚到上一版本，重启启动器后生效。"
                                                             Else
                                                                 Message = "回滚失败：没有可用的备份，或备份替换失败。"
                                                             End If
                                                         Catch ex As Exception
                                                             Message = "回滚失败：" & ex.Message
                                                         End Try
                                                         RunOnUi(Sub()
                                                                     Host.Hint(Message, If(Ok, PCL.HintType.Green, PCL.HintType.Red))
                                                                     Done(Ok, Message)
                                                                     NotifyChanged()
                                                                 End Sub)
                                                     End Sub)
            Catch ex As Exception
                Host.Hint("无法启动回滚任务：" & ex.Message, PCL.HintType.Red)
                Done(False, ex.Message)
            End Try
        End Sub

        Friend Sub SetEnabledAsync(Id As String, DisplayName As String, Value As Boolean, Done As Action(Of Boolean, String))
            Try
                Host.Tasks.Run(If(Value, "启用 ", "禁用 ") & DisplayName, Sub(Ctx)
                                                                         Dim Ok As Boolean = False
                                                                         Dim Message As String = ""
                                                                         Try
                                                                             Ctx.Report(0.3, "正在" & If(Value, "启用 ", "禁用 ") & DisplayName & "…")
                                                                             Ok = Client.SetEnabled(Id, Value)
                                                                             If Ok Then
                                                                                 Message = DisplayName & If(Value, " 已启用。", " 已禁用。")
                                                                             ElseIf Value Then
                                                                                 ' 3.8.x：声明了中/高风险权限但尚未信任的插件，宿主要求用户在插件页手动确认。
                                                                                 Dim Found = Client.FindInstalled(Id)
                                                                                 If Found IsNot Nothing Then
                                                                                     Message = DisplayName & " 未能启用：宿主要求先确认它的权限。请到「插件」页手动启用并允许信任。"
                                                                                 Else
                                                                                     Message = "启用失败：插件可能不存在。"
                                                                                 End If
                                                                             Else
                                                                                 Message = "禁用失败：插件可能不存在。"
                                                                             End If
                                                                         Catch ex As Exception
                                                                             Message = "操作失败：" & ex.Message
                                                                         End Try
                                                                         RunOnUi(Sub()
                                                                                     Host.Hint(Message, If(Ok, PCL.HintType.Green, PCL.HintType.Red))
                                                                                     Done(Ok, Message)
                                                                                     NotifyChanged()
                                                                                 End Sub)
                                                                     End Sub)
            Catch ex As Exception
                Host.Hint("无法启动后台任务：" & ex.Message, PCL.HintType.Red)
                Done(False, ex.Message)
            End Try
        End Sub

        Private Sub RunOnUi(Act As Action)
            ' 同 NotifyChanged：热重载后旧实例仍需把后台任务结果投递回旧页面。
            Try
                Host.RunInUi(Sub()
                                 Try
                                     Act()
                                 Catch
                                 End Try
                             End Sub)
            Catch
                Try
                    Act()
                Catch
                End Try
            End Try
        End Sub

    End Class

End Namespace
