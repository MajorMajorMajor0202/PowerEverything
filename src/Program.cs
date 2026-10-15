using Microsoft.CommandPalette.Extensions;
using Microsoft.Windows.Storage;
using Serilog;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace PowerEverything;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        string path = ApplicationData.GetDefault().TemporaryPath;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                Path.Combine(path, "Logs/PowerEverything_.log"),
                rollingInterval: RollingInterval.Day,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            HandleCOMServerActivation();
        }
        else
        {
            Console.WriteLine("Not being launched as a Extension... exiting.");
        }
    }

    private static void HandleCOMServerActivation()
    {
        ComServer server = new();
        ManualResetEvent extensionDisposed = new(false);

        PowerEverythingExtension instance = new(extensionDisposed);
        server.RegisterClass<PowerEverythingExtension, IExtension>(() => instance);
        server.Start();

        extensionDisposed.WaitOne();
        server.Stop();
        server.UnsafeDispose();
    }
}
