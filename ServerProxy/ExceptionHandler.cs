namespace ServerProxy;

internal static class ExceptionHandler
{
    // Handles the given exception by displaying an error message box with the exception details.
    // 
    // Parameters:
    //   ex: The exception to handle.
    public static void Handle(Exception ex)
    {
        MessageBox.Show($"""
                         程序遇到无法处理的异常，即将退出，请记录该错误信息并反馈给维护人员
                         错误信息: {ex.Message}
                         内部错误: {ex.InnerException.Message}

                         错误回溯:
                         {ex.StackTrace}
                         """, "致命错误",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Exit();
    }
}