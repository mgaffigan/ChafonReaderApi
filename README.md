
# Pure C# API for Chafon RFID Readers
Based on C++ and C# API's published on http://chafon.com/default.aspx
Configuration and tag writing is not currently implemented.  Pull requests are accepted.

Supported devices:
- Chafon CF-RU5102

# Example use
Configure a device using a different tool, or use the default config of baud rate 57600 and address 00.  Connect to the device then poll for tags with `InventoryAsync`.

    using Rmg.Chafon.ReaderApi.Ru5102;

    using var reader = await Ru5102Device.CreateAsync("COM1", 57600, 0x00);
    while (Console.ReadLine() == "")
    {
        await reader.InventoryAsync(null, tag => Console.WriteLine(tag));
    }

# Protocol
PC talks to device using CP210x VCP USB COM Port.  No synchronization bytes used.  All 
frames are length prefixed with a CRC.

    {length} {address} {command} {data} {crc16}

Length byte does not include itself but does include the address, command, and CRC (e.g. a 
one data byte message would have length 4).  Address seems intended for a multi-drop bus 
(RS-485).  There does not seem to be any form of a "continuation" to allow for messages to
contain more than 256 bytes of data.

## Commands
| Command | Notes |
|--|--|
| 0x01 - Inventory | Produces multiple responses.  Responses 3 and 4 are "partial".  1, 2, and 0xfb are "last messages".  0xfb represents "No tags found".  Other values are unknown |
| 0x02 - Read | Read a specific tag memory |
| 0x21 - Get Reader Information | Gets version, frequency, and other information |