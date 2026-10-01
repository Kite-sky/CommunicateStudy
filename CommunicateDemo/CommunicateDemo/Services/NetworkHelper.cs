using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace CommunicateDemo.Services;

/// <summary>
/// 网络辅助：枚举本机可用 IP 地址
/// </summary>
public class NetworkHelper
{
    /// <summary>
    /// 获取本机所有可用的 IPv4 地址（含 0.0.0.0 和 127.0.0.1）
    /// </summary>
    public static List<string> GetLocalIPv4Addresses()
    {
        var result = new List<string> { "0.0.0.0", "127.0.0.1" };
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            // 跳过 回环 / 未运行 / 隧道
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (ni.OperationalStatus != OperationalStatus.Up) continue;

            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IsLinkLocalIpv4(ip.Address) &&
                    !result.Contains(ip.Address.ToString()))
                {
                    result.Add(ip.Address.ToString());
                }
            }
        }

        return result;
    }
    /// <summary>判断是否为 IPv4 链路本地地址 (169.254.x.x)</summary>
    private static bool IsLinkLocalIpv4(System.Net.IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }
}
