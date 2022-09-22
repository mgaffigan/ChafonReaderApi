using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rmg.Chafon.ReaderApi.Ru5102
{
    public class Ru5102Device : IDisposable
    {
        private readonly Ru5102SerialPort Port;
        private GetReaderInformationResponse Info;

        #region ctor

        internal Ru5102Device(Ru5102SerialPort port, GetReaderInformationResponse info)
        {
            this.Port = port;
            this.Info = info;
        }

        public static async Task<Ru5102Device> CreateAsync(string comPort, int baudRate, byte address)
        {
            var port = new Ru5102SerialPort(comPort, baudRate, address);
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var info = await port.SendReceiveAsync(new GetReaderInformationRequest(), cts.Token);
                    return new Ru5102Device(port, info);
                }
            }
            catch
            {
                port.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Port.Dispose();
        }

        #endregion

        public async Task InventoryAsync(TidAddress? tidAddress, Action<string> handleTag, CancellationToken ct = default)
        {
            await Port.SendReceiveUntilAsync(new InventoryRequest(tidAddress), resp =>
            {
                foreach (var tag in resp.Tags)
                {
                    handleTag(tag);
                }

                return !resp.ScanFinished;
            }, ct);
        }
    }

    public record TidAddress(int Offset = 2, int Length = 4);
}
