using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace QookiL.Core;

public sealed record LanAddress(string Ip, string Adapter, string Description, bool IsVirtual);

public static class NetworkHelper
{
    public static string HostName => Dns.GetHostName();

    /// <summary>虚拟网卡名称特征（VMware / VirtualBox / Hyper-V / WSL / VPN 等）。</summary>
    private static readonly string[] VirtualMarkers =
    {
        "vmware", "virtualbox", "virtual box", "hyper-v", "vethernet", "wsl",
        "loopback", "isatap", "teredo", "bluetooth", "wan miniport",
        "tap-", "tap windows", "vpn",
    };

    private static bool LooksVirtual(string name, string description)
    {
        var s = (name + " " + description).ToLowerInvariant();
        foreach (var m in VirtualMarkers)
            if (s.Contains(m)) return true;
        return false;
    }

    /// <summary>
    /// 列出本机可用于局域网访问的 IPv4 地址，按「真实网卡优先」排序：
    /// Wi‑Fi → 以太网 → 其他；虚拟网卡（VMware/WSL 等，其地址手机连不上）一律排最后。
    /// 二维码取第一项，保证优先给出设备真正可达的地址。
    /// </summary>
    public static List<LanAddress> GetLanIPv4s()
    {
        var result = new List<(int Score, LanAddress Addr)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                var isVirtual = LooksVirtual(nic.Name, nic.Description);
                var score = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => 0,
                    NetworkInterfaceType.Ethernet => 1,
                    _ => 2,
                } + (isVirtual ? 10 : 0);

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr.Address)) continue;
                    result.Add((score, new LanAddress(addr.Address.ToString(), nic.Name, nic.Description, isVirtual)));
                }
            }
        }
        catch
        {
            // 网络枚举失败则返回空列表
        }
        return result.OrderBy(x => x.Score).Select(x => x.Addr).ToList();
    }
}
