using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;

namespace ServerProxy;

using AdapterDNS = Dictionary<NetworkInterface, List<IPAddress>>;

internal class Adapter
{
    /// <summary>
    ///     rerturn a list of Active and non-virtural adapter
    /// </summary>
    /// <returns></returns>
    public static List<NetworkInterface> ListAllInterface()
    {
        var nics = NetworkInterface.GetAllNetworkInterfaces();
        var res = new List<NetworkInterface>();

        foreach (var adapter in nics)
        {
            var Pd1 = adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                      adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            if (!IsVirtualNetworkAdapter(adapter) && IsActivateNetworkAdapter(adapter) &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback) res.Add(adapter);
        }

        return res;
    }

    public static bool IsIPv6Adapter(NetworkInterface adapter)
    {
        var adapterProperties = adapter.GetIPProperties();
        try
        {
            if (adapterProperties.GetIPv6Properties().Index > 0) return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool ShouldProxyV6()
    {
        foreach (var i in ListAllInterface())
            if (IsIPv6Adapter(i))
                return true;
        return false;
    }


    /// <summary>
    ///     set adapter with dns
    /// </summary>
    /// <param name="adapter"></param>
    /// <param name="newDnsAddress"></param>
    public static void SetDNS(NetworkInterface adapter, string[] newDnsAddress)
    {
        AdapterDNS old = new();
        var objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
        var objMOC = objMC.GetInstances();

        foreach (ManagementObject objMO in objMOC)
            if (objMO["Caption"].ToString().Contains(adapter.Description))
            {
                old.Add(adapter, GetAdapterDNS(adapter));

                var objdns = objMO.GetMethodParameters("SetDNSServerSearchOrder");
                if (objdns == null) continue;
                objdns["DNSServerSearchOrder"] = newDnsAddress;
                var res = objMO.InvokeMethod("SetDNSServerSearchOrder", objdns, null);
                var statuscode = (uint)res["ReturnValue"];
                if (statuscode != 0)
                    throw new Exception($"Unable to change DNS, status code:{statuscode.ToString()}");
            }
    }

    public static void ResetAdapterDNS(NetworkInterface adapter)
    {
        SetDNS(adapter, null);
    }

    public static void SetAdapterDNS(NetworkInterface adapter, string newdns)
    {
        var olddns = adapter.GetIPProperties().DnsAddresses[0];
        string[] newdnslist = { newdns, olddns.ToString() };

        SetDNS(adapter, newdnslist);
    }

    public static AdapterDNS GetAdapterDNS(List<NetworkInterface> aa)
    {
        AdapterDNS res = new();

        foreach (var adapter in aa)
        {
            var ip = adapter.GetIPProperties();
            res.Add(adapter, ip.DnsAddresses.ToList());
        }

        return res;
    }

    public static List<IPAddress> GetAdapterDNS(NetworkInterface ni)
    {
        var ip = ni.GetIPProperties();
        return ni.GetIPProperties().DnsAddresses.ToList();
    }

    private static bool IsVirtualNetworkAdapter(NetworkInterface networkInterface)
    {
        // Add checks to identify virtual network adapters based on description, name, or other properties
        string[] virtualAdapterKeywords = { "Virtual", "VMware", "VirtualBox" };
        return virtualAdapterKeywords.Any(keyword =>
            networkInterface.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsActivateNetworkAdapter(NetworkInterface networkInterface)
    {
        return networkInterface.OperationalStatus == OperationalStatus.Up;
    }

    public static void CSetDns(NetworkInterface adapter, string ipv4PrimaryAddress, string ipv6PrimaryAddress)
    {
        //var currentNic = GetActiveNetworkInterface();
        var setIpv4DnsAddress = $"interface ipv4 set dns name=\"{adapter.Name}\" static {ipv4PrimaryAddress}";
        var setIpv6DnsAddress = $"interface ipv6 set dns name=\"{adapter.Name}\" static {ipv6PrimaryAddress}";

        RunCommand(setIpv4DnsAddress);
        if (IsIPv6Adapter(adapter)) RunCommand(setIpv6DnsAddress);
    }

    public static void CUnsetDns(NetworkInterface adapter)
    {
        //var currentNic = GetActiveNetworkInterface();
        var setIpv4DnsAddress = $"interface ipv4 set dns name=\"{adapter.Name}\" dhcp";
        var setIpv6DnsAddress = $"interface ipv6 set dns name=\"{adapter.Name}\" dhcp";

        RunCommand(setIpv4DnsAddress);
        if (IsIPv6Adapter(adapter)) RunCommand(setIpv6DnsAddress);
    }

    private static void RunCommand(string command)
    {
        var proc = new Process();
        proc.StartInfo.FileName = "netsh.exe";
        proc.StartInfo.UseShellExecute = true;
        proc.StartInfo.Verb = "runas";
        proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        proc.StartInfo.Arguments = command;
        proc.Start();
        proc.WaitForExit();
    }
}