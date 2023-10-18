﻿using System.Text.Json;

namespace ServerProxy;

public partial class Form1 : Form
{
    private static NotifyIcon _taskbarIcon;
    private static Proxy _proxy;
    private static readonly CheckBox _checkBox = new() { Text = "启动时检查更新" };
    private static Config _config;

    public Form1()
    {
        // Component initialization
        InitializeComponent();
        _taskbarIcon = new NotifyIcon();
        _taskbarIcon.Icon = Icon;
        _taskbarIcon.Visible = true;
        _taskbarIcon.ContextMenuStrip = new ContextMenuStrip();
        var _host = new ToolStripControlHost(_checkBox);
        _checkBox.CheckStateChanged += OnCheckBoxChanged;
        _taskbarIcon.ContextMenuStrip.Items.Insert(0, _host);
        _taskbarIcon.ContextMenuStrip.Items.Add("退出", null, (s, e) => OnExit());

        // Read config
        try
        {
            var raw_conf = File.ReadAllText(Application.StartupPath + "/config.json");
            _config = JsonSerializer.Deserialize<Config>(raw_conf);
        }
        catch (Exception ex)
        {
            _config = new Config
            {
                checkUpdate = true,
                serverIP = "114.132.172.176",
                baseUpdateAddr = "http://IP_ADDRESS_START_HERE.38:31080"
            };
        }


        _checkBox.Checked = _config.checkUpdate;

        // Check for update
        if (_config.checkUpdate)
        {
            var updater = new Updater();
            updater.Check(_config.baseUpdateAddr);
        }

        // Install Certificate
        CertificateUtil.Install();

        // Create Proxy
        SetStatus(Status.Starting);
        _proxy = new Proxy($"https://{_config.serverIP}/");
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

    private static void OnCheckBoxChanged(object sender, EventArgs e)
    {
        _config.checkUpdate = _checkBox.Checked;
        var configString = JsonSerializer.Serialize(_config);
        File.WriteAllText(Application.StartupPath + "/config.json", configString);
    }

    ~Form1()
    {
        _taskbarIcon.Dispose();
        if (Proxy.HnsOriginalStatus.IsStarted) _proxy.ServiceRestore();
    }
}