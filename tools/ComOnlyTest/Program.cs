using System.IO.Ports;

try
{
    using var port = new SerialPort("COM4", 250000, Parity.None, 8, StopBits.Two);
    port.Open();
    Console.WriteLine("OK: COM4 ouvert (aucun appel D2XX)");
    port.Close();
}
catch (Exception ex)
{
    Console.WriteLine($"ECHEC: {ex.GetType().Name}: {ex.Message}");
    Environment.ExitCode = 1;
}
