namespace ServerProxy;

public partial class Form1 : Form
{
    private static NotifyIcon taskbarIcon;
    private static Proxy proxy;

    public Form1()
    {
        // Component initialization
        InitializeComponent();
        taskbarIcon = new NotifyIcon();
        taskbarIcon.Icon = Icon;
        taskbarIcon.Visible = true;
        taskbarIcon.ContextMenuStrip = new ContextMenuStrip();
        taskbarIcon.ContextMenuStrip.Items.Add("退出", null, (s, e) => OnExit());

        // Install Certificate
        CertificateUtil.Install();

        // Create Proxy
        SetStatus(Status.Starting);
        proxy = new Proxy("https://114.132.172.176/");
        _ = Task.Run(proxy.StartProxy);
    }

    // Show a notification with the specified title, message, and icon.
    //
    // Parameters:
    //   title:
    //     The title of the notification.
    //
    //   message:
    //     The message of the notification.
    //
    //   icon:
    //     The icon to be displayed in the notification.
    public static void ShowNotification(string title, string message, ToolTipIcon icon)
    {
        taskbarIcon.ShowBalloonTip(1000, title, message, icon);
    }

    // Sets the status of the service.
    //
    // Parameters:
    //   status: The status to set.
    //
    // Returns:
    //   void.
    public static void SetStatus(Status status)
    {
        var iconText = "代理服务\n服务状态：";
        switch (status)
        {
            case Status.Healthy:
                iconText += "健康";
                break;
            case Status.Starting:
                iconText += "启动中";
                break;
            case Status.Unhealty:
                iconText += "连接阻塞";
                break;
        }

        taskbarIcon.Text = iconText;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Visible = false;
    }

    private static void OnExit()
    {
        taskbarIcon.Dispose();
        if (Proxy.HnsOriginalStatus.IsStarted) proxy.ServiceRestore();
        Environment.Exit(0);
    }
}