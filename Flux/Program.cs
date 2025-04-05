using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Threading;
using Flux;

class Program
{
    private static Config config = null;

    static void Main(string[] args)
    {
        LoadConfiguration();

        if(config == null)
        {
            Console.WriteLine("Config is somehow null, I dont even know how you did this.");
            return;
        }

        if (!IsServerAvailable(config.Server.IpAddress, config.Server.Port))
        {
            Console.WriteLine($"Server {config.Server.IpAddress}:{config.Server.Port} is not reachable.");
            return;
        }

        if (args.Length == 0)
        {
            Console.WriteLine("Please drag and drop a file onto this application.");
            return;
        }

        foreach (var filePath in args)
            SendFile(filePath);
        Console.Read();
    }

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

        File.WriteAllText("flux.conf", JsonConvert.SerializeObject(config));

        Console.WriteLine("Configuration loaded successfully.");
    }

    private static bool IsServerAvailable(string ip, int port)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                var result = client.BeginConnect(ip, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                if (!success) return false;
                client.EndConnect(result);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SendFile(string filePath)
    {
        try
        {
            using TcpClient client = new TcpClient(config.Server.IpAddress, config.Server.Port);
            using NetworkStream stream = client.GetStream();

            FileInfo fileInfo = new FileInfo(filePath);

            string currentFileName = fileInfo.Name;

            byte[] fileNameBytes = Encoding.UTF8.GetBytes(currentFileName);
            long fileSize = fileInfo.Length;

            Console.Clear();
            Console.WriteLine($"Uploading '{currentFileName}'...");

            stream.Write(BitConverter.GetBytes(fileNameBytes.Length), 0, 4);
            stream.Write(fileNameBytes, 0, fileNameBytes.Length);
            stream.Write(BitConverter.GetBytes(fileSize), 0, 8);

            long totalSent = 0;

            int bufferSize = 8192;

            byte[] buffer = new byte[bufferSize];

            using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

            int bytesRead;

            Stopwatch stopwatch = Stopwatch.StartNew();

            while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                stream.Write(buffer, 0, bytesRead);
                totalSent += bytesRead;

                double progress = (double)totalSent / fileSize * 100;
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                double speed = elapsedSeconds > 0 ? totalSent / elapsedSeconds / 1024 / 1024 : 0; // MB/s
                double eta = speed > 0 ? (fileSize - totalSent) / (speed * 1024 * 1024) : 0;

                Console.Write($"\rProgress: {progress:F2}% | Sent: {totalSent / (1024 * 1024)} MB / {fileSize / (1024 * 1024)} MB | Speed: {speed:F2} MB/s | ETA: {eta:F1} sec");
            }

            stopwatch.Stop();
            Console.WriteLine($"\nUpload completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds!");

            Thread.Sleep(3 * 1000);
            Console.Clear();
            Console.WriteLine("Done Uploading!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError sending file '{filePath}': {ex.Message}");
        }
    }
}

