using System.IO.Ports;
using System.Threading;
using SerialCom.Models;

namespace SerialCom.Services;

/// <summary>
/// RS-232 全双工服务。直接通过 SerialPort 读写，无需方向控制。
/// </summary>
public class Rs232Service : SerialServiceBase
{
    public override CommunicationMode Mode => CommunicationMode.Rs232;

    protected override Task<int> WriteBytesCoreAsync(SerialPort port, byte[] data, CancellationToken cancellationToken)
    {
        port.Write(data, 0, data.Length);
        return Task.FromResult(data.Length);
    }
}
