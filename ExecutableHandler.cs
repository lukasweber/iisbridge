using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;

namespace iisbridge 
{

    public class ExecutableHandler 
    {
        private string processFilename;
        private string args;
        private int port;
        private Dictionary<string,string> envVariables;

        private Task processObserverTask;
        private Process process;
        private CancellationTokenSource tokenSource;
        private DateTime? startedTime;
        private bool startFailed = false;

        public DateTime? StartedTime { get => startedTime; }
        public bool Started { get => StartedTime != null; }
        public bool StartFailed { get => startFailed; }

        public ExecutableHandler(string processFilename, string args, int port, Dictionary<string,string> envVariables) 
        {
            this.processFilename = processFilename;
            this.args = args;
            this.port = port;
            this.envVariables = envVariables;
        }

        private void StartProcess() {
            // TODO: Find a better way to prevent the web app from running twice
            // Kill applications aleready running on this port
            this.KillByPort(port);

            // Create Process StartInfo
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = processFilename;
            startInfo.Arguments = args;
            foreach (KeyValuePair<string, string> variable in envVariables) 
            {
                startInfo.EnvironmentVariables.Add(variable.Key,variable.Value);
            }
            startInfo.WorkingDirectory = Environment.CurrentDirectory;

            // Start web app process   
            using (process = Process.Start(startInfo)) 
            {
                startFailed = false;
                Console.WriteLine($"Starting webapp process (process: {processFilename}, args: {args})");
                do
                {
                    if (!process.HasExited)
                    {
                        // Refresh the current process property values.
                        process.Refresh();

                        if (!process.Responding)
                        {
                            Console.WriteLine("Application not responding. It is getting killed and restarted.");
                            // Kill an restart process
                            process.Kill();
                            StartProcess();
                        }
                    }
                }
                while (!process.WaitForExit(1000) && !tokenSource.Token.IsCancellationRequested);

                startedTime = null;

                // If no cancellation was requeted -> throw error
                if (!tokenSource.Token.IsCancellationRequested) 
                {
                    startFailed = true;
                    tokenSource.Cancel();
                    throw new Exception($"Web app exited with code {process.ExitCode} (process: {processFilename}, args: {args})");
                }
            }
        }

        public void Start() 
        {
            startedTime = DateTime.Now;
            tokenSource = new CancellationTokenSource();
            this.processObserverTask = Task.Run(() => StartProcess(), tokenSource.Token);
        }

        public void Stop() 
        {
            try 
            {
                tokenSource.Cancel();
                process.Kill();
            } catch (Exception ex) 
            {
                Console.WriteLine($"Error on stopping process: {ex.Message}");
            }
        }

        private void KillByPort(int port)
        {
            var processes = GetAllProcesses();
            if (processes.Any(p => p.Port == port)) 
            {
                try 
                {
                    Process.GetProcessById(processes.First(p => p.Port == port).PID).Kill();
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            } else 
            {
                Console.WriteLine("No process to kill!");
            }
        }

        public List<PRC> GetAllProcesses()
        {
            var pStartInfo = new ProcessStartInfo();
            pStartInfo.FileName = "netstat.exe";
            pStartInfo.Arguments = "-a -n -o";
            pStartInfo.WindowStyle = ProcessWindowStyle.Maximized;
            pStartInfo.UseShellExecute = false;
            pStartInfo.RedirectStandardInput = true;
            pStartInfo.RedirectStandardOutput = true;
            pStartInfo.RedirectStandardError = true;

            var process = new Process()
            {
                StartInfo = pStartInfo
            };
            process.Start();

            var soStream = process.StandardOutput;

            var output = soStream.ReadToEnd();

            var result = new List<PRC>(); 

            var lines = Regex.Split(output, "\r\n");
            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("Proto"))
                    continue;

                var parts = line.Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries);

                var len = parts.Length;
                if (len > 2)
                    result.Add(new PRC
                    {
                        Protocol = parts[0],
                        Port = int.Parse(parts[1].Split(':').Last()),
                        PID = int.Parse(parts[len - 1])
                    });
            }
            return result;
        }
    }

    public class PRC
    {
        public int PID { get; set; }
        public int Port { get; set; }
        public string Protocol { get; set; }
    }
}