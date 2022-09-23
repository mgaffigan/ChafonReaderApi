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

        public async Task InventoryAsync(AddressSegment? tidAddress, Action<string> handleTag, CancellationToken ct = default)
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

        public async Task<byte[]> ReadMemoryAsync(
    string tag, uint password,
    MemoryBank bank, AddressSegment seg,
    CancellationToken ct = default
)
        {
            var resp = await TryReadMemoryAsync(tag, password, bank, seg, ct);
            if (!resp.Success)
            {
                throw new NakException("Tag did not respond");
            }
            return resp.Data;
        }

        public async Task<ReadResult> TryReadMemoryAsync(
            string tag, uint password,
            MemoryBank bank, AddressSegment seg,
            CancellationToken ct = default
        )
        {
            var resp = await Port.SendReceiveAsync(new ReadRequest(tag, password, bank, seg), ct);
            return new ReadResult(resp.Status == ReadStatus.Success, resp.Data);
        }
    }

    public enum MemoryBank : byte
    {
        Reserved = 0,
        EPC = 1,
        TID = 2,
        User = 3,
    }

    public record AddressSegment(int Offset, int Length);

    public record ReadResult(bool Success, byte[] Data);

    [Serializable]
    public class NakException : Exception
    {
        public NakException() { }
        public NakException(string message) : base(message) { }
        public NakException(string message, Exception inner) : base(message, inner) { }
        protected NakException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
