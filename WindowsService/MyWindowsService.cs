using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using WindowsService.Helper;
using WindowsService.Integrations;

namespace WindowsService
{
    public partial class MyWindowsService : ServiceBase
    {
        Timer timer;
        LogToFileHelper logToFile;
        private FormulaCertaIntegration integracao;

        public MyWindowsService()
        {
            InitializeComponent();

            var logFilePath = System.Configuration.ConfigurationManager.AppSettings["logFilePath"];
            logToFile = new LogToFileHelper(logFilePath);

            //Reading the interval time from App.config
            var configInterval = System.Configuration.ConfigurationManager.AppSettings["timerIntervalMinutes"];

            if (!float.TryParse(configInterval, 
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out float timerIntervalMinutes))
            {
                timerIntervalMinutes = 5; //Default value if not defined in App.config
            }

            //Converting minutes to milliseconds
            var intervalMilliseconds = TimeSpan.FromMinutes(timerIntervalMinutes).TotalMilliseconds;

            //Initializing timer with the interval
            timer = new Timer(intervalMilliseconds);
            timer.Elapsed += Timer_Elapsed;
        }

        protected override void OnStart(string[] args)
        {
            var version = System.Configuration.ConfigurationManager.AppSettings["version"];
            logToFile.Log($"OnStart {version}");

            timer.Start();

            //Run the first time on start
            Timer_Elapsed(this,null);
        }

        internal void OnDebug()
        {
            OnStart(null);

            Console.WriteLine("The process will keep running until Debug has stopped");
            System.Threading.Thread.Sleep(int.MaxValue);
        }

        protected override void OnStop()
        {
            var version = System.Configuration.ConfigurationManager.AppSettings["version"];
            logToFile.Log($"OnStop {version}");

            timer.Stop();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            logToFile.Log("Timer_Elapsed");

            //Iniciando integração com formula certa
            if (integracao == null)
            {
                integracao = new FormulaCertaIntegration();
            }
            integracao.Run();
        }
    }
}
