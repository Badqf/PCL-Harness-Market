Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Web.Script.Serialization

Namespace PCLHarness.Market

    ''' <summary>索引或安装流程里的可预期错误，Message 直接给用户看。</summary>
    Friend Class MarketException
        Inherits Exception
        Public Sub New(Message As String)
            MyBase.New(Message)
        End Sub
    End Class

    ''' <summary>JavaScriptSerializer 的一层薄封装，容错读取 JSON。</summary>
    Friend Module MarketJson

        Private ReadOnly Serializer As New JavaScriptSerializer() With {.MaxJsonLength = Integer.MaxValue}

        Friend Function DeserializeObject(Raw As String) As Object
            Try
                Return Serializer.DeserializeObject(Raw)
            Catch ex As Exception
                Throw New MarketException("索引不是合法 JSON：" & ex.Message)
            End Try
        End Function

        Friend Function Serialize(Value As Object) As String
            Return Serializer.Serialize(Value)
        End Function

        Friend Function AsDict(Value As Object) As Dictionary(Of String, Object)
            Return TryCast(Value, Dictionary(Of String, Object))
        End Function

        Friend Function AsArray(Value As Object) As Object()
            Return TryCast(Value, Object())
        End Function

        Friend Function AsString(Dict As Dictionary(Of String, Object), Key As String, Optional Fallback As String = "") As String
            If Dict Is Nothing Then Return Fallback
            Dim Value As Object = Nothing
            If Not Dict.TryGetValue(Key, Value) OrElse Value Is Nothing Then Return Fallback
            Return Convert.ToString(Value, CultureInfo.InvariantCulture)
        End Function

        Friend Function AsBool(Dict As Dictionary(Of String, Object), Key As String, Optional Fallback As Boolean = False) As Boolean
            If Dict Is Nothing Then Return Fallback
            Dim Value As Object = Nothing
            If Not Dict.TryGetValue(Key, Value) OrElse Value Is Nothing Then Return Fallback
            If TypeOf Value Is Boolean Then Return CBool(Value)
            Dim Text = Convert.ToString(Value, CultureInfo.InvariantCulture)
            Return String.Equals(Text, "true", StringComparison.OrdinalIgnoreCase) OrElse Text = "1"
        End Function

        Friend Function AsLong(Dict As Dictionary(Of String, Object), Key As String, Optional Fallback As Long = 0) As Long
            If Dict Is Nothing Then Return Fallback
            Dim Value As Object = Nothing
            If Not Dict.TryGetValue(Key, Value) OrElse Value Is Nothing Then Return Fallback
            Try
                Return Convert.ToInt64(Value, CultureInfo.InvariantCulture)
            Catch
                Return Fallback
            End Try
        End Function

        Friend Function AsInt(Dict As Dictionary(Of String, Object), Key As String, Optional Fallback As Integer = 0) As Integer
            Return CInt(AsLong(Dict, Key, Fallback))
        End Function

        Friend Function AsStringList(Dict As Dictionary(Of String, Object), Key As String) As List(Of String)
            Dim Result As New List(Of String)
            If Dict Is Nothing Then Return Result
            Dim Value As Object = Nothing
            If Not Dict.TryGetValue(Key, Value) OrElse Value Is Nothing Then Return Result
            Dim Items = AsArray(Value)
            If Items IsNot Nothing Then
                For Each One In Items
                    Dim Text = Convert.ToString(One, CultureInfo.InvariantCulture)
                    If Not String.IsNullOrWhiteSpace(Text) Then Result.Add(Text.Trim())
                Next
            Else
                Dim Text = Convert.ToString(Value, CultureInfo.InvariantCulture)
                If Not String.IsNullOrWhiteSpace(Text) Then Result.Add(Text.Trim())
            End If
            Return Result
        End Function

    End Module

    ''' <summary>索引里的一个插件条目。</summary>
    Friend Class MarketPluginEntry
        Public Property Id As String = ""
        Public Property Name As String = ""
        Public Property Summary As String = ""
        Public Property Description As String = ""
        Public Property Author As String = ""
        Public Property Homepage As String = ""
        Public Property License As String = ""
        Public Property Tags As New List(Of String)
        Public Property Featured As Boolean
        Public Property Latest As MarketRelease

        Public ReadOnly Property DisplayName As String
            Get
                Return If(String.IsNullOrWhiteSpace(Name), Id, Name)
            End Get
        End Property

        Friend Function Matches(Keyword As String) As Boolean
            If String.IsNullOrWhiteSpace(Keyword) Then Return True
            Dim K = Keyword.Trim()
            Return Contains(DisplayName, K) OrElse Contains(Id, K) OrElse Contains(Summary, K) OrElse
                   Contains(Description, K) OrElse Contains(Author, K) OrElse
                   Tags.Exists(Function(T) Contains(T, K))
        End Function

        Private Shared Function Contains(Source As String, Keyword As String) As Boolean
            Return Not String.IsNullOrEmpty(Source) AndAlso Source.IndexOf(Keyword, StringComparison.OrdinalIgnoreCase) >= 0
        End Function

        Friend Shared Function Parse(Dict As Dictionary(Of String, Object)) As MarketPluginEntry
            If Dict Is Nothing Then Return Nothing
            Dim Entry As New MarketPluginEntry()
            Entry.Id = MarketJson.AsString(Dict, "id").Trim().ToLowerInvariant()
            If Not Regex.IsMatch(Entry.Id, "^[a-z0-9][a-z0-9_\-\.]{0,63}$") Then Return Nothing
            Entry.Name = MarketJson.AsString(Dict, "name").Trim()
            Entry.Summary = MarketJson.AsString(Dict, "summary").Trim()
            Entry.Description = MarketJson.AsString(Dict, "description").Trim()
            Entry.Author = MarketJson.AsString(Dict, "author").Trim()
            Entry.Homepage = MarketJson.AsString(Dict, "homepage").Trim()
            Entry.License = MarketJson.AsString(Dict, "license").Trim()
            Entry.Tags = MarketJson.AsStringList(Dict, "tags")
            Entry.Featured = MarketJson.AsBool(Dict, "featured")
            Dim LatestValue As Object = Nothing
            If Not Dict.TryGetValue("latest", LatestValue) Then Return Nothing
            Dim LatestDict = MarketJson.AsDict(LatestValue)
            If LatestDict Is Nothing Then Return Nothing
            Entry.Latest = MarketRelease.Parse(LatestDict)
            If Entry.Latest Is Nothing Then Return Nothing
            Return Entry
        End Function
    End Class

    ''' <summary>索引里的一个具体版本。</summary>
    Friend Class MarketRelease
        Public Property Version As String = ""
        Public Property MinApiVersion As Integer = 6
        Public Property PublishedAt As String = ""
        Public Property Size As Long
        Public Property Sha256 As String = ""
        Public Property Download As String = ""
        Public Property Notes As String = ""

        Public ReadOnly Property HasHash As Boolean
            Get
                Return Regex.IsMatch(If(Sha256, ""), "^[0-9a-fA-F]{64}$")
            End Get
        End Property

        Public ReadOnly Property HasDownload As Boolean
            Get
                Return Uri.IsWellFormedUriString(Download, UriKind.Absolute)
            End Get
        End Property

        Friend Shared Function Parse(Dict As Dictionary(Of String, Object)) As MarketRelease
            If Dict Is Nothing Then Return Nothing
            Dim Release As New MarketRelease()
            Release.Version = MarketJson.AsString(Dict, "version").Trim()
            If String.IsNullOrEmpty(Release.Version) Then Return Nothing
            Release.MinApiVersion = MarketJson.AsInt(Dict, "minApiVersion", 6)
            Release.PublishedAt = MarketJson.AsString(Dict, "publishedAt").Trim()
            Release.Size = MarketJson.AsLong(Dict, "size", 0)
            Release.Sha256 = MarketJson.AsString(Dict, "sha256").Trim().ToLowerInvariant()
            Release.Download = MarketJson.AsString(Dict, "download").Trim()
            Release.Notes = MarketJson.AsString(Dict, "notes").Trim()
            Return Release
        End Function
    End Class

    ''' <summary>已解析的市场索引。</summary>
    Friend Class MarketIndex
        Public Property Schema As Integer
        Public Property GeneratedAt As String = ""
        Public Property Plugins As New List(Of MarketPluginEntry)
        Public Property Warnings As New List(Of String)

        Friend Function Find(Id As String) As MarketPluginEntry
            If String.IsNullOrEmpty(Id) Then Return Nothing
            Return Plugins.Find(Function(P) String.Equals(P.Id, Id, StringComparison.OrdinalIgnoreCase))
        End Function

        Friend Shared Function Parse(Raw As String) As MarketIndex
            If String.IsNullOrWhiteSpace(Raw) Then Throw New MarketException("索引是空的")
            Dim Root = MarketJson.AsDict(MarketJson.DeserializeObject(Raw))
            If Root Is Nothing Then Throw New MarketException("索引根节点必须是 JSON 对象")
            Dim Index As New MarketIndex()
            Index.Schema = MarketJson.AsInt(Root, "schema", 0)
            If Index.Schema <> 1 Then Throw New MarketException("不支持的索引版本：" & Index.Schema.ToString(CultureInfo.InvariantCulture))
            Index.GeneratedAt = MarketJson.AsString(Root, "generatedAt").Trim()
            Dim RawPlugins As Object = Nothing
            If Not Root.TryGetValue("plugins", RawPlugins) Then Throw New MarketException("索引缺少 plugins 数组")
            Dim Items = MarketJson.AsArray(RawPlugins)
            If Items Is Nothing Then Throw New MarketException("索引的 plugins 必须是数组")
            Dim Seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each Item In Items
                Dim Dict = MarketJson.AsDict(Item)
                Dim Entry = MarketPluginEntry.Parse(Dict)
                If Entry Is Nothing Then
                    Index.Warnings.Add("跳过了一条格式不正确的插件条目")
                    Continue For
                End If
                If Seen.Contains(Entry.Id) Then
                    Index.Warnings.Add("索引里重复的插件 id：" & Entry.Id)
                    Continue For
                End If
                Seen.Add(Entry.Id)
                Index.Plugins.Add(Entry)
            Next
            If Index.Plugins.Count = 0 Then Index.Warnings.Add("索引里还没有插件；作者提交条目后这里会自动出现。")
            Return Index
        End Function
    End Class

    ''' <summary>版本号比较与展示工具。</summary>
    Friend Module MarketText

        Private ReadOnly VersionPattern As New Regex("^[vV]?(\d+(?:\.\d+){0,3})(?:-([0-9A-Za-z\.\-]+))?(?:\+.*)?$")

        Friend Function CompareVersion(Left As String, Right As String) As Integer
            Dim A = SplitVersion(Left)
            Dim B = SplitVersion(Right)
            For I = 0 To 3
                Dim X = If(I < A.Numbers.Count, A.Numbers(I), 0)
                Dim Y = If(I < B.Numbers.Count, B.Numbers(I), 0)
                If X <> Y Then Return X.CompareTo(Y)
            Next
            If A.PreRelease = B.PreRelease Then Return 0
            If A.PreRelease.Length = 0 Then Return 1
            If B.PreRelease.Length = 0 Then Return -1
            Return String.CompareOrdinal(A.PreRelease, B.PreRelease)
        End Function

        Private Class VersionParts
            Public Numbers As New List(Of Integer)
            Public PreRelease As String = ""
        End Class

        Private Function SplitVersion(Value As String) As VersionParts
            Dim Result As New VersionParts()
            If String.IsNullOrWhiteSpace(Value) Then Return Result
            Dim Match = VersionPattern.Match(Value.Trim())
            Dim Core = If(Match.Success, Match.Groups(1).Value, Value.Trim())
            For Each Part In Core.Split("."c)
                Dim Number As Integer = 0
                Integer.TryParse(Part, Number)
                Result.Numbers.Add(Number)
            Next
            If Match.Success AndAlso Match.Groups(2).Success Then Result.PreRelease = Match.Groups(2).Value
            Return Result
        End Function

        Friend Function FormatSize(Bytes As Long) As String
            If Bytes <= 0 Then Return "大小未知"
            If Bytes < 1024 Then Return Bytes.ToString(CultureInfo.InvariantCulture) & " B"
            If Bytes < 1024L * 1024 Then Return (Bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) & " KB"
            Return (Bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) & " MB"
        End Function

        Friend Function FormatDate(Value As String) As String
            If String.IsNullOrWhiteSpace(Value) Then Return ""
            Dim Parsed As DateTime
            If DateTime.TryParse(Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal Or DateTimeStyles.AssumeUniversal, Parsed) Then
                Return Parsed.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            End If
            Return Value.Trim()
        End Function

        Friend Function ShortHash(Value As String) As String
            If String.IsNullOrEmpty(Value) Then Return ""
            If Value.Length <= 12 Then Return Value
            Return Value.Substring(0, 12)
        End Function

    End Module

End Namespace
