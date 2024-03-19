using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Protocol
{
	public class Init
	{
		internal static IPAddress? ServerIp;
		public static void SetServer(IPAddress ip) { ServerIp = ip; }
	}
}
