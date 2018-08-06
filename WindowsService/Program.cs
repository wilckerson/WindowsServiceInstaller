using SharpRaven;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace WindowsService
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {

            RavenClient sentry = null;
            try
            {
                var sentryKey = System.Configuration.ConfigurationManager.AppSettings["sentryKey"];
                sentry = new RavenClient(sentryKey);
#if DEBUG

                var svc = new MyWindowsService();
            svc.OnDebug();
#else
            
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
            new MyWindowsService()
            };
            ServiceBase.Run(ServicesToRun);
            
#endif
            }
            catch (Exception ex)
            {
                sentry?.Capture(new SharpRaven.Data.SentryEvent(ex));
            }
        }
    }
}
