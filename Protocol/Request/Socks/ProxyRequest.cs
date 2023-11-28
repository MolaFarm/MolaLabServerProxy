using System.Net;
using System.Net.Sockets;
using System.Text;
using Protocol.Enum.Socks;

namespace Protocol.Request.Socks;

public class ProxyRequest
{
	public ushort Version { get; set; }
	public ProxyCommand Command { get; set; }
	public byte ReverseData { get; set; }
	public AddressType DestinationAddressType { get; set; }
	public IPAddress DestinationAddress { get; set; }
	public string? Hostname { get; set; }
	public ushort DestinationPort { get; set; }

	public byte[] ToBytes()
	{
		var addressBytes = Hostname != null ? Encoding.UTF8.GetBytes(Hostname) : DestinationAddress.GetAddressBytes();
		var addressLength = DestinationAddressType switch
		{
			AddressType.Domain => addressBytes.Length + 1,
			AddressType.IPv4 => 4,
			AddressType.IPv6 => 16
		};

		var bytes = new byte[addressLength + 6];
		bytes[0] = (byte)Version;
		bytes[1] = (byte)Command;
		bytes[2] = ReverseData;
		bytes[3] = (byte)DestinationAddressType;
		if (DestinationAddressType == AddressType.Domain)
		{
			bytes[4] = (byte)addressBytes.Length;
			Array.Copy(addressBytes, 0, bytes, 5, addressBytes.Length);
		}
		else
		{
			Array.Copy(addressBytes, 0, bytes, 4, addressBytes.Length);
		}

		bytes[addressLength + 4] = (byte)(DestinationPort >> 8);
		bytes[addressLength + 5] = (byte)(DestinationPort & 0xff);
		return bytes;
	}

	public static ProxyRequest FromBytes(byte[] bytes)
	{
		var request = new ProxyRequest
		{
			Version = bytes[0],
			Command = (ProxyCommand)bytes[1],
			ReverseData = bytes[2],
			DestinationAddressType = (AddressType)bytes[3]
		};

		switch (request.DestinationAddressType)
		{
			case AddressType.IPv4:
				var ipv4Bytes = new byte[4];
				for (var i = 0; i < 4; i++) ipv4Bytes[i] = bytes[i + 4];
				request.Hostname = $"{bytes[4]}.{bytes[5]}.{bytes[6]}.{bytes[7]}";
				request.DestinationAddress = new IPAddress(ipv4Bytes);
				request.DestinationPort = (ushort)((bytes[8] << 8) | bytes[9]);
				break;
			case AddressType.IPv6:
				for (var i = 0; i < 16; i += 2)
				{
					request.Hostname += ((bytes[i + 4] << 8) | bytes[i + 5]).ToString("X4");
					if (i != 15) request.Hostname += ":";
				}

				var ipv6Bytes = new byte[16];
				for (var i = 0; i < 4; i++) ipv6Bytes[i] = bytes[i + 4];
				request.DestinationAddress = new IPAddress(ipv6Bytes);
				break;
			case AddressType.Domain:
				// TODO
				request.Hostname = Encoding.UTF8.GetString(bytes, 5, bytes[4]);
				if (request.Hostname.EndsWith("labserver.internal"))
				{
					request.DestinationAddress = IPAddress.Parse("IP_ADDRESS_START_HERE.51");
				}
				else
				{
					var addrs = Dns.GetHostAddresses(request.Hostname);
					if (addrs != null) request.DestinationAddress = addrs[0];
				}

				request.DestinationPort = (ushort)((bytes[bytes[4] + 5] << 8) | bytes[bytes[4] + 6]);
				break;
		}

		return request;
	}

	public byte[] GetAcceptBytes(Socket? clientSocket = null, int? udpListenPort = null)
	{
		var addressLength = 4;

		if (Command.Equals(ProxyCommand.Udp))
		{
			ArgumentNullException.ThrowIfNull(clientSocket);
			ArgumentNullException.ThrowIfNull(udpListenPort);
			if (clientSocket.LocalEndPoint.AddressFamily.Equals(AddressFamily.InterNetworkV6))
				addressLength = 16;
		}
		else if (DestinationAddress.AddressFamily.Equals(AddressFamily.InterNetworkV6))
		{
			addressLength = 16;
		}

		var length = 4 + addressLength + 2;
		var bytes = new byte[length];
		bytes[0] = (byte)Version;
		bytes[1] = 0; // Accept
		bytes[2] = 0; // Reverse
		bytes[3] = DestinationAddress.AddressFamily switch
		{
			AddressFamily.InterNetwork => 1,
			AddressFamily.InterNetworkV6 => 4
		};
		if (!Command.Equals(ProxyCommand.Udp))
		{
			var addressBytes = DestinationAddress.GetAddressBytes();
			Array.Copy(bytes, 4, addressBytes, 0, addressLength);
			bytes[length - 2] = (byte)(DestinationPort >> 8);
			bytes[length - 1] = (byte)(DestinationPort & 0xff);
		}
		else
		{
			var addressBytes = (clientSocket.LocalEndPoint as IPEndPoint).Address.GetAddressBytes();
			Array.Copy(bytes, 4, addressBytes, 0, addressLength);
			bytes[length - 2] = (byte)(udpListenPort >> 8);
			bytes[length - 1] = (byte)(udpListenPort & 0xff);
		}

		return bytes;
	}
}