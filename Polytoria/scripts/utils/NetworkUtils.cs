// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Polytoria.Utils;

public static class NetworkUtils
{
	public static int GetAvailablePort()
	{
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	public static bool IsPrivate(this IPAddress ip)
	{
		if (ip.IsIPv4MappedToIPv6)
			ip = ip.MapToIPv4();

		if (IPAddress.IsLoopback(ip))
			return true;

		switch (ip.AddressFamily)
		{
			case AddressFamily.InterNetwork:
				var bytes = ip.GetAddressBytes();

				// RFC 1918; Class A, B, C https://www.ietf.org/rfc/rfc1918.txt
				if (bytes[0] == 10)
					return true;

				if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
					return true;

				if (bytes[0] == 192 && bytes[1] == 168)
					return true;

				// RFC 3927; IPv4 Link-local https://www.ietf.org/rfc/rfc3927.txt
				if (bytes[0] == 169 && bytes[1] == 254)
					return true;

				// https://en.wikipedia.org/wiki/0.0.0.0
				// https://www.theregister.com/security/2024/08/09/chrome-firefox-safari-patch-0000-security-hole/1347382
				// https://archive.ph/Jpi0U
				if (bytes.All(b => b == 0))
					return true;

				return false;
			case AddressFamily.InterNetworkV6:
				return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal;
			default:
				return true;
		}
	}
}
