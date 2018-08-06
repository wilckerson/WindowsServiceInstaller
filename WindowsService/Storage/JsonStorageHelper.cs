using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsService.Storage
{
    public static class JsonStorageHelper
    {
        public static void Write<T>(string jsonFilePath,T obj) where T : class
        {
            string jsonContent = JsonConvert.SerializeObject(obj ?? Activator.CreateInstance<T>(),Formatting.Indented);
            File.WriteAllText(jsonFilePath, jsonContent);
        }

        public static T Read<T>(string jsonFilePath) where T : class
        {
            try
            {
                string fileContent = File.ReadAllText(jsonFilePath);
                T obj = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(fileContent);
                return obj ?? Activator.CreateInstance<T>();
            }
            catch (Exception)
            {
                var emptyObj = Activator.CreateInstance<T>();
                return emptyObj;
            }
        }
    }
}
