namespace ServerProxy;

public class Config
{
	public string LastReadBroadCast { get; set; } = "";
	public int ListeningPort { get; set; } = 7788;
	public int ServerPort { get; set; } = 1080;
	public string ServerIp { get; set; }; // Set the default server IP here.
	public string BaseUpdateAddr { get; set; } = "https://git.labserver.internal";
	public bool CheckUpdate { get; set; } = true;
	public bool ShowMessageBoxOnStart { get; set; } = true;
	public bool ShowDebugConsoleOnStart { get; set; } = false;
}