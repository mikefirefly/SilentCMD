using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using Brenner.SilentCmd.Properties;
using System.IO;
using System.Linq;
using System.Threading;

namespace Brenner.SilentCmd
{
    internal class Engine
    {
        private Configuration _config = new Configuration();
        private readonly LogWriter _logWriter = new LogWriter();
        private readonly string _debugLogFile = Process.GetCurrentProcess().MainModule.FileName.Replace(".exe", ".log");

        /// <summary>
        /// Executes the batch file defined in the arguments
        /// </summary>
        public int Execute(string[] args)
        {
            string debugText = "";
            try
            {
                _config.ParseArguments(args);
                _logWriter.Initialize(_config.LogFilePath, _config.LogAppend);

                if (_config.ShowHelp)
                {
                    ShowHelp();
                    return 0;
                }

                DelayIfNecessary();
                ResolveBatchFilePath();

                string launcher = "", command = "";

                if (_config.BatchFilePath.ToLower().EndsWith(".bat") || _config.BatchFilePath.ToLower().EndsWith(".cmd"))
                {
                    command = _config.BatchFileArguments;
                    launcher = _config.BatchFilePath;
                }
                else if (_config.BatchFilePath.ToLower().EndsWith(".py"))
                {
                    var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = "/c where python",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };

                    proc.Start();
                    launcher = proc.StandardOutput.ReadLine();
                    if (!File.Exists(launcher)) launcher = "";
                    command = $"\"{_config.BatchFilePath}\" {_config.BatchFileArguments}";
                }
                else if (_config.BatchFilePath.ToLower().EndsWith(".ps1"))
                {
                    var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = "/c where powershell",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };

                    proc.Start();
                    launcher = proc.StandardOutput.ReadLine();
                    if (!File.Exists(launcher))
                        if (!File.Exists(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"))
                            launcher = "";
                        else launcher = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
                    command = $"-file \"{_config.BatchFilePath}\" {_config.BatchFileArguments}";
                }
                else
                {
                    _logWriter.WriteLine(Resources.Error, $"File type launcher not configured: {_config.BatchFilePath}");
                    File.AppendAllText(_debugLogFile,
                        $"Timestamp   : {DateTime.Now}{Environment.NewLine}" +
                        $"Unsupported : {_config.BatchFilePath}" +
                        $"{Environment.NewLine}{Environment.NewLine}");
                }

                if (launcher == "")
                {
                    _logWriter.WriteLine(Resources.Error, $"Unable to find launcher for file type: {_config.BatchFilePath}");
                    File.AppendAllText(_debugLogFile,
                        $"Timestamp : {DateTime.Now}{Environment.NewLine}" +
                        $"Error     : {_config.BatchFilePath}" +
                        $"{Environment.NewLine}{Environment.NewLine}");
                    return 2;
                }

                _logWriter.WriteLine(Resources.StartingCommand, _config.BatchFilePath);

                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo(launcher, command)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,   // CreateNoWindow only works, if shell is not used
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(_config.BatchFilePath)
                    };
                    debugText = 
                        $"Timestamp : {DateTime.Now}{Environment.NewLine}" +
                        $"Filename  : {process.StartInfo.FileName}{Environment.NewLine}" +
                        $"Arguments : {process.StartInfo.Arguments}{Environment.NewLine}" +
                        $"Directory : {process.StartInfo.WorkingDirectory}";
                    process.OutputDataReceived += OutputHandler;
                    process.ErrorDataReceived += OutputHandler;
                    process.Start();
                    process.BeginOutputReadLine();
                    process.WaitForExit();
                    debugText += $"{Environment.NewLine}Exit code : {process.ExitCode}";
                    return process.ExitCode;
                }
            }
            catch (Exception e)
            {
                _logWriter.WriteLine(Resources.Error, e.Message);
                debugText += $"{Environment.NewLine}Exception : {e.Message}";
                return 1;
            }
            finally
            {
                _logWriter.WriteLine(Resources.FinishedCommand, _config.BatchFilePath);
                _logWriter.Dispose();
                debugText += $"{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(_debugLogFile, debugText);
            }
        }

        private void DelayIfNecessary()
        {
            if (_config.Delay <= TimeSpan.FromSeconds(0)) return;

            _logWriter.WriteLine("Delaying execution by {0} seconds", _config.Delay.TotalSeconds);
            Thread.Sleep(_config.Delay);
        }

        private static void ShowHelp()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            AssemblyName name = assembly.GetName();
            string userManual = string.Format(Resources.UserManual, name.Version);
            MessageBox.Show(userManual, Resources.ProgramTitle);
        }

        private void ResolveBatchFilePath()
        {
            if (string.IsNullOrEmpty(_config.BatchFilePath)) return;

            if (!string.IsNullOrEmpty(Path.GetDirectoryName(_config.BatchFilePath))) return;

            if (string.IsNullOrEmpty(Path.GetExtension(_config.BatchFilePath)))
            {
                if (FindPath(_config.BatchFilePath + ".bat")) return;
                FindPath(_config.BatchFilePath + ".cmd");
            }
            else
            {
                FindPath(_config.BatchFilePath);
            }
        }

        /// <returns>True if file was found</returns>
        private bool FindPath(string filename)
        {
            string currentPath = Path.Combine(Environment.CurrentDirectory, filename);
            if (File.Exists(currentPath)) return true;

            var enviromentPath = System.Environment.GetEnvironmentVariable("PATH");

            var paths = enviromentPath.Split(';');
            var fullPath = paths.Select(x => Path.Combine(x, filename))
                               .Where(x => File.Exists(x))
                               .FirstOrDefault();

            if (!string.IsNullOrEmpty(fullPath))
            {
                _config.BatchFilePath = fullPath;
                return true;
            }

            return false;
        }

        private void OutputHandler(object sender, DataReceivedEventArgs e)
        {
            _logWriter.WriteLine(e.Data);
        }
    }
}

// "G:\nocmdtest\pytest.py" BLAH FU BAR /LOG:log.txt
// "G:\nocmdtest\battest.bat" BLAH FU BAR /LOG:log.txt
// "G:\nocmdtest\pstest.ps1" BLAH FU BAR /LOG:log.txt
// powershell -file "G:\\nocmdtest\\pstest.ps1" BLAH FU BAR