Imports System
Imports System.Globalization
Imports System.Reflection
Imports PCL

Namespace PCLHarness.Market

    ''' <summary>下载结果（与 3.8.3 的 PluginDownloadResult 对齐，旧宿主退化为 Ok/Failed）。</summary>
    Friend Class MarketDownloadOutcome
        Public Property Code As Integer
        Public Property Message As String = ""
        Public ReadOnly Property Ok As Boolean
            Get
                Return Code = 0
            End Get
        End Property
    End Class

    ''' <summary>
    ''' 3.7.3 ~ 3.8.3 的宿主能力差异都收在这里：
    ''' 新 API 用反射探测，旧宿主自动退回旧签名，市场仍然保持 minApiVersion 6 可加载。
    ''' </summary>
    Friend NotInheritable Class HostCompat

        Private Sub New()
        End Sub

        Public Shared Function Download(Host As PCL.PluginHost, Url As String, Target As String) As MarketDownloadOutcome
            If Host Is Nothing Then Return Failed("没有可用的宿主")
            Try
                Dim Method As MethodInfo = Host.GetType().GetMethod(
                    "HttpDownloadResult",
                    New Type() {GetType(String), GetType(String)})
                If Method IsNot Nothing Then
                    Dim Raw As Object = Method.Invoke(Host, New Object() {Url, Target})
                    Dim Code As Integer = Convert.ToInt32(Raw, CultureInfo.InvariantCulture)
                    Return New MarketDownloadOutcome With {
                        .Code = Code,
                        .Message = DescribeDownload(Code)
                    }
                End If
            Catch ex As TargetInvocationException
                Return Failed(If(ex.InnerException IsNot Nothing, ex.InnerException.Message, ex.Message))
            Catch
                ' 反射本身失败时继续尝试旧 API，不把兼容逻辑变成新的故障点。
            End Try

            Try
                If Host.HttpDownload(Url, Target) Then
                    Return New MarketDownloadOutcome With {.Code = 0, .Message = ""}
                End If
                Return New MarketDownloadOutcome With {
                    .Code = 5,
                    .Message = "下载失败（网络不可用或服务器返回错误）"
                }
            Catch ex As Exception
                Return Failed(ex.Message)
            End Try
        End Function

        Private Shared Function Failed(Message As String) As MarketDownloadOutcome
            Return New MarketDownloadOutcome With {
                .Code = 5,
                .Message = If(String.IsNullOrWhiteSpace(Message), "下载失败", Message)
            }
        End Function

        Public Shared Function DescribeDownload(Code As Integer) As String
            Select Case Code
                Case 0 : Return ""
                Case 1 : Return "插件没有声明网络权限，无法下载。"
                Case 2 : Return "目标主机未被授权。请在插件设置中批准该主机，或让索引改用已授权的主机。"
                Case 3 : Return "宿主拒绝写入下载路径。"
                Case 4 : Return "安装包超过宿主允许的大小，或下载内容异常。"
                Case Else : Return "下载失败（网络不可用、服务器返回错误，或被宿主拒绝）。"
            End Select
        End Function

        ''' <summary>0=未签名 1=已签名 2=签名无效 3=未知发布者；旧宿主返回 0。</summary>
        Public Shared Function SignatureStatus(Info As PCL.PluginSummary) As Integer
            If Info Is Nothing Then Return 0
            Try
                Dim Prop = Info.GetType().GetProperty("SignatureStatus")
                If Prop IsNot Nothing Then
                    Return Convert.ToInt32(Prop.GetValue(Info, Nothing), CultureInfo.InvariantCulture)
                End If
            Catch
            End Try
            Return 0
        End Function

        Public Shared Function SignatureText(Status As Integer) As String
            Select Case Status
                Case 1 : Return "已签名"
                Case 2 : Return "签名无效"
                Case 3 : Return "未知发布者"
                Case Else : Return "未签名"
            End Select
        End Function

        Public Shared Function HasBackup(Info As PCL.PluginSummary) As Boolean
            If Info Is Nothing Then Return False
            Try
                Dim Prop = Info.GetType().GetProperty("HasBackup")
                If Prop IsNot Nothing Then Return CBool(Prop.GetValue(Info, Nothing))
            Catch
            End Try
            Return False
        End Function

        Public Shared Function CanRollback(Host As PCL.PluginHost, Id As String) As Boolean
            If Host Is Nothing OrElse String.IsNullOrWhiteSpace(Id) Then Return False
            Try
                Dim Method = Host.Plugins.GetType().GetMethod("CanRollback", New Type() {GetType(String)})
                If Method IsNot Nothing Then Return CBool(Method.Invoke(Host.Plugins, New Object() {Id}))
            Catch
            End Try
            Return False
        End Function

        Public Shared Function Rollback(Host As PCL.PluginHost, Id As String) As Boolean
            If Host Is Nothing OrElse String.IsNullOrWhiteSpace(Id) Then Return False
            Try
                Dim Method = Host.Plugins.GetType().GetMethod("Rollback", New Type() {GetType(String)})
                If Method IsNot Nothing Then Return CBool(Method.Invoke(Host.Plugins, New Object() {Id}))
            Catch
            End Try
            Return False
        End Function

    End Class

End Namespace
