using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.Diagnostics;
using System.Security.Principal;
using System.IO;

namespace WindowsServiceConsoleInstaller
{
    class Program
    {
        const string LINE_SEPARATOR = "===============================================================================";

        static void Main(string[] args)
        {
#if DEBUG
#else
            //Verificar se rodou como administrador
            if (IsAdministrator() == false)
            {
                // Restart program and run as admin
                var exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                ProcessStartInfo startInfo = new ProcessStartInfo(exeName);
                startInfo.Verb = "runas";
                System.Diagnostics.Process.Start(startInfo);
                //Application.Current.Shutdown();
                return;
            }
#endif

            //ASCII ART + Descrição
            Console.WriteLine(@"
  _____           _        _           _            
 |_   _|         | |      | |         | |           
   | |  _ __  ___| |_ __ _| | __ _  __| | ___  _ __ 
   | | | '_ \/ __| __/ _` | |/ _` |/ _` |/ _ \| '__|
  _| |_| | | \__ \ || (_| | | (_| | (_| | (_) | |   
 |_____|_| |_|___/\__\__,_|_|\__,_|\__,_|\___/|_|   
                                                                                                       
===============================================================================

    Instalador do Serviço Windows
");

            //Obtendo os parametros
            string serviceName = ConfigurationManager.AppSettings["serviceName"];
            string installLocation = ConfigurationManager.AppSettings["installLocation"];
            string serviceExe = ConfigurationManager.AppSettings["serviceExe"];
            string filesLocation = ConfigurationManager.AppSettings["serviceFilesLocation"];

            //Validando os parametros
            if (string.IsNullOrEmpty(serviceName)
                || string.IsNullOrEmpty(serviceExe)
                || string.IsNullOrEmpty(installLocation)
                || string.IsNullOrEmpty(filesLocation))
            {
                Console.WriteLine("ERRO: Os parametros de configuração não estão atribuidos.");
                Console.WriteLine(LINE_SEPARATOR);
                Console.WriteLine("Precione qualquer tecla para fechar esta janela...");
                Console.ReadKey();
                return;
            }

            do
            {
                try
                {
                    Console.WriteLine(LINE_SEPARATOR);
                    Console.WriteLine("Apertar 'ENTER' para Instalar ou 'D' para Desinstalar:");

                    var k = Console.ReadKey();
                    Console.WriteLine("");

                    if (k.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine("Iniciando a instalação...");

                        //Instalação
                        //System.Threading.Thread.Sleep(5000);
                        Install(serviceName, serviceExe, installLocation, filesLocation);

                        Console.WriteLine(LINE_SEPARATOR);
                        Console.WriteLine(@"
        /@
        \ \
      ___> \
     (__O)  \
    (____@)  \
    (____@)   \
     (__o)_    \
           \    \
");
                        Console.WriteLine("Instalado com sucesso.");
                        Console.WriteLine(LINE_SEPARATOR);

                        break;
                    }
                    else if (k.Key == ConsoleKey.D)
                    {
                        //Desinstalação
                        Console.WriteLine("Iniciando a desinstalação...");

                        //Desinstalação
                        //System.Threading.Thread.Sleep(5000);
                        Uninstall(serviceName, installLocation);

                        Console.WriteLine(LINE_SEPARATOR);
                        Console.WriteLine(@"
        /@
        \ \
      ___> \
     (__O)  \
    (____@)  \
    (____@)   \
     (__o)_    \
           \    \
");
                        Console.WriteLine("Desinstalado com sucesso.");
                        Console.WriteLine(LINE_SEPARATOR);

                        break;
                    }
                    else
                    {
                        Console.WriteLine("Opção inválida.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(LINE_SEPARATOR);
                    Console.WriteLine("Não foi possivel instalar. Tente novamente mais tarde ou entre em contate o suporte.");
                    Console.WriteLine($"\n{ex.Message} {ex.InnerException?.Message}");
                    Console.WriteLine(LINE_SEPARATOR);
                    break;
                }
            }
            while (true);


            Console.WriteLine("Precione qualquer tecla para fechar esta janela...");
            Console.ReadKey();
            return;
        }


        private static void Install(string serviceName, string serviceExe, string installLocation, string filesLocation)
        {
            //Removendo a versão antiga se houver
            Uninstall(serviceName, installLocation);

            Console.WriteLine("Copiando os arquivos para a pasta de destino...");

            //Cria a pasta do local de instalação se não houver
            if (!Directory.Exists(installLocation))
            {
                Directory.CreateDirectory(installLocation);
            }

            //Cria a pasta 'Files' no local de instalação se não houver
            var installLocationFilesPath = Path.Combine(installLocation, "Files");
            if (!Directory.Exists(installLocationFilesPath))
            {
                Directory.CreateDirectory(installLocationFilesPath);
            }

            //Copia os arquivos da pasta Files para a Files do local de instalação
            if (filesLocation.StartsWith("./")  
                || filesLocation.StartsWith(".\\"))
            {
                filesLocation = Path.GetFullPath(filesLocation);
            }

            //Verificanso se encontrou a pasta Files
            if (!Directory.Exists(filesLocation))
            {
                throw new Exception("Não foi possivel localizar os arquivos de instalação");
            }

            var files = Directory.GetFiles(filesLocation);
            foreach (
                var filePath in files)
            {
                var fileDestPath = filePath.Replace(filesLocation, installLocationFilesPath);
                File.Copy(filePath, fileDestPath, true);
            }

            Console.WriteLine("Arquivos copiados.");


            Console.WriteLine("Criando e iniciando o servico...");
            //Dando um tempo desde a remoção do serviço anterior
            System.Threading.Thread.Sleep(3000);

            //Monta o caminho do executavel baseado na pasta de instalação
            var serviceExePath = Path.Combine(installLocationFilesPath, serviceExe);

            //Executar os comandos no terminal
            string createResult = Exec("sc.exe", $" create \"{serviceName}\" binpath=\"{serviceExePath}\" start=auto", false);
            if (createResult.Contains("1072"))
            {
                throw new Exception($"\n\n\n REINICIE O COMPUTADOR E TENTE NOVAMENTE \n\n {createResult} \n");
            }
            else if (createResult.Contains("ERROR") || createResult.Contains("FALHA"))
            {
                throw new Exception($"Erro ao criar o serviço. {createResult}");
            }

            //Dando um tempo entre a criação e o inicio do serviço
            System.Threading.Thread.Sleep(3000);

            var startResult = Exec("sc.exe", $" start \"{serviceName}\"", false);
            if (startResult.Contains("1072"))
            {
                throw new Exception($"\n\n\n REINICIE O COMPUTADOR E TENTE NOVAMENTE \n\n {startResult} \n");
            }
            else if (startResult.Contains("ERROR") || startResult.Contains("FALHA"))
            {
                throw new Exception($"Erro ao iniciar o serviço. {startResult}");
            }

            Console.WriteLine("Serviço criado e iniciado.");
        }

        private static void Uninstall(string serviceName, string installLocation)
        {
            Console.WriteLine("Parando e removendo o serviço atual se houver...");

            //Executar os comandos no terminal
            Exec("sc.exe", $" stop \"{serviceName}\"", false);
            Exec("sc.exe", $" delete \"{serviceName}\"", false);

            Console.WriteLine("Serviço parado e removido.");

            //Excluindo a pasta da instalação
            Console.WriteLine("Excluindo a pasta Files da instalação antiga se houver...");

            var installLocationFilesPath = Path.Combine(installLocation, "Files");
            if (Directory.Exists(installLocationFilesPath))
            {
                Directory.Delete(installLocationFilesPath, true);
                Console.WriteLine("Pasta excluida.");
            }
        }

        private static string Exec(string cmd, string arguments, bool showResultConsole = true)
        {
            var processInfo = new ProcessStartInfo()
            {
                UseShellExecute = false, //Executando processo no mesmo console atual
                FileName = cmd,
                Arguments = arguments,
                RedirectStandardOutput = true,
                Verb = "runas"
            };

            if (showResultConsole)
            {
                Console.WriteLine($"Starting the command >> {cmd} {arguments}");
                Console.WriteLine("---------------------------------------------------------");
            }

            string result = "";

            using (var process = Process.Start(processInfo))
            {
                process.WaitForExit();

                if (processInfo.RedirectStandardOutput)
                {
                    result = process.StandardOutput.ReadToEnd();
                }
            }

            if (showResultConsole)
            {
                Console.WriteLine(result);
                Console.WriteLine("---------------------------------------------------------");
                Console.WriteLine($"The command '{cmd}' has finished.");
            }

            return result;
        }

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
