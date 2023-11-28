using ServerProxy.Tools;

namespace ServerProxy.Broadcast;

public class BroadCastMessage
{
	public string Title { get; set; }
	public string Body { get; set; }
	public string Datetime { get; set; }
	public string? ForceUpdateTagName { get; set; } = null;

	public void Show()
	{
		MessageBox.Show("广播通知", $"{Title}\n\n{Body}\n\n广播发送日期：{Datetime}");
	}
}