#include <iostream>
#include <winrt/Windows.Data.Xml.Dom.h>
#include <winrt/Windows.UI.Notifications.h>

using namespace winrt;
using namespace Windows::Data::Xml::Dom;
using namespace Windows::UI::Notifications;

extern "C" __declspec(dllexport) void Show(const char* appname, const char* title, const char* message)
{
	init_apartment();

	auto manager = ToastNotificationManager::CreateToastNotifier(to_hstring(appname));
	auto toastXml = ToastNotificationManager::GetTemplateContent(ToastTemplateType::ToastText02);

	auto elements = toastXml.GetElementsByTagName(L"text");
	elements.Item(0).InnerText(to_hstring(title));
	elements.Item(1).InnerText(to_hstring(message));

	auto toast = ToastNotification(toastXml);
	manager.Show(toast);
}
