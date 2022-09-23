using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rmg.Chafon.ReaderApi.Ru5102
{
    internal abstract record Request<TResponse>(byte Command)
        where TResponse : Response
    {
        public abstract TResponse ParseResponse(ReadOnlySpan<byte> data);

        public virtual void Serialize(MemoryStream wr)
        {
            // no-op
        }

        public virtual void ValidateResponseSize(int size)
        {
            // no-op
        }
    }

    internal abstract record Response();

    internal record GetReaderInformationRequest() : Request<GetReaderInformationResponse>(0x21)
    {
        public override void ValidateResponseSize(int size)
        {
            if (size != 9)
            {
                throw new ArgumentException("Unexpected length");
            }
        }


        static readonly double[] baseFreqs = new[] { 902.6d, 920.125d, 902.75d, 917.1d, 865.1d };
        static readonly double[] baseFreqMult = new[] { 0.4d, 0.25d, 0.5d, 0.2d, 0.2d };

        public override GetReaderInformationResponse ParseResponse(ReadOnlySpan<byte> data)
        {
            //             0  1  2  3  4  5  6  7  8
            //  0 1  2     3  4  5  6  7  8  9 10 11
            // 0d00 21    00 01 17 08 03 3e 00 0d 1e    b1 f1
            var res0 = data[0];
            if (res0 != 0)
            {
                throw new FormatException($"Unexpected reserved value {res0}");
            }

            var version = new Version(data[1], data[2]);

            // ??
            //var readerType = data[3];

            var trType = data[4];
            if ((trType & 2) == 0)
            {
                throw new InvalidOperationException("Reader does not support ISO 18000-6B or 6C");
            }

            // band is carried in top two bits of min and max
            // bottom six bits are tuning indicies
            var maxFreqIdx = data[5];
            var minFreqIdx = data[6];
            var band = (byte)(((maxFreqIdx & 0xc0) >> 4) | (minFreqIdx >> 6));
            if (band > baseFreqs.Length)
            {
                throw new FormatException($"Invalid band {band}");
            }

            var maxFreq = FreqFromVal(band, maxFreqIdx & 0x3f);
            var minFreq = FreqFromVal(band, minFreqIdx & 0x3f);

            var powerDbm = data[7];
            var scanTime = TimeSpan.FromMilliseconds(100 * data[8]);

            return new GetReaderInformationResponse(version, minFreq, maxFreq, powerDbm, scanTime);
        }

        private double FreqFromVal(int band, int i)
        {
            if (band > baseFreqs.Length)
            {
                throw new FormatException($"Invalid band {band}");
            }

            return baseFreqs[band] + (baseFreqMult[band] * i);
        }
    }

    internal record GetReaderInformationResponse(Version Version, double MinFreq, double MaxFreq, double PowerDBm, TimeSpan InventoryScanTimeout)
        : Response();

    // Responds with one or many responses
    // {b0} {count} {pOUcharIDList}
    // response subtype
    //  1, 2, 0xfb: no more responses
    //  3, 4: intermediate response
    // if subtype != 0xfb, Card Count
    // pOUcharIDList
    //   { {length} {tagdata} }
    internal record InventoryRequest(AddressSegment? TidAddress)
        : Request<InventoryResponse>(0x01)
    {
        public override InventoryResponse ParseResponse(ReadOnlySpan<byte> data)
        {
            var type = data[0];
            if (type >= 1 && type <= 4)
            {
                // Card Data
                var tagCount = data[1];
                var tags = new string[tagCount];
                var cid = data.Slice(2);
                for (int i = 0; i < tagCount; i++)
                {
                    var len = cid[0];
                    tags[i] = Convert.ToHexString(cid.Slice(1 /* skip len */, len));
                    cid = cid.Slice(1 /* skip len */  + len);
                }
                if (cid.Length > 0)
                {
                    throw new FormatException($"Unexpected data after tags ({Convert.ToBase64String(data)})");
                }
                return new InventoryResponse(type == 1 || type == 2, tags);
            }
            else if (type == 0xfb)
            {
                // Scan finished, no data
                if (data.Length != 1)
                {
                    throw new FormatException($"Unexpected data in subtype {type:x2} ({Convert.ToBase64String(data)})");
                }
                return new InventoryResponse(true, new string[0]);
            }
            else throw new FormatException($"Unexpected subtype {type:x2}");
        }

        public override void Serialize(MemoryStream wr)
        {
            if (TidAddress != null)
            {
                wr.WriteByte(checked((byte)TidAddress.Offset));
                wr.WriteByte(checked((byte)TidAddress.Length));
            }
        }
    }

    internal record InventoryResponse(bool ScanFinished, IReadOnlyList<string> Tags) : Response();

    internal record ReadRequest
    (
        string Tag, uint Password,
        MemoryBank BankToRead, AddressSegment SegmentToRead
    ) : Request<ReadResponse>(0x02)
    {
        public override void Serialize(MemoryStream wr)
        {
            //           0  1 2  3  4  5  6 7 8 9
            // 0e0002   01 0001 00 04 04 00000000   943b

            // tag ID
            var tagData = Convert.FromHexString(Tag);
            if ((tagData.Length % 2) != 0)
            {
                throw new InvalidOperationException("EPC must be two-byte multiple");
            }
            wr.WriteByte(checked((byte)(tagData.Length / 2)));
            wr.Write(tagData);

            // read from address
            wr.WriteByte((byte)BankToRead);
            wr.WriteByte(checked((byte)SegmentToRead.Offset));
            if ((SegmentToRead.Length % 4) != 0)
            {
                throw new InvalidOperationException("SegmentToRead Length must be multiple of 4");
            }
            wr.WriteByte(checked((byte)(SegmentToRead.Length / 4)));
            wr.WriteByte((byte)((Password >> 0) & 0xff));
            wr.WriteByte((byte)((Password >> 8) & 0xff));
            wr.WriteByte((byte)((Password >> 16) & 0xff));
            wr.WriteByte((byte)((Password >> 24) & 0xff));

            // Mask
            // Not implemented
            //if (mask)
            //{
            //    wr.WriteByte(mask address);
            //    wr.WriteByte(mask length);
            //}
        }

        public override void ValidateResponseSize(int size)
        {
            if (size < 1)
            {
                throw new InvalidOperationException();
            }
        }

        public override ReadResponse ParseResponse(ReadOnlySpan<byte> data)
        {
            var status = (ReadStatus)data[0];
            return new ReadResponse(status, data.Slice(1).ToArray());
        }
    }

    internal record ReadResponse(ReadStatus Status, byte[] Data) : Response();

    internal enum ReadStatus
    {
        Success = 0,
        Nak = 0xfc,
    }
}
