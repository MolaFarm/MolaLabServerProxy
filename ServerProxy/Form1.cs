namespace ServerProxy;

public partial class Form1 : Form
{
    private static NotifyIcon _taskbarIcon;
    private static Proxy _proxy;

    public Form1()
    {
        // Component initialization
        InitializeComponent();
        _taskbarIcon = new NotifyIcon();
        _taskbarIcon.Icon = Icon;
        _taskbarIcon.Visible = true;
        _taskbarIcon.ContextMenuStrip = new ContextMenuStrip();
        _taskbarIcon.ContextMenuStrip.Items.Add("退出", null, (s, e) => OnExit());

        // Check for update
        var updater = new Updater();
        updater.Check("http://IP_ADDRESS_START_HERE.38:31080");

        // Install Certificate
        CertificateUtil.Install();

        // Create Proxy
        SetStatus(Status.Starting);
        _proxy = new Proxy("https://114.132.172.176/");
        _ = Task.Run(_proxy.StartProxy);
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
        _taskbarIcon.ShowBalloonTip(1000, title, message, icon);
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

        _taskbarIcon.Text = iconText;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Visible = false;
    }

    private static void OnExit()
    {
        _taskbarIcon.Dispose();
        if (Proxy.HnsOriginalStatus.IsStarted) _proxy.ServiceRestore();
        Environment.Exit(0);
    }

    ~Form1()
    {
        _taskbarIcon.Dispose();
        if (Proxy.HnsOriginalStatus.IsStarted) _proxy.ServiceRestore();
    }
}