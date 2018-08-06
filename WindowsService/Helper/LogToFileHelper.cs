using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsService.Helper
{
    public class LogToFileHelper
    {
        private string logFilePath;

        public LogToFileHelper(string logFilePath)
        {
            
            this.logFilePath = logFilePath;

            var directory = Path.GetDirectoryName(logFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public void Log(string msg)
        {
            string logMsg = $"{DateTime.Now}: {msg}";
            Console.WriteLine(logMsg);

            using (StreamWriter sw = new StreamWriter(logFilePath, true))
            {
                sw.WriteLine(logMsg);
                sw.Close();
            }
        }
    }
}
