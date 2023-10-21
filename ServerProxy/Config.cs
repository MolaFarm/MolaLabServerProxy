namespace ServerProxy;

public class Config
{
    public string serverIP { get; set; }; // Set the default server IP here.
    public string baseUpdateAddr { get; set; } = "https://git.labserver.internal";
    public bool checkUpdate { get; set; } = true;
    public bool showMessageBoxOnStart { get; set; } = true;
}