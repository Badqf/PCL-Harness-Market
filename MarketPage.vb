Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media
Imports PCL

Namespace PCLHarness.Market

    ''' <summary>插件市场主页面：搜索、安装、更新、启用与卸载。</summary>
    Friend Class MarketPage
        Inherits UserControl

        Private Const Space As String = "　·　"
        Private Const CurrentApiVersion As Integer = 6

        Private ReadOnly Owner As MarketPlugin
        Private ReadOnly Root As StackPanel
        Private ReadOnly ListPanel As StackPanel
        Private ReadOnly BannerPanel As StackPanel
        Private ReadOnly StatusText As TextBlock
        Private ReadOnly SubtitleText As TextBlock
        Private ReadOnly TabsPanel As StackPanel
        Private ReadOnly SearchBox As MyTextBox
        Private ReadOnly RefreshButton As MyButton
        Private ReadOnly SelfPanel As StackPanel
        Private ReadOnly BusyIds As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly ProgressBars As New Dictionary(Of String, ProgressBar)(StringComparer.OrdinalIgnoreCase)
        Private SelfBar As ProgressBar

        Private _keyword As String = ""
        Private _tab As String = "discover"
        Private _expandedId As String = ""
        Private _alive As Boolean = True
        Private _subscribed As Boolean

        Public Sub New(MarketOwner As MarketPlugin)
            Owner = MarketOwner
            Root = New StackPanel With {.Margin = New Thickness(25, 18, 25, 30)}
            Content = Root

            ' ---------- 页首 ----------
            Dim Title As New TextBlock With {
                .Text = "插件市场",
                .FontSize = 22,
                .FontWeight = FontWeights.SemiBold
            }
            Title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
            Root.Children.Add(Title)

            SubtitleText = MakeText("从社区索引发现、安装和管理 PCL Harness 插件。", 12, "ColorBrushGray2")
            SubtitleText.Margin = New Thickness(0, 4, 0, 0)
            Root.Children.Add(SubtitleText)

            ' ---------- 控制卡 ----------
            Dim ControlCard As New MyCard With {.Margin = New Thickness(0, 16, 0, 12)}
            Dim ControlGrid As New Grid With {.Margin = New Thickness(16, 12, 16, 10)}
            ControlGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            ControlGrid.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            ControlGrid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
            ControlGrid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
            ControlGrid.RowDefinitions.Add(New RowDefinition With {.Height = GridLength.Auto})
            ControlCard.Children.Add(ControlGrid)

            TabsPanel = New StackPanel With {
                .Orientation = Orientation.Horizontal,
                .VerticalAlignment = VerticalAlignment.Center
            }
            Grid.SetRow(TabsPanel, 0)
            Grid.SetColumn(TabsPanel, 0)
            ControlGrid.Children.Add(TabsPanel)

            Dim ToolsPanel As New StackPanel With {
                .Orientation = Orientation.Horizontal,
                .VerticalAlignment = VerticalAlignment.Center
            }
            Grid.SetRow(ToolsPanel, 0)
            Grid.SetColumn(ToolsPanel, 1)
            ControlGrid.Children.Add(ToolsPanel)

            SearchBox = New MyTextBox With {
                .Width = 240,
                .Height = 30,
                .FontSize = 12,
                .CornerRadius = New CornerRadius(6),
                .VerticalContentAlignment = VerticalAlignment.Center,
                .Margin = New Thickness(0, 0, 8, 0),
                .HintText = "搜索插件、作者或标签"
            }
            AddHandler SearchBox.TextChanged,
                Sub(Sender As Object, E As TextChangedEventArgs)
                    _keyword = If(SearchBox.Text, "").Trim()
                    RenderList()
                End Sub
            ToolsPanel.Children.Add(SearchBox)

            RefreshButton = New MyButton With {
                .Text = "刷新",
                .Height = 30,
                .MinWidth = 72,
                .Padding = New Thickness(14, 0, 14, 0),
                .VerticalAlignment = VerticalAlignment.Center
            }
            AddHandler RefreshButton.Click,
                Sub(Sender As Object, E As MouseButtonEventArgs)
                    Owner.FetchAsync(True)
                End Sub
            ToolsPanel.Children.Add(RefreshButton)

            Dim Divider As New Border With {
                .Height = 1,
                .Margin = New Thickness(0, 10, 0, 8),
                .Opacity = 0.7
            }
            Divider.SetResourceReference(Border.BackgroundProperty, "ColorBrush6")
            Grid.SetRow(Divider, 1)
            Grid.SetColumnSpan(Divider, 2)
            ControlGrid.Children.Add(Divider)

            StatusText = MakeText("", 11, "ColorBrushGray3")
            Grid.SetRow(StatusText, 2)
            Grid.SetColumnSpan(StatusText, 2)
            ControlGrid.Children.Add(StatusText)
            Root.Children.Add(ControlCard)

            SelfPanel = New StackPanel With {.Margin = New Thickness(0, 0, 0, 4)}
            Root.Children.Add(SelfPanel)

            BannerPanel = New StackPanel With {.Margin = New Thickness(0, 0, 0, 4)}
            Root.Children.Add(BannerPanel)

            ListPanel = New StackPanel()
            Root.Children.Add(ListPanel)

            Dim Footer As New TextBlock With {
                .Text = "索引由社区维护。安装包会校验 SHA-256；启用插件等于运行它的代码，请只启用你信任的来源。",
                .FontSize = 11,
                .TextWrapping = TextWrapping.Wrap,
                .Margin = New Thickness(0, 18, 0, 0)
            }
            Footer.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3")
            Root.Children.Add(Footer)

            ' ---------- 订阅与生命周期 ----------
            AddHandler Owner.Changed, AddressOf OnOwnerChanged
            AddHandler Owner.ProgressChanged, AddressOf OnOwnerProgress
            _subscribed = True
            AddHandler Me.Loaded,
                Sub(Sender As Object, E As RoutedEventArgs)
                    _alive = True
                    If Not _subscribed Then
                        AddHandler Owner.Changed, AddressOf OnOwnerChanged
                        AddHandler Owner.ProgressChanged, AddressOf OnOwnerProgress
                        _subscribed = True
                    End If
                    RenderAll()
                End Sub
            AddHandler Me.Unloaded,
                Sub(Sender As Object, E As RoutedEventArgs)
                    _alive = False
                    If _subscribed Then
                        RemoveHandler Owner.Changed, AddressOf OnOwnerChanged
                        RemoveHandler Owner.ProgressChanged, AddressOf OnOwnerProgress
                        _subscribed = False
                    End If
                End Sub

            RenderAll()
        End Sub

        Private Sub OnOwnerChanged(Sender As Object, E As EventArgs)
            If Not _alive Then Return
            RenderAll()
        End Sub

        Private Sub OnOwnerProgress(Sender As Object, E As EventArgs)
            If Not _alive Then Return
            For Each Pair In ProgressBars
                Pair.Value.Value = Math.Round(Owner.GetInstallProgress(Pair.Key) * 100)
            Next
            If SelfBar IsNot Nothing Then
                SelfBar.Value = Math.Round(Owner.SelfProgress * 100)
            End If
        End Sub

        Private Sub RenderAll()
            If Not _alive Then Return
            RenderStatus()
            RenderSelfUpdate()
            RenderTabs()
            RenderList()
        End Sub

        ' ==================== 市场自更新 ====================

        Private Sub RenderSelfUpdate()
            SelfPanel.Children.Clear()
            SelfBar = Nothing
            If Not _alive Then Return
            Dim Upd = Owner.SelfUpdate
            If Upd Is Nothing AndAlso Not Owner.NeedRestart Then Return

            Dim SelfId = GetSelfId()
            Dim Installing = Owner.IsInstalling(SelfId)
            Dim Card As New MyCard With {.Margin = New Thickness(0, 0, 0, 6)}
            Dim Layout As New Grid With {.Margin = New Thickness(18, 14, 18, 14)}
            Layout.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            Layout.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Card.Children.Add(Layout)

            Dim Left As New StackPanel()
            Dim Title As New TextBlock With {
                .Text = If(Owner.NeedRestart, "更改已就绪，重启后生效", "插件市场 v" & Upd.Version & " 可用"),
                .FontSize = 14.5,
                .FontWeight = FontWeights.SemiBold
            }
            Title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1")
            Left.Children.Add(Title)

            Dim Description As String
            If Owner.NeedRestart Then
                Description = "操作已完成。关闭并重新启动启动器后才会生效。"
            ElseIf Not String.IsNullOrWhiteSpace(Upd.Notes) Then
                Description = Upd.Notes
            Else
                Description = "新版本已发布。更新会下载并校验 SHA-256，完成后提示重启。"
            End If
            Dim DescText = MakeText(Description, 12, "ColorBrushGray2")
            DescText.TextWrapping = TextWrapping.Wrap
            DescText.Margin = New Thickness(0, 4, 12, 0)
            Left.Children.Add(DescText)

            If Installing Then
                SelfBar = New ProgressBar With {
                    .Height = 6,
                    .Minimum = 0,
                    .Maximum = 100,
                    .Value = Math.Round(Owner.SelfProgress * 100),
                    .Margin = New Thickness(0, 8, 0, 0)
                }
                SelfBar.SetResourceReference(ProgressBar.ForegroundProperty, "ColorBrush1")
                SelfBar.SetResourceReference(ProgressBar.BackgroundProperty, "ColorBrush7")
                Left.Children.Add(SelfBar)
            End If
            Grid.SetColumn(Left, 0)
            Layout.Children.Add(Left)

            Dim Right As New StackPanel With {.Margin = New Thickness(16, 0, 0, 0)}
            Grid.SetColumn(Right, 1)
            Layout.Children.Add(Right)
            If Owner.NeedRestart Then
                Dim RestartButton = MakeButton("立即重启", True, False)
                RestartButton.IsEnabled = Not Installing
                AddHandler RestartButton.Click,
                    Sub(Sender As Object, E As MouseButtonEventArgs)
                        ConfirmRestart()
                    End Sub
                Right.Children.Add(RestartButton)
            Else
                Dim UpdateButton = MakeButton("更新", True, False)
                UpdateButton.IsEnabled = Not Installing AndAlso Not BusyIds.Contains(SelfId)
                If Installing Then UpdateButton.Text = "更新中…"
                Dim TargetVersion = Upd.Version
                AddHandler UpdateButton.Click,
                    Sub(Sender As Object, E As MouseButtonEventArgs)
                        BusyIds.Add(SelfId)
                        RenderSelfUpdate()
                        Owner.UpdateSelfAsync(
                            Sub(Ok As Boolean, Message As String)
                                BusyIds.Remove(SelfId)
                                RenderAll()
                            End Sub)
                    End Sub
                Right.Children.Add(UpdateButton)
            End If
            SelfPanel.Children.Add(Card)
        End Sub

        Private Sub ConfirmRestart()
            Try
                Dim Answer = Owner.Host.Ask("立即重启 PCL Harness？未保存的操作会丢失。", "重启启动器", "立即重启", "稍后")
                If String.Equals(Answer, "立即重启", StringComparison.Ordinal) Then
                    PCL.ModBase.RestartLauncher()
                End If
            Catch ex As Exception
                Owner.Host.Hint("无法重启启动器：" & ex.Message, PCL.HintType.Red)
            End Try
        End Sub

        ' ==================== 状态区 ====================

        Private Sub RenderStatus()
            If Not _alive Then Return
            Dim St = Owner.GetState()
            Dim Parts As New List(Of String)
            If St.Loading Then Parts.Add("正在刷新索引…")
            If St.Index IsNot Nothing Then Parts.Add(St.Index.Plugins.Count & " 个插件")
            If St.FetchedAt <> DateTime.MinValue Then Parts.Add("更新于 " & St.FetchedAt.ToLocalTime().ToString("MM-dd HH:mm"))
            If Not String.IsNullOrEmpty(St.SourceUrl) Then Parts.Add("来源 " & MarketClient.HostOf(St.SourceUrl))
            If St.FromCache Then Parts.Add("本地缓存")
            StatusText.Text = If(Parts.Count = 0, "还没有读取索引。", String.Join(Space, Parts.ToArray()))

            RefreshButton.IsEnabled = Not St.Loading
            RefreshButton.Text = If(St.Loading, "刷新中…", "刷新")

            Dim MarketInstalled = 0
            If St.Index IsNot Nothing Then
                Dim InstalledMap = GetInstalledMap()
                Dim SelfId = GetSelfId()
                MarketInstalled = St.Index.Plugins.Where(Function(E) E IsNot Nothing AndAlso Not IsSameId(E.Id, SelfId) AndAlso InstalledMap.ContainsKey(E.Id)).Count()
            End If
            SubtitleText.Text = "从社区索引发现、安装和管理 PCL Harness 插件。市场里已安装 " & Math.Max(0, MarketInstalled) & " 个。"

            BannerPanel.Children.Clear()
            If Not String.IsNullOrEmpty(St.ErrorText) Then
                AddNotice(St.ErrorText, True)
            End If
            If St.Warnings IsNot Nothing AndAlso St.Warnings.Count > 0 Then
                Dim Shown = St.Warnings.Take(3).ToArray()
                AddNotice(String.Join(vbCrLf, Shown), False)
            End If
            If St.Index Is Nothing AndAlso Not St.Loading AndAlso String.IsNullOrEmpty(St.ErrorText) Then
                AddNotice("还没有索引内容。点「刷新」从网络读取；也可以在插件设置里更换索引地址。", False)
            End If
        End Sub

        Private Sub AddNotice(Text As String, IsError As Boolean)
            Dim Box As New Border With {
                .CornerRadius = New CornerRadius(5),
                .Padding = New Thickness(12, 9, 12, 9),
                .Margin = New Thickness(0, 0, 0, 6)
            }
            If IsError Then
                Box.SetResourceReference(Border.BackgroundProperty, "ColorBrushRedBack")
            Else
                Box.SetResourceReference(Border.BackgroundProperty, "ColorBrush7")
            End If
            Dim TextBlockValue = MakeText(Text, 11.5, If(IsError, "ColorBrushRedLight", "ColorBrushGray2"))
            Box.Child = TextBlockValue
            BannerPanel.Children.Add(Box)
        End Sub

        ' ==================== 分类与列表 ====================

        Private Function GetSelfId() As String
            If Owner.Host Is Nothing OrElse Owner.Host.Info Is Nothing Then Return ""
            Return If(Owner.Host.Info.Id, "")
        End Function

        Private Function IsSameId(A As String, B As String) As Boolean
            Return Not String.IsNullOrEmpty(A) AndAlso String.Equals(A, B, StringComparison.OrdinalIgnoreCase)
        End Function

        Private Function GetInstalledMap() As Dictionary(Of String, PCL.PluginSummary)
            Dim Map As New Dictionary(Of String, PCL.PluginSummary)(StringComparer.OrdinalIgnoreCase)
            Dim SelfId = GetSelfId()
            Try
                For Each One In Owner.Client.ListInstalled()
                    If One IsNot Nothing AndAlso Not String.IsNullOrEmpty(One.Id) AndAlso Not IsSameId(One.Id, SelfId) Then
                        Map(One.Id) = One
                    End If
                Next
            Catch
            End Try
            Return Map
        End Function

        Private Sub RenderTabs()
            If Not _alive OrElse TabsPanel Is Nothing Then Return
            Dim St = Owner.GetState()
            Dim Entries As New List(Of MarketPluginEntry)
            If St.Index IsNot Nothing Then Entries = St.Index.Plugins
            Dim Map = GetInstalledMap()
            Dim SelfId = GetSelfId()
            Dim MarketEntries = Entries.Where(Function(E) E IsNot Nothing AndAlso Not IsSameId(E.Id, SelfId)).ToList()

            Dim DiscoverCount = MarketEntries.Count
            Dim EnabledCount = MarketEntries.Where(Function(E) Map.ContainsKey(E.Id) AndAlso Map(E.Id).Enabled).Count()
            Dim DisabledCount = MarketEntries.Where(Function(E) Map.ContainsKey(E.Id) AndAlso Not Map(E.Id).Enabled).Count()
            Dim FavoriteCount = MarketEntries.Where(Function(E) Owner.IsFavorite(E.Id)).Count()

            TabsPanel.Children.Clear()
            TabsPanel.Children.Add(MakeTab("发现", DiscoverCount, "discover"))
            TabsPanel.Children.Add(MakeTab("收藏", FavoriteCount, "favorites"))
            TabsPanel.Children.Add(MakeTab("已安装", EnabledCount, "installed"))
            TabsPanel.Children.Add(MakeTab("已禁用", DisabledCount, "disabled"))
        End Sub

        Private Function MakeTab(Text As String, Count As Integer, Key As String) As MyButton
            Dim Value As New MyButton With {
                .Text = Text & " " & Count.ToString(),
                .Height = 32,
                .MinWidth = 0,
                .Padding = New Thickness(16, 0, 16, 0),
                .Margin = New Thickness(0, 0, 8, 0)
            }
            If String.Equals(_tab, Key, StringComparison.OrdinalIgnoreCase) Then
                Value.ColorType = MyButton.ColorState.Highlight
            End If
            AddHandler Value.Click,
                Sub(Sender As Object, E As MouseButtonEventArgs)
                    _tab = Key
                    _expandedId = ""
                    RenderAll()
                End Sub
            Return Value
        End Function


        Private Sub RenderList()
            If Not _alive OrElse ListPanel Is Nothing Then Return
            ProgressBars.Clear()
            Dim St = Owner.GetState()
            Dim Entries As New List(Of MarketPluginEntry)
            If St.Index IsNot Nothing Then Entries = St.Index.Plugins
            Dim InstalledMap = GetInstalledMap()
            Dim SelfId = GetSelfId()

            Dim All As New List(Of MarketPluginEntry)
            For Each Entry In Entries
                If Entry IsNot Nothing AndAlso Entry.Latest IsNot Nothing AndAlso Not IsSameId(Entry.Id, SelfId) Then All.Add(Entry)
            Next

            Dim Shown = All.Where(Function(E) E IsNot Nothing AndAlso E.Matches(_keyword)).ToList()
            Select Case _tab
                Case "favorites"
                    Shown = Shown.Where(Function(E) Owner.IsFavorite(E.Id)).ToList()
                Case "installed"
                    Shown = Shown.Where(Function(E) InstalledMap.ContainsKey(E.Id) AndAlso InstalledMap(E.Id).Enabled).ToList()
                Case "disabled"
                    Shown = Shown.Where(Function(E) InstalledMap.ContainsKey(E.Id) AndAlso Not InstalledMap(E.Id).Enabled).ToList()
            End Select

            Shown.Sort(
                Function(A, B)
                    If A.Featured <> B.Featured Then Return If(A.Featured, -1, 1)
                    Return String.Compare(A.DisplayName, B.DisplayName, StringComparison.OrdinalIgnoreCase)
                End Function)

            ListPanel.Children.Clear()
            If Shown.Count = 0 Then
                Dim Empty As String
                If St.Loading AndAlso St.Index Is Nothing Then
                    Empty = "正在读取索引…"
                ElseIf St.Index Is Nothing Then
                    Empty = "索引还没有加载。"
                ElseIf _tab = "favorites" Then
                    Empty = "还没有收藏市场里的插件。点卡片下方的「☆ 收藏」，把插件留在这里。"
                ElseIf _tab = "installed" Then
                    Empty = "市场里的插件还没有已启用的。"
                ElseIf _tab = "disabled" Then
                    Empty = "市场里的插件没有已禁用的。"
                ElseIf Not String.IsNullOrEmpty(_keyword) Then
                    Empty = "没有匹配的插件。换个关键词试试。"
                Else
                    Empty = "索引里还没有插件。"
                End If
                Dim HintText = MakeText(Empty, 12, "ColorBrushGray2")
                HintText.Margin = New Thickness(0, 8, 0, 2)
                ListPanel.Children.Add(HintText)
                Return
            End If

            For Each Entry In Shown
                ListPanel.Children.Add(BuildCard(Entry, InstalledMap))
            Next
        End Sub

        Private Function BuildCard(Entry As MarketPluginEntry, InstalledMap As Dictionary(Of String, PCL.PluginSummary)) As FrameworkElement
            Dim Latest = Entry.Latest
            Dim Inst As PCL.PluginSummary = Nothing
            InstalledMap.TryGetValue(Entry.Id, Inst)
            Dim SelfId = If(Owner.Host Is Nothing OrElse Owner.Host.Info Is Nothing, "", Owner.Host.Info.Id)
            Dim IsSelf = String.Equals(Entry.Id, SelfId, StringComparison.OrdinalIgnoreCase)
            Dim Busy = BusyIds.Contains(Entry.Id)
            Dim Compatible = Latest.MinApiVersion <= CurrentApiVersion
            Dim Installable = Compatible AndAlso Latest.HasHash AndAlso Latest.HasDownload

            Dim Card As New MyCard With {.Margin = New Thickness(0, 0, 0, 6)}
            Dim GridMain As New Grid With {.Margin = New Thickness(18, 10, 18, 10)}
            GridMain.ColumnDefinitions.Add(New ColumnDefinition With {.Width = New GridLength(1, GridUnitType.Star)})
            GridMain.ColumnDefinitions.Add(New ColumnDefinition With {.Width = GridLength.Auto})
            Card.Children.Add(GridMain)

            ' ---------- 左列 ----------
            Dim Left As New StackPanel With {.Margin = New Thickness(0, 0, 12, 0)}
            Grid.SetColumn(Left, 0)
            GridMain.Children.Add(Left)

            Dim TitleRow As New StackPanel With {.Orientation = Orientation.Horizontal}
            Dim NameText = MakeText(Entry.DisplayName, 14.5, "ColorBrush1")
            NameText.FontWeight = FontWeights.SemiBold
            NameText.Margin = New Thickness(0, 0, 8, 0)
            TitleRow.Children.Add(NameText)

            Dim VersionText = MakeText("v" & Latest.Version, 11.5, "ColorBrushGray2")
            VersionText.VerticalAlignment = VerticalAlignment.Bottom
            VersionText.Margin = New Thickness(0, 0, 8, 2)
            TitleRow.Children.Add(VersionText)

            If Entry.Featured Then
                Dim FeaturedText = MakeText("推荐", 10.5, "ColorBrush4")
                FeaturedText.VerticalAlignment = VerticalAlignment.Center
                FeaturedText.Margin = New Thickness(0, 0, 8, 0)
                TitleRow.Children.Add(FeaturedText)
            End If
            Left.Children.Add(TitleRow)

            Dim Meta As New List(Of String)
            If Not String.IsNullOrEmpty(Entry.Author) Then Meta.Add("作者 " & Entry.Author)
            If Latest.Size > 0 Then Meta.Add(MarketText.FormatSize(Latest.Size))
            Dim Published = MarketText.FormatDate(Latest.PublishedAt)
            If Published <> "" Then Meta.Add(Published)
            If Entry.HasDownloads Then Meta.Add("下载 " & Entry.Downloads.Value.ToString("N0", Globalization.CultureInfo.InvariantCulture))
            If Not Compatible Then Meta.Add("需要 API v" & Latest.MinApiVersion.ToString())
            If Entry.Tags IsNot Nothing AndAlso Entry.Tags.Count > 0 Then Meta.Add("#" & String.Join(" #", Entry.Tags.ToArray()))
            If Meta.Count > 0 Then
                Dim MetaText = MakeText(String.Join(" · ", Meta.ToArray()), 11, "ColorBrushGray2")
                MetaText.Margin = New Thickness(0, 3, 0, 0)
                Left.Children.Add(MetaText)
            End If

            Dim Summary As String = If(Not String.IsNullOrEmpty(Entry.Summary), Entry.Summary, Entry.Description)
            If Not String.IsNullOrEmpty(Summary) Then
                Dim SummaryText = MakeText(Summary, 12, "ColorBrushGray2")
                SummaryText.Margin = New Thickness(0, 5, 0, 0)
                SummaryText.MaxWidth = 720
                Left.Children.Add(SummaryText)
            End If

            If _expandedId = Entry.Id Then
                If Not String.IsNullOrEmpty(Entry.Description) AndAlso Entry.Description <> Summary Then
                    Dim DescriptionText = MakeText(Entry.Description, 11.5, "ColorBrushGray2")
                    DescriptionText.Margin = New Thickness(0, 8, 0, 0)
                    DescriptionText.MaxWidth = 720
                    Left.Children.Add(DescriptionText)
                End If
                If Not String.IsNullOrEmpty(Latest.Notes) Then
                    Dim NotesText = MakeText("更新说明：" & Latest.Notes, 12, "ColorBrushGray2")
                    NotesText.Margin = New Thickness(0, 8, 0, 0)
                    NotesText.MaxWidth = 720
                    Left.Children.Add(NotesText)
                End If
                Dim Extra As New List(Of String)
                If Not String.IsNullOrEmpty(Entry.License) Then Extra.Add("许可证 " & Entry.License)
                If Latest.HasHash Then Extra.Add("SHA-256 " & MarketText.ShortHash(Latest.Sha256) & "…")
                If Inst IsNot Nothing Then Extra.Add("签名 " & HostCompat.SignatureText(HostCompat.SignatureStatus(Inst)))
                If Extra.Count > 0 Then
                    Dim ExtraText = MakeText(String.Join(" · ", Extra.ToArray()), 10.5, "ColorBrushGray3")
                    ExtraText.Margin = New Thickness(0, 8, 0, 0)
                    Left.Children.Add(ExtraText)
                End If
                If Not String.IsNullOrEmpty(Entry.Homepage) Then
                    Dim Link As New TextBlock With {
                        .Text = "打开主页",
                        .FontSize = 11.5,
                        .Cursor = Cursors.Hand,
                        .TextDecorations = TextDecorations.Underline,
                        .Margin = New Thickness(0, 8, 0, 0)
                    }
                    Link.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush8")
                    Dim Target As String = Entry.Homepage
                    AddHandler Link.MouseLeftButtonUp,
                        Sub(Sender As Object, E As MouseButtonEventArgs)
                            Try
                                Owner.Host.OpenUrl(Target)
                            Catch
                            End Try
                        End Sub
                    Left.Children.Add(Link)
                End If
            End If

            ' ---------- 右列 ----------
            Dim Right As New StackPanel With {.MinWidth = 210, .VerticalAlignment = VerticalAlignment.Top}
            Grid.SetColumn(Right, 1)
            GridMain.Children.Add(Right)

            Dim StateLabel As String
            Dim Compare As Integer = 0
            If IsSelf Then
                StateLabel = "当前插件 · v" & Owner.Host.Info.Version
            ElseIf Inst Is Nothing Then
                If Not Compatible Then
                    StateLabel = "需要 API v" & Latest.MinApiVersion.ToString()
                ElseIf Not Latest.HasHash Then
                    StateLabel = "索引缺少 SHA-256"
                ElseIf Not Latest.HasDownload Then
                    StateLabel = "索引缺少下载地址"
                Else
                    StateLabel = "未安装"
                End If
            Else
                Compare = MarketText.CompareVersion(Inst.Version, Latest.Version)
                StateLabel = "已安装 v" & Inst.Version & If(Inst.Enabled, " · 已启用", " · 未启用")
                If Compare < 0 Then
                    StateLabel &= " · 可更新"
                ElseIf Compare > 0 Then
                    StateLabel &= " · 本地较新"
                End If
            End If
            Dim StateText = MakeText(StateLabel, 11, "ColorBrushGray2")
            StateText.TextAlignment = TextAlignment.Right
            StateText.Margin = New Thickness(0, 0, 0, 2)
            Right.Children.Add(StateText)

            If Owner.IsInstalling(Entry.Id) Then
                Dim Bar As New ProgressBar With {
                    .Height = 6,
                    .Minimum = 0,
                    .Maximum = 100,
                    .Value = Math.Round(Owner.GetInstallProgress(Entry.Id) * 100),
                    .Margin = New Thickness(0, 4, 0, 2)
                }
                Bar.SetResourceReference(ProgressBar.ForegroundProperty, "ColorBrush1")
                Bar.SetResourceReference(ProgressBar.BackgroundProperty, "ColorBrush7")
                ProgressBars(Entry.Id) = Bar
                Right.Children.Add(Bar)
            End If

            Dim Actions As New StackPanel With {
                .Orientation = Orientation.Horizontal,
                .HorizontalAlignment = HorizontalAlignment.Right,
                .Margin = New Thickness(0, 6, 0, 0)
            }
            Right.Children.Add(Actions)

            If Not IsSelf Then
                Dim FavoriteButton = MakeIconButton(If(Owner.IsFavorite(Entry.Id), "★", "☆"),
                                                    If(Owner.IsFavorite(Entry.Id), "取消收藏", "收藏"))
                FavoriteButton.IsEnabled = Not Busy
                AddHandler FavoriteButton.Click,
                    Sub(Sender As Object, E As MouseButtonEventArgs)
                        Owner.ToggleFavorite(Entry.Id)
                        RenderAll()
                    End Sub
                Actions.Children.Add(FavoriteButton)

                If Inst Is Nothing Then
                    If Installable Then
                        Dim InstallButton = MakeIconButton("↓", "安装 v" & Latest.Version, True, False)
                        InstallButton.IsEnabled = Not Busy
                        AddHandler InstallButton.Click,
                            Sub(Sender As Object, E As MouseButtonEventArgs)
                                BeginInstall(Entry, Latest, False)
                            End Sub
                        Actions.Children.Add(InstallButton)
                    Else
                        Dim Blocked = MakeIconButton("↓", "当前不可安装", False, False)
                        Blocked.IsEnabled = False
                        Actions.Children.Add(Blocked)
                    End If
                Else
                    If Compare < 0 Then
                        If Installable Then
                            Dim UpdateButton = MakeIconButton("↑", "更新到 v" & Latest.Version, True, False)
                            UpdateButton.IsEnabled = Not Busy
                            AddHandler UpdateButton.Click,
                                Sub(Sender As Object, E As MouseButtonEventArgs)
                                    BeginInstall(Entry, Latest, True, Inst)
                                End Sub
                            Actions.Children.Add(UpdateButton)
                        Else
                            Dim Blocked = MakeIconButton("↑", "新版本不可安装", False, False)
                            Blocked.IsEnabled = False
                            Actions.Children.Add(Blocked)
                        End If
                    End If

                    Dim ToggleButton = MakeIconButton(If(Inst.Enabled, "⏸", "▶"),
                                                     If(Inst.Enabled, "禁用", "启用"), False, False)
                    ToggleButton.IsEnabled = Not Busy
                    AddHandler ToggleButton.Click,
                        Sub(Sender As Object, E As MouseButtonEventArgs)
                            BeginToggle(Entry, Inst, Not Inst.Enabled)
                        End Sub
                    Actions.Children.Add(ToggleButton)

                    Dim RemoveButton = MakeIconButton("×", "卸载", False, True)
                    RemoveButton.IsEnabled = Not Busy
                    AddHandler RemoveButton.Click,
                        Sub(Sender As Object, E As MouseButtonEventArgs)
                            BeginUninstall(Entry, Inst)
                        End Sub
                    Actions.Children.Add(RemoveButton)

                    If Owner.CanRollback(Entry.Id) Then
                        Dim RollbackButton = MakeIconButton("↶", "回滚到上一版本")
                        RollbackButton.IsEnabled = Not Busy
                        AddHandler RollbackButton.Click,
                            Sub(Sender As Object, E As MouseButtonEventArgs)
                                BeginRollback(Entry, Inst)
                            End Sub
                        Actions.Children.Add(RollbackButton)
                    End If
                End If
            End If

            Dim DetailsButton = MakeIconButton(If(_expandedId = Entry.Id, "‹", "…"),
                                               If(_expandedId = Entry.Id, "收起详情", "查看详情"))
            DetailsButton.IsEnabled = Not Busy
            AddHandler DetailsButton.Click,
                Sub(Sender As Object, E As MouseButtonEventArgs)
                    _expandedId = If(_expandedId = Entry.Id, "", Entry.Id)
                    RenderList()
                End Sub
            Actions.Children.Add(DetailsButton)

            Return Card
        End Function

        ' ==================== 操作 ====================

        Private Sub BeginInstall(Entry As MarketPluginEntry, Release As MarketRelease, IsUpdate As Boolean, Optional Inst As PCL.PluginSummary = Nothing)

            Dim Prompt As String
            If IsUpdate Then
                Prompt = "把「" & Entry.DisplayName & "」更新到 v" & Release.Version & "？" & vbCrLf & vbCrLf &
                         "插件目录会被替换，data 目录和设置保留。若旧版本正在运行，新版本通常要重启启动器后才生效。"
            Else
                Prompt = "从索引安装「" & Entry.DisplayName & "」v" & Release.Version & "？" & vbCrLf & vbCrLf &
                         "下载后会校验 SHA-256；安装完成后插件默认不启用。"
            End If
            Dim Title = If(IsUpdate, "更新插件", "安装插件")
            If Owner.Host.Ask(Prompt, Title, "继续", "取消") <> "继续" Then Return

            BusyIds.Add(Entry.Id)
            Owner.InstallAsync(Entry, Release,
                Sub(Ok As Boolean, Message As String)
                    BusyIds.Remove(Entry.Id)
                    RenderAll()
                End Sub)
            RenderList()
        End Sub

        Private Sub BeginRollback(Entry As MarketPluginEntry, Inst As PCL.PluginSummary)
            If Inst Is Nothing Then Return
            Dim Prompt = "回滚「" & Entry.DisplayName & "」到上一版本？" & vbCrLf & vbCrLf &
                         "启动器会换回最近一份备份；如果插件正在运行，会先排队，重启启动器后生效。"
            If Owner.Host.Ask(Prompt, "回滚插件", "回滚", "取消") <> "回滚" Then Return

            BusyIds.Add(Entry.Id)
            RenderList()
            Owner.RollbackAsync(Entry.Id, Entry.DisplayName,
                Sub(Ok As Boolean, Message As String)
                    BusyIds.Remove(Entry.Id)
                    RenderAll()
                End Sub)
        End Sub

        Private Sub BeginUninstall(Entry As MarketPluginEntry, Inst As PCL.PluginSummary)
            If Inst IsNot Nothing AndAlso Inst.Loaded Then
                Owner.Host.Message("卸载插件",
                    "「" & Entry.DisplayName & "」正在运行，插件文件被启动器占用，现在无法删除。" & vbCrLf & vbCrLf &
                    "请先在「插件」页禁用它，关闭并重新启动启动器，再回到这里卸载。")
                Return
            End If

            Dim Prompt = "卸载「" & Entry.DisplayName & "」v" & Inst.Version & "？" & vbCrLf & vbCrLf &
                         "插件目录会被移到 Plugins\.trash，data 目录与设置保留。"
            If Owner.Host.Ask(Prompt, "卸载插件", "卸载", "取消") <> "卸载" Then Return

            BusyIds.Add(Entry.Id)
            RenderList()
            Owner.UninstallAsync(Entry.Id, Entry.DisplayName,
                Sub(Ok As Boolean, Message As String)
                    BusyIds.Remove(Entry.Id)
                    RenderAll()
                End Sub)
        End Sub

        Private Sub BeginToggle(Entry As MarketPluginEntry, Inst As PCL.PluginSummary, Enable As Boolean)
            If Enable Then
                Dim Prompt = "启用「" & Entry.DisplayName & "」会立即加载并运行它的代码。" & vbCrLf & vbCrLf &
                             "只启用你信任来源的插件。"
                If Owner.Host.Ask(Prompt, "启用插件", "启用", "取消") <> "启用" Then Return
            End If

            BusyIds.Add(Entry.Id)
            RenderList()
            Owner.SetEnabledAsync(Entry.Id, Entry.DisplayName, Enable,
                Sub(Ok As Boolean, Message As String)
                    BusyIds.Remove(Entry.Id)
                    RenderAll()
                End Sub)
        End Sub

        ' ==================== 小工具 ====================

        Private Shared Function MakeText(Content As String, Size As Double, Optional ColorKey As String = "ColorBrush1") As TextBlock
            Dim Value As New TextBlock With {
                .Text = If(Content, ""),
                .FontSize = Size,
                .TextWrapping = TextWrapping.Wrap
            }
            If Not String.IsNullOrEmpty(ColorKey) Then Value.SetResourceReference(TextBlock.ForegroundProperty, ColorKey)
            Return Value
        End Function

        Private Shared Function MakeIconButton(Glyph As String, Tip As String, Optional Highlight As Boolean = False, Optional Danger As Boolean = False) As MyButton
            Dim Value As New MyButton With {
                .Text = Glyph,
                .Width = 34,
                .MinWidth = 34,
                .Height = 30,
                .Padding = New Thickness(0),
                .Margin = New Thickness(4, 0, 0, 0),
                .HorizontalAlignment = HorizontalAlignment.Right
            }
            If Danger Then
                Value.ColorType = MyButton.ColorState.Red
            ElseIf Highlight Then
                Value.ColorType = MyButton.ColorState.Highlight
            End If
            ToolTipService.SetToolTip(Value, Tip)
            ToolTipService.SetShowOnDisabled(Value, True)
            Return Value
        End Function
        Private Shared Function MakeButton(Content As String, Highlight As Boolean, Danger As Boolean) As MyButton
            Dim Value As New MyButton With {
                .Text = Content,
                .Height = 30,
                .MinWidth = 96,
                .Padding = New Thickness(12, 0, 12, 0),
                .Margin = New Thickness(0, 6, 0, 0),
                .HorizontalAlignment = HorizontalAlignment.Stretch
            }
            If Danger Then
                Value.ColorType = MyButton.ColorState.Red
            ElseIf Highlight Then
                Value.ColorType = MyButton.ColorState.Highlight
            End If
            Return Value
        End Function

    End Class

End Namespace
