using System.ServiceProcess;

namespace CloudSyncService;

internal static class Program
{
    /// <summary>
    /// Главная точка входа для приложения
    /// </summary>
    static void Main()
    {
        ServiceBase[] ServicesToRun;
        ServicesToRun = new ServiceBase[]
        {
            new SyncService()
        };
        ServiceBase.Run(ServicesToRun);
    }
}