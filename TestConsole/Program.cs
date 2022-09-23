using Rmg.Chafon.ReaderApi.Ru5102;

using var reader = await Ru5102Device.CreateAsync("COM1", 57600, 0x00);
while (Console.ReadLine() == "")
{
    await reader.InventoryAsync(null, tag => Console.WriteLine(tag));
    var readResp = await reader.TryReadMemoryAsync("0001", 0, MemoryBank.Reserved, new AddressSegment(4, 4));
    if (readResp.Success)
    {
        Console.WriteLine($"Read {Convert.ToHexString(readResp.Data)}");
    }
    else
    {
        Console.WriteLine("Read NAK");
    }
}
