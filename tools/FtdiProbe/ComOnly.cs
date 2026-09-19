using System.IO.Ports;

try
{
    using var port = new SerialPort("COM4", 250000, Parity.None, 8, StopBits.Two);
    port.Open();
    Console.WriteLine("COM4 ouvert sans toucher D2XX");
    port.Close();
}
catch (Exception ex)
{
    Console.WriteLine($"COM4 échec: {ex.GetType().Name}: {ex.Message}");
}
