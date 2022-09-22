using Rmg.Chafon.ReaderApi.Ru5102;

using var reader = await Ru5102Device.CreateAsync("COM1", 57600, 0x00);
while (Console.ReadLine() == "")
{
    await reader.InventoryAsync(null, tag => Console.WriteLine(tag));
}
