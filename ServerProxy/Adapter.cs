using System.Net.NetworkInformation;
using System.Management;
using System.Net;
using System.Diagnostics;

namespace ServerProxy
{
    using AdapterDNS = Dictionary<NetworkInterface, List<IPAddress>>;
    internal class Adapter
    {

        /// <summary>
        /// rerturn a list of Active and non-virtural adapter
        /// </summary>
        /// <returns></returns>
        public static List<NetworkInterface> ListAllInterface()
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            List<NetworkInterface> res = new List<NetworkInterface>();

            foreach (NetworkInterface adapter in nics)
            {
                bool Pd1 = (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet || adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
                if (!IsVirtualNetworkAdapter(adapter) && IsActivateNetworkAdapter(adapter))
                {
                    res.Add(adapter);
                }
            }
            return res;
        }


        /// <summary>
        ///  set adapter with dns
        /// </summary>
        /// <param name="adapter"></param>
        /// <param name="newDnsAddress"></param>
        public static void SetDNS(NetworkInterface adapter, string[] newDnsAddress)
        {
            AdapterDNS old = new();
            ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObjectCollection objMOC = objMC.GetInstances();

            foreach (ManagementObject objMO in objMOC)
            {
                if (objMO["Caption"].ToString().Contains(adapter.Description))
                {
                    old.Add(adapter, GetAdapterDNS(adapter));

                    ManagementBaseObject objdns = objMO.GetMethodParameters("SetDNSServerSearchOrder");
                    if (objdns != null)
                    {
                        Console.WriteLine("Settiing new DNS"+adapter.Name);
                        objdns["DNSServerSearchOrder"] = newDnsAddress;
                        ManagementBaseObject res = objMO.InvokeMethod("SetDNSServerSearchOrder", objdns, null);
                        //Console.WriteLine(res["ReturnValue"]);
                        uint statuscode = (uint) res["ReturnValue"];
                        if (statuscode != 0)
                        {
                            throw new Exception($"Unable to change DNS, status code:{statuscode.ToString()}");
                        }
                    } 
                    else
                    {
                        Console.WriteLine("can't find objdns of Adapter: "+adapter.Name.ToString());
                    }
                }

            }
        }

        public static void ResetAdapterDNS(NetworkInterface adapter)
        {
            SetDNS(adapter, null);
        }

        public static void SetAdapterDNS(NetworkInterface adapter, string newdns)
        {
            IPAddress olddns = adapter.GetIPProperties().DnsAddresses[0];

            string[] newdnslist = { newdns, olddns.ToString() };
            foreach (string dns in newdnslist)
            {
                Console.WriteLine(dns);
            }

            SetDNS(adapter, newdnslist);
        }
        
        public static AdapterDNS GetAdapterDNS(List<NetworkInterface> aa)
        {
            AdapterDNS res =new();

            foreach(NetworkInterface adapter in aa)
            {
                IPInterfaceProperties ip = adapter.GetIPProperties();

                res.Add(adapter, ip.DnsAddresses.ToList());
            }

            return res;
        }

        public static List<IPAddress> GetAdapterDNS(NetworkInterface ni)
        {
            IPInterfaceProperties ip = ni.GetIPProperties();
            return ni.GetIPProperties().DnsAddresses.ToList();
        }
        /// <summary>
        /// log adapter information for testing
        /// </summary>
        /// <param name="aa"></param>
        public static void LogAdapters(List<NetworkInterface> aa)
        {
            foreach(NetworkInterface adapter in aa)
            {
                Console.WriteLine("网络适配器名称：" + adapter.Name);
                //Console.WriteLine("适配器连接状态：" + adapter.OperationalStatus.ToString());

                IPInterfaceProperties ip = adapter.GetIPProperties();     //IP配置信息
                if (ip.UnicastAddresses.Count > 0)
                {
                    Console.WriteLine("IP地址:" + ip.UnicastAddresses[0].Address.ToString());
                    Console.WriteLine("子网掩码:" + ip.UnicastAddresses[0].IPv4Mask.ToString());
                }
                int DnsCount = ip.DnsAddresses.Count;
                Console.WriteLine("DNS服务器地址：");   //默认网关
                if (DnsCount > 0)
                {
                    //其中第一个为首选DNS，第二个为备用的
                    for (int i = 0; i < DnsCount; i++)
                    {
                        Console.WriteLine("              " + ip.DnsAddresses[i].ToString());
                    }
                }
            }
        }
        static bool IsVirtualNetworkAdapter(NetworkInterface networkInterface)
        {
            // Add checks to identify virtual network adapters based on description, name, or other properties
            string[] virtualAdapterKeywords = { "Virtual", "VMware", "VirtualBox" };
            return virtualAdapterKeywords.Any(keyword => networkInterface.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        static bool IsActivateNetworkAdapter(NetworkInterface networkInterface)
        {
            return (networkInterface.OperationalStatus == OperationalStatus.Up);
        }

        public static void CSetDns(NetworkInterface adapter,string ipv4PrimaryAddress, string ipv6PrimaryAddress)
        {
            //var currentNic = GetActiveNetworkInterface();
            string setIpv4DnsAddress = $"interface ipv4 set dns name=\"{adapter.Name}\" static {ipv4PrimaryAddress}";
            string setIpv6DnsAddress = $"interface ipv6 set dns name=\"{adapter.Name}\" static {ipv6PrimaryAddress}";

            RunCommand(setIpv4DnsAddress);
            RunCommand(setIpv6DnsAddress);
        }

        public static void CUnsetDns(NetworkInterface adapter)
        {
            //var currentNic = GetActiveNetworkInterface();
            string setIpv4DnsAddress = $"interface ipv4 set dns name=\"{adapter.Name}\" dhcp";
            string setIpv6DnsAddress = $"interface ipv6 set dns name=\"{adapter.Name}\" dhcp";

            RunCommand(setIpv4DnsAddress);
            RunCommand(setIpv6DnsAddress);
        }

        static void RunCommand(string command)
        {
            Process proc = new Process();
            proc.StartInfo.FileName = "netsh.exe";
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.Verb = "runas";
            proc.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            proc.StartInfo.Arguments = command;
            proc.Start();
            proc.WaitForExit();
        }

    }
}


