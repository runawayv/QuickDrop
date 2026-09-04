using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace QuickDrop.Services;

public class NetworkAdapterInfo
{
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public bool HasGateway { get; set; }
    public string Display => HasGateway ? $"{Name} — {Ip}" : $"{Name} — {Ip} (без шлюза)";
}

public static class NetworkService
{
    public static List<NetworkAdapterInfo> GetActiveAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var props = ni.GetIPProperties();
                var ip = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
                if (ip == null) continue;

                bool hasGateway = props.GatewayAddresses
                    .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

                list.Add(new NetworkAdapterInfo { Name = ni.Name, Ip = ip.ToString(), HasGateway = hasGateway });
            }
        }
        catch { }
        return list.OrderByDescending(a => a.HasGateway).ThenBy(a => a.Name).ToList();
    }

    public static NetworkAdapterInfo? GetBestAdapter() => GetActiveAdapters().FirstOrDefault();
}