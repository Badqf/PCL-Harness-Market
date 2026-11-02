Imports System
Imports PCL

''' <summary>
''' 插件市场的示例插件：只做两件事——写一条日志、弹一个左下角提示。
''' 没有声明任何权限，也不访问文件或网络。
''' </summary>
Public Class HelloMarketPlugin
    Inherits PclPluginBase

    Public Overrides Sub OnLoad(Host As PluginHost)
        MyBase.OnLoad(Host)
        Host.LogInfo("你好，市场！示例插件已加载。")
        Host.Hint("你好，市场！感谢试用插件市场。", HintType.Green)
    End Sub

    Public Overrides Sub OnUnload()
        Host?.LogInfo("你好，市场！示例插件已卸载。")
        MyBase.OnUnload()
    End Sub
End Class
