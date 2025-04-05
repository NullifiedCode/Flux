using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using FluxServer;
using Newtonsoft.Json;

class Program
{
    private static Config config;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;

    static void Main()
    {
  

        LoadConfiguration();

        if (config.Server.HideConsole)
        {
            var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            ShowWindow(handle, SW_HIDE);
        }

        Console.WriteLine(config.Server.SaveDirectory);

        if (string.IsNullOrEmpty(config.Server.SaveDirectory) || config.Server.SaveDirectory == "/")
        {
            Console.WriteLine("Save Directory not set or invalid, Please fix it.");
            return;
        }

        var listener = new TcpListener(IPAddress.Parse(config.Server.IpAddress), config.Server.Port);
        listener.Start();
        Console.WriteLine($"Server started on {config.Server.IpAddress}:{config.Server.Port}...");

        while (true)
        {
            try
            {
                using var client = listener.AcceptTcpClient();
                using var stream = client.GetStream();

                var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address;

                if (!IsLocalSubnet(clientIp))
                {
                    Console.WriteLine($"Rejected connection from {clientIp}");
                    continue;
                }


                string fileName = Encoding.UTF8.GetString(ReadExact(stream, BitConverter.ToInt32(ReadExact(stream, 4), 0)));
                while(!string.IsNullOrEmpty(fileName))
                {
                    Console.WriteLine($"Receiving file: {fileName}");

                    long fileSize = BitConverter.ToInt64(ReadExact(stream, 8), 0);
                    Console.WriteLine($"File size: {fileSize} bytes");

                    if (!Directory.Exists(config.Server.SaveDirectory))
                        Directory.CreateDirectory(config.Server.SaveDirectory);

                    string fullPath = Path.Combine(config.Server.SaveDirectory, fileName);
                    using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                    {
                        long totalBytesReceived = 0;
                        int bufferSize = 8192;
                        byte[] buffer = new byte[bufferSize];

                        while (totalBytesReceived < fileSize)
                        {
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead == 0) throw new IOException("Connection closed unexpectedly.");

                            fileStream.Write(buffer, 0, bytesRead);
                            totalBytesReceived += bytesRead;

                            double progress = (double)totalBytesReceived / fileSize * 100;
                            Console.Write($"\rReceiving: {progress:F2}% | {totalBytesReceived / (1024 * 1024)} MB / {fileSize / (1024 * 1024)} MB");
                        }
                    }

                    Console.WriteLine($"\nFile '{fileName}' saved to '{fullPath}'");
                }
            }
            catch (Exception ex)
            {
                // Some reason it throws an error first then decides to proceed? I dont really know.
                //Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    private static bool IsLocalSubnet(IPAddress address) =>
        address.Equals(IPAddress.Loopback) ||
        (address.GetAddressBytes() is byte[] ip &&
         (ip[0] == 10 || (ip[0] == 172 && ip[1] is >= 16 and <= 31) || (ip[0] == 192 && ip[1] == 168)));

    private static void LoadConfiguration()
    {
        if (!File.Exists("flux.conf"))
            File.WriteAllText("flux.conf", JsonConvert.SerializeObject(new Config()));

        var data = File.ReadAllText("flux.conf");

        var oldConfig = JsonConvert.DeserializeObject<Config>(data);

        config = new Config();

        config.Server.IpAddress = string.IsNullOrEmpty(oldConfig?.Server.IpAddress)
            ? config.Server.IpAddress
            : oldConfig.Server.IpAddress;

        config.Server.Port = oldConfig?.Server.Port ?? config.Server.Port;

        config.Server.SaveDirectory = string.IsNullOrEmpty(oldConfig?.Server.SaveDirectory)
            ? config.Server.SaveDirectory
            : oldConfig.Server.SaveDirectory;

        config.Server.HideConsole = oldConfig?.Server.HideConsole ?? config.Server.HideConsole;

        File.WriteAllText("flux.conf", JsonConvert.SerializeObject(config));

        Console.WriteLine("Configuration loaded successfully.");
    }

    private static byte[] ReadExact(NetworkStream stream, int length)
    {
        byte[] buffer = new byte[length];
        int bytesRead = 0;
        while (bytesRead < length)
        {
            int read = stream.Read(buffer, bytesRead, length - bytesRead);
            if (read == 0) throw new IOException("Connection closed unexpectedly.");
            bytesRead += read;
        }
        return buffer;
    }
}

