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
                         程序遇到致命错误，即将退出，请记录该错误信息并反馈给维护人员
                         错误信息: {ex.Message}

                         错误回溯:
                         {ex.StackTrace}
                         """, "致命错误",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
        if (ex.Message.Contains("拒绝访问"))
            MessageBox.Show("该错误可能是因为执行权限不够引起的，尝试以管理员权限身份重新运行程序可能解决该问题。", "建议", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
    }
}