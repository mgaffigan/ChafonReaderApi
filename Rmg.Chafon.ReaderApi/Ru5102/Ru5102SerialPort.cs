using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rmg.Chafon.ReaderApi.Ru5102
{
    internal interface IChannel
    {
        Task<TResponse> SendReceiveAsync<TResponse>(Request<TResponse> request, CancellationToken ct)
            where TResponse : Response;
        Task SendReceiveUntilAsync<TResponse>(Request<TResponse> request, Func<TResponse, bool> shouldContinue, CancellationToken ct)
            where TResponse : Response;
    }

    internal class Ru5102SerialPort : IDisposable, IChannel
    {
        private readonly byte Address;
        private readonly SerialPort Port;
        private bool isDisposed;

        public Ru5102SerialPort(string port, int baudRate, byte address)
        {
            this.Address = address;
            this.Port = new SerialPort(port, baudRate);
            this.Port.Open();
        }

        private void AssertAlive()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(Ru5102SerialPort));
            }
        }

        public void Dispose()
        {
            AssertAlive();
            isDisposed = true;

            Port.Dispose();
        }

        public async Task<TResponse> SendReceiveAsync<TResponse>(Request<TResponse> request, CancellationToken ct)
            where TResponse : Response
        {
            await WriteRequest(request);
            return await ReadFrame(request, ct);
        }

        public async Task SendReceiveUntilAsync<TResponse>(Request<TResponse> request, Func<TResponse, bool> shouldContinue, CancellationToken ct) 
            where TResponse : Response
        {
            await WriteRequest(request);
            TResponse resp;
            do
            {
                resp = await ReadFrame(request, ct);
            }
            while (shouldContinue(resp));
        }

        private async Task WriteRequest<TResponse>(Request<TResponse> request)
            where TResponse : Response
        {
            // Request packet:
            // {length} {address} {command} {packet} {crc16}
            // length does not include length byte, but does include addr, cmd, and crc
            // crc include whole packet

            var buf = new byte[256];
            var wr = new MemoryStream(buf);
            wr.SetLength(0);
            wr.WriteByte(0 /* Length - overwritten after data serialized */);
            wr.WriteByte(Address);
            wr.WriteByte(request.Command);
            request.Serialize(wr);

            // Set length 
            buf[0] = checked((byte)(wr.Length + 2 /* crc */ - 1 /* Length byte is not counted in length */));

            // append CRC
            var (crcMsb, crcLsb) = UShortToMsbLsb(GetCrc(buf.AsSpan(0, (int)wr.Length)));
            wr.WriteByte(crcLsb);
            wr.WriteByte(crcMsb);

            // Send
            await Port.BaseStream.WriteAsync(buf.AsMemory(0, (int)wr.Length));
        }

        private static ushort GetCrc(ReadOnlySpan<byte> data)
        {
            ushort crc = 0xffff;
            for (int i = 0; i < data.Length; i++)
            {
                crc = (ushort)(crc ^ data[i]);
                for (int j = 0; j < 8; j++)
                {
                    bool odd = (crc & 1) != 0;
                    crc = (ushort)(crc >> 1);
                    if (odd)
                    {
                        crc = (ushort)(crc ^ 0x8408);
                    }
                }
            }
            return crc;
        }

        private static (byte, byte) UShortToMsbLsb(ushort i) => ((byte)((i >> 8) & 0xff), (byte)(i & 0xff));
        private static int LsbMsbToInt(ReadOnlySpan<byte> b) => b[0] | (b[1] << 8);

        private async Task<TResponse> ReadFrame<TResponse>(Request<TResponse> req, CancellationToken ct)
            where TResponse : Response
        {
            // Response packet:
            // {length} {addr} {cmd} {data} {crc}
            // length does not include length byte, but does include addr, cmd, and crc
            // crc include whole packet

            // read the size field
            var sizeBuf = new byte[1 /* length */ + 1 /* addr */ + 1 /* command */];
            await ReadExactly(sizeBuf, ct);
            var size = sizeBuf[0];

            // validate header
            if (sizeBuf[1] != Address)
            {
                throw new FormatException($"Unexpected address.  Expected {Address:x2} received {sizeBuf[1]:x2}");
            }
            if (sizeBuf[2] != req.Command)
            {
                throw new FormatException($"Expected response for command {req.Command:x2} but received {size} bytes with command {sizeBuf[2]:x2}");
            }
            var respSize = size - (1 /* addr */ + 1 /* command */ + 2 /* crc */);
            if (respSize < 0)
            {
                throw new FormatException("Invalid length");
            }
            req.ValidateResponseSize(respSize);

            // then read the rest
            var fullBuf = new byte[1 /* length */ + size];
            sizeBuf.CopyTo(fullBuf, 0);
            await ReadExactly(fullBuf.AsMemory(sizeBuf.Length), ct);

            // validate CRC
            if (GetCrc(fullBuf) != 0)
            {
                throw new FormatException($"Unexpected CRC on response to {req}: {Convert.ToBase64String(fullBuf)}");
            }

            // parse
            return req.ParseResponse(fullBuf.AsSpan(1 /* length */ + 1 /* addr */ + 1 /* cmd */, respSize));
        }

        private async Task ReadExactly(Memory<byte> buf, CancellationToken ct)
        {
            for (int i = 0; i < buf.Length; )
            {
                ct.ThrowIfCancellationRequested();
                var read = await Port.BaseStream.ReadAsync(buf.Slice(i, buf.Length - i), ct);
                if (read <= 0)
                {
                    throw new EndOfStreamException();
                }
                i += read;
            }
        }
    }
}
