using System;
using MsBox.Avalonia.Enums;

namespace ServerProxy.Tools;

internal static class ExceptionHandler
{
    // Handles the given exception by displaying an error message box with the exception details.
    // 
    // Parameters:
    //   ex: The exception to handle.
    public static void Handle(Exception ex)
    {
        // TODO: Migrate to Avalonia UI
        MessageBox.Show("致命错误",
            $"""
             程序遇到无法处理的异常，即将退出，请记录该错误信息并反馈给维护人员
             错误信息: {ex.Message}
             内部错误: {ex.InnerException.Message}

             错误回溯:
             {ex.StackTrace}
             """,
            ButtonEnum.Ok, Icon.Error);
        Environment.Exit(-1);
    }
}