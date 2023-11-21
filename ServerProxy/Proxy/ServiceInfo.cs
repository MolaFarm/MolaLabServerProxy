using System.ServiceProcess;

namespace ServerProxy.Proxy;

public struct ServiceInfo
{
    public bool IsStarted;
    public bool IsExist;
    public ServiceStartMode StartType;
}