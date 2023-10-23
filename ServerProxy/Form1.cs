using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace ServerProxy;

public partial class Form1 : Form
{
    private static NotifyIcon _taskbarIcon;
    private static Proxy _proxy;
    private static List<NetworkInterface> adapters;
    public static bool IsOnExit;

    public Form1()
    {
        // Component initialization
        InitializeComponent();
        var serverServiceMenu = new ToolStripMenuItem("快速访问服务器");
        var programSettingsMenu = new ToolStripMenuItem("设置");
        var isCheckUpdateOnStart = new ToolStripMenuItem("启动时检查更新", null, OnCheckBoxChanged);
        var isShowMessageBoxOnStart = new ToolStripMenuItem("启动时检测到冲突时弹窗提示", null, OnCheckBoxChanged);
        var isShowDebugConsole = new ToolStripMenuItem("启动时同时启动Debug终端", null, OnCheckBoxChanged);

        _taskbarIcon = new NotifyIcon();
        _taskbarIcon.Icon = Icon;
        _taskbarIcon.Visible = true;
        _taskbarIcon.ContextMenuStrip = new ContextMenuStrip();
        serverServiceMenu.DropDownItems.Add("GitLab", null, OnServerServiceMenuClicked);
        serverServiceMenu.DropDownItems.Add("Coder", null, OnServerServiceMenuClicked);
        serverServiceMenu.DropDownItems.Add("NextCloud", null, OnServerServiceMenuClicked);
        programSettingsMenu.DropDownItems.Add(isCheckUpdateOnStart);
        programSettingsMenu.DropDownItems.Add(isShowMessageBoxOnStart);
        programSettingsMenu.DropDownItems.Add(isShowDebugConsole);
        _taskbarIcon.ContextMenuStrip.Items.Add(programSettingsMenu);
        _taskbarIcon.ContextMenuStrip.Items.Add(serverServiceMenu);
        _taskbarIcon.ContextMenuStrip.Items.Add("退出", null, (s, e) => OnExit());

        isCheckUpdateOnStart.Checked = Program._config.checkUpdate;
        isShowMessageBoxOnStart.Checked = Program._config.showMessageBoxOnStart;
        isShowDebugConsole.Checked = Program._config.showDebugConsoleOnStart;

        if (Program._config.showDebugConsoleOnStart) DebugConsole.AllocConsole();

        // Install Certificate
        CertificateUtil.Install();

        // Create Proxy
        SetStatus(Status.Starting);
        _proxy = new Proxy($"https://{Program._config.serverIP}/");
        _ = Task.Run(_proxy.StartProxy);

        // Set DNS
        adapters = Adapter.ListAllInterface();
        foreach (var adapter in adapters) Adapter.CSetDns(adapter, "127.0.0.1", "::1");

        // Check for update
        if (!Program._config.checkUpdate) return;
        var updater = new Updater();
        var UpdaterThread = new Thread(() => updater.CheckUpdate(Program._config.baseUpdateAddr));
        UpdaterThread.Start();
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
            case Status.UnHealthy:
                iconText += "连接阻塞";
                break;
        }

        _taskbarIcon.Text = iconText;
    }

    public static Status GetStatus()
    {
        switch (_taskbarIcon.Text)
        {
            case "代理服务\n服务状态：健康":
                return Status.Healthy;
            case "代理服务\n服务状态：启动中":
                return Status.Starting;
            case "代理服务\n服务状态：连接阻塞":
                return Status.UnHealthy;
        }

        return Status.UnHealthy;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Visible = false;
    }

    private static void OnExit()
    {
        if (Program._config.showDebugConsoleOnStart) DebugConsole.FreeConsole();
        IsOnExit = true;
        foreach (var adapter in adapters) Adapter.CUnsetDns(adapter);
        if (Proxy.HnsOriginalStatus.IsStarted) _proxy.ServiceRestore();
        _taskbarIcon.Dispose();
        Application.Exit();
    }

    private static void OnServerServiceMenuClicked(object? sender, EventArgs e)
    {
        var item = sender as ToolStripItem;
        var addr = item.Text switch
        {
            "GitLab" => "https://git.labserver.internal",
            "Coder" => "https://coder.labserver.internal",
            "NextCloud" => "https://cloud.labserver.internal",
            _ => null
        };

        Process.Start(new ProcessStartInfo(addr) { UseShellExecute = true });
    }

    private static void OnCheckBoxChanged(object? sender, EventArgs e)
    {
        var ck = sender as ToolStripMenuItem;
        ck.Checked = !ck.Checked;
        if (ck.Text == "启动时检查更新")
            Program._config.checkUpdate = ck.Checked;
        else if (ck.Text == "启动时检测到冲突时弹窗提示")
            Program._config.showMessageBoxOnStart = ck.Checked;
        else
            Program._config.showDebugConsoleOnStart = ck.Checked;

        var configString = JsonSerializer.Serialize(Program._config, SourceGenerationContext.Default.Config);
        File.WriteAllText(Application.StartupPath + "/config.json", configString);
    }
}