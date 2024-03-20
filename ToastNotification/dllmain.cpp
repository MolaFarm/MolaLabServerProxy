#include <iostream>
#include <winrt/Windows.Data.Xml.Dom.h>
#include <winrt/Windows.UI.Notifications.h>

using namespace winrt;
using namespace Windows::Data::Xml::Dom;
using namespace Windows::UI::Notifications;

static bool is_init = false;
static ToastNotifier* manager;

extern "C" __declspec(dllexport) void Show(const char* appname, const char* title, const char* message)
{
    if (!is_init)
    {
        init_apartment();
        static auto new_manager = ToastNotificationManager::CreateToastNotifier(to_hstring(appname));
        manager = &new_manager;
        is_init = true;
    }

    //static ToastNotifier manager = ToastNotificationManager::CreateToastNotifier(to_hstring(appname));
    auto toastXml = ToastNotificationManager::GetTemplateContent(ToastTemplateType::ToastText02);

    auto elements = toastXml.GetElementsByTagName(L"text");
    elements.Item(0).InnerText(to_hstring(title));
    elements.Item(1).InnerText(to_hstring(message));

    auto toast = ToastNotification(toastXml);
    manager->Show(toast);
}
