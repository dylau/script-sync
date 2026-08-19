using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;


namespace ScriptSync
{
    [Rhino.Commands.CommandStyle(Rhino.Commands.Style.ScriptRunner)]
    public class ScriptSyncStart : Command
    {
        /// <summary> The server that listens for incoming paths to run. </summary>
        private TcpListener _server;
        /// <summary> The thread that runs the server. </summary>
        public Thread WorkerThread { get; set; }
        /// <summary> Whether the server is running or not. </summary>
        public bool IsRunning { get; set; }
        /// <summary> The IP address of the server. </summary>
        public string Ip = "127.0.0.1";
        /// <summary> The port of the server. </summary>
        public int Port = 58258;

        public ScriptSyncStart()
        {
            Instance = this;
        }

        public static ScriptSyncStart Instance { get; private set; }

        public override string EnglishName => "ScriptSyncStart";

        protected override Rhino.Commands.Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("Starting ScriptSync..");
            
            // if it is already in use by the instance of this Rhino
            if (IsRunning)
            {
                RhinoApp.WriteLine("Server already running");
                return Rhino.Commands.Result.Success;
            }

            KillExistingListener();
            
            // Try to start the server directly - if port is still in TIME_WAIT from previous run,
            // the TcpListener will handle it or we'll get a specific error
            try
            {
                _server = new TcpListener(IPAddress.Parse(Ip), Port);
                _server.Start();
                RhinoApp.WriteLine("ScriptSync listening on {0}:{1}", Ip, Port);
            }
            catch (Exception e)
            {
                // Check if it's this Rhino instance that still has the port
                if (ScriptSyncStop.Instance != null && ScriptSyncStop.Instance.IsRunning())
                {
                    RhinoApp.WriteLine("Stopping existing listener...");
                    ScriptSyncStop.Instance.Stop();
                    Thread.Sleep(500);
                    try
                    {
                        _server = new TcpListener(IPAddress.Parse(Ip), Port);
                        _server.Start();
                        RhinoApp.WriteLine("ScriptSync listening on {0}:{1}", Ip, Port);
                    }
                    catch (Exception ex)
                    {
                        RhinoApp.WriteLine("Error: " + ex.Message);
                        return Rhino.Commands.Result.Failure;
                    }
                }
                else if (e.Message.Contains("Only one usage of each socket address"))
                {
                    RhinoApp.WriteLine("Error: another process is using port " + Port);
                }
                else
                {
                    RhinoApp.WriteLine("Error: " + e.Message);
                }
                return Rhino.Commands.Result.Failure;
            }
            
            IsRunning = false;

            Thread WorkerThread = new Thread(new ThreadStart(Run));
            WorkerThread.Start();

            return Rhino.Commands.Result.Success;
        }

        private void KillExistingListener()
        {
            if (ScriptSyncStop.Instance != null && ScriptSyncStop.Instance.IsRunning())
            {
                RhinoApp.WriteLine("Stopping existing listener...");
                ScriptSyncStop.Instance.Stop();
                Thread.Sleep(500);
                RhinoApp.WriteLine("Stopped existing listener");
                return;
            }
            
            // PowerShell fallback disabled to avoid killing other Rhino instances
            // try
            // {
            //     var startInfo = new ProcessStartInfo
            //     {
            //         FileName = "powershell",
            //         Arguments = "-Command \"Get-NetTCPConnection -LocalPort " + Port + " -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }\"",
            //         UseShellExecute = false,
            //         CreateNoWindow = true
            //     };
            //     Process.Start(startInfo);
            //     Thread.Sleep(1000);
            // }
            // catch (Exception ex)
            // {
            //     RhinoApp.WriteLine("Warning: Could not free port: " + ex.Message);
            // }
        }

        /// <summary>
        /// It is called on a thread to run the server and listen for incoming paths to run.
        /// </summary>
        public void Run()
        {
            // Enable SO_REUSEADDR to allow reusing the port after restart
            _server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _server.Start();
            IsRunning = true;

            // Run init Python file to warm up Python3 in Rhino
            string initScriptPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "__scriptsync_init__.py");
            if (!System.IO.File.Exists(initScriptPath))
            {
                System.IO.File.WriteAllText(initScriptPath, "#! python3\nimport sys; print('ScriptSync: Python', sys.version.split()[0], 'ready', flush=True)", Encoding.UTF8);
            }
            if (!System.IO.File.Exists(initScriptPath))
            {
                RhinoApp.WriteLine("ScriptSync: init script missing, skipping warm-up: " + initScriptPath);
            }
            else
            {
                RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    try { RhinoApp.RunScript("_-ScriptEditor _Run \"" + initScriptPath + "\"", true); }
                    catch (Exception ex) { RhinoApp.WriteLine("ScriptSync warning: could not run init script: " + ex.Message); }
                }));
            }

            while (IsRunning)
            {
                TcpClient client = _server.AcceptTcpClient();
                client.NoDelay = true;
                byte[] data = new byte[4096];
                NetworkStream stream = client.GetStream();
                int bytesRead = stream.Read(data, 0, data.Length);
                string scriptPath = Encoding.UTF8.GetString(data, 0, bytesRead).Trim();

                if (bytesRead == 0 || scriptPath == "__SCRIPTSYNC_STOP__")
                {
                    IsRunning = false;
                    break;
                }

                string resultJson = "{\"success\":true,\"error\":\"\"}";
                string cleanPath = scriptPath.Trim();
                if (cleanPath.StartsWith("/"))
                    cleanPath = cleanPath.Substring(1);
                cleanPath = cleanPath.Replace("/", "\\");

                string scriptExt = System.IO.Path.GetExtension(cleanPath).ToLower();
                string scriptDir = System.IO.Path.GetDirectoryName(cleanPath);
                string scriptName = System.IO.Path.GetFileNameWithoutExtension(cleanPath);
                string errorFilePath = cleanPath + ".error";

                if (scriptExt == ".py")
                {
                    try { if (File.Exists(errorFilePath)) File.Delete(errorFilePath); } catch (Exception ex) { RhinoApp.WriteLine("ScriptSync warning: could not delete error file: " + ex.Message); }

                    string wrappedScriptPath = System.IO.Path.Combine(scriptDir, ".__scsy_wrapper__.py");
                    string originalCode = File.ReadAllText(cleanPath, Encoding.UTF8);
                    string shebang = "";
                    if (originalCode.StartsWith("#!"))
                    {
                        int newlineIdx = originalCode.IndexOf('\n');
                        shebang = originalCode.Substring(0, newlineIdx + 1);
                        originalCode = originalCode.Substring(newlineIdx + 1);
                    }

                    // Hoist `from __future__` imports to the top of the file.
                    // CPython requires these to be the first statements in a module
                    // (after the optional shebang / module docstring).
                    // We capture them in `futureImports` and strip them out of `originalCode`,
                    // then prepend `futureImports` separately when assembling the wrapper.
                    var futureMatches = System.Text.RegularExpressions.Regex.Matches(
                        originalCode,
                        @"^[ \t]*from __future__[^\n]*\n",
                        System.Text.RegularExpressions.RegexOptions.Multiline);
                    string futureImports = "";
                    foreach (System.Text.RegularExpressions.Match m in futureMatches)
                        futureImports += m.Value;
                    if (futureMatches.Count > 0)
                        originalCode = originalCode.Replace(futureImports, "");

                    // First write original code and check syntax with py_compile
                    File.WriteAllText(wrappedScriptPath, shebang + futureImports + originalCode, Encoding.UTF8);

                    string syntaxCheckRunner = "import py_compile\n" +
                        "try:\n" +
                        "    py_compile.compile(r\"" + wrappedScriptPath.Replace("\\", "\\\\") + "\", doraise=True)\n" +
                        "except py_compile.PyCompileError as e:\n" +
                        "    with open(r\"" + errorFilePath.Replace("\\", "\\\\") + "\", 'w') as f:\n" +
                        "        f.write('SyntaxError: ' + str(e))\n" +
                        "    import sys\n" +
                        "    sys.exit(1)\n";

                    string syntaxCheckRunnerPath = System.IO.Path.Combine(scriptDir, ".__scsy_syntax_runner__.py");
                    File.WriteAllText(syntaxCheckRunnerPath, syntaxCheckRunner, Encoding.UTF8);

                    // Run syntax check synchronously by checking file existence after delay
                    bool syntaxError = false;
                    if (!System.IO.File.Exists(syntaxCheckRunnerPath))
                    {
                        RhinoApp.WriteLine("ScriptSync: syntax check runner missing, skipping: " + syntaxCheckRunnerPath);
                    }
                    else
                    {
                        RhinoApp.InvokeOnUiThread(new Action(() =>
                        {
                            try { RhinoApp.RunScript("_-ScriptEditor _Run \"" + syntaxCheckRunnerPath + "\"", true); }
                            catch (Exception ex) { RhinoApp.WriteLine("ScriptSync warning: could not run syntax check: " + ex.Message); }
                        }));
                    }
                    Thread.Sleep(300);
                    try { if (File.Exists(syntaxCheckRunnerPath)) File.Delete(syntaxCheckRunnerPath); } catch (Exception ex) { RhinoApp.WriteLine("ScriptSync warning: could not delete syntax check file: " + ex.Message); }

                    if (File.Exists(errorFilePath))
                    {
                        syntaxError = true;
                        string errorContent = File.ReadAllText(errorFilePath, Encoding.UTF8);
                        RhinoApp.WriteLine("ScriptSync: SyntaxError in " + scriptName + scriptExt + "\n" + errorContent);
                        resultJson = "{\"success\":false,\"error\":\"" + EscapeJsonString(errorContent) + "\"}";
                    }

                    if (syntaxError)
                    {
                        // Clean up and return error
                        try { if (File.Exists(wrappedScriptPath)) File.Delete(wrappedScriptPath); } catch (Exception ex) { RhinoApp.WriteLine("ScriptSync warning: could not delete wrapper script: " + ex.Message); }
                        byte[] syntaxErrorResponse = Encoding.ASCII.GetBytes(resultJson);
                        try { stream.Write(syntaxErrorResponse, 0, syntaxErrorResponse.Length); stream.Flush(); } catch (Exception ex) { RhinoApp.WriteLine("ScriptSync warning: could not send syntax error response: " + ex.Message); }
                        client.Close();
                        continue;
                    }

                    // Now wrap with try/except for runtime errors.
                    // Keep try/except wrapper unconditional so empty / future-only
                    // scripts still capture errors to *.py.error.
                    string wrappedCode = shebang +
                        futureImports +
                        "import sys, traceback\n" +
                        "try:\n" +
                        "    " + originalCode.Replace("\n", "\n    ") + "\n" +
                        "except Exception:\n" +
                        "    with open(r\"" + errorFilePath + "\", 'w') as f:\n" +
                        "        f.write(traceback.format_exc())\n" +
                        "    raise\n";

                    File.WriteAllText(wrappedScriptPath, wrappedCode, Encoding.UTF8);

                    if (!System.IO.File.Exists(wrappedScriptPath))
                    {
                        RhinoApp.WriteLine("ScriptSync: wrapped script missing, skipping run: " + wrappedScriptPath);
                        resultJson = "{\"success\":false,\"error\":\"wrapper script missing\"}";
                    }
                    else
                    {
                        RhinoApp.InvokeOnUiThread(new Action(() =>
                        {
                            try
                            {
                                RhinoApp.RunScript("_-ScriptEditor _Run \"" + wrappedScriptPath + "\"", true);
                            }
                            catch (Exception ex) { RhinoApp.WriteLine("ScriptSync warning: could not run wrapped script: " + ex.Message); }
                        }));
                    }

                    Thread.Sleep(500);

                    // Retry wrapper deletion with exponential backoff to handle slow script close
                    int maxRetries = 5;
                    int delayMs = 100;
                    for (int retry = 0; retry < maxRetries; retry++)
                    {
                        try
                        {
                            if (!File.Exists(wrappedScriptPath)) break;
                            File.Delete(wrappedScriptPath);
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (retry < maxRetries - 1)
                            {
                                Thread.Sleep(delayMs);
                                delayMs *= 2;
                            }
                            else
                            {
                                RhinoApp.WriteLine("ScriptSync warning: could not delete wrapper script after " + maxRetries + " attempts: " + ex.Message);
                            }
                        }
                    }
                }
                else
                {
                    if (!System.IO.File.Exists(cleanPath))
                    {
                        RhinoApp.WriteLine("ScriptSync: script not found, skipping: " + cleanPath);
                        resultJson = "{\"success\":false,\"error\":\"file not found\"}";
                    }
                    else
                    {
                        RhinoApp.InvokeOnUiThread(new Action(() =>
                        {
                            try
                            {
                                RhinoApp.RunScript("_-ScriptEditor _Run \"" + cleanPath + "\"", true);
                            }
                            catch (Exception ex) { RhinoApp.WriteLine("ScriptSync warning: could not run script: " + ex.Message); }
                        }));
                    }
                }

                Thread.Sleep(100);

                byte[] responseBytes = Encoding.ASCII.GetBytes(resultJson);
                try
                {
                    stream.Write(responseBytes, 0, responseBytes.Length);
                    stream.Flush();
                }
                catch (Exception ex) { RhinoApp.WriteLine("ScriptSync warning: could not send response: " + ex.Message); }

                client.Close();
            }
            _server.Stop();
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                RhinoApp.WriteLine("ScriptSync stopped");
            }));
        }

        private string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The ScriptEditor on a thread needs a dry run to be able to run scripts.
        /// </summary>
        /// <returns> true if the dry run is ok </returns>
        private bool IsScriptEditorRunnerFromThreadOk()
        {
            string cPyScriptPath = System.IO.Path.GetFullPath(@"./temp/cpy_version.py");
            string ironPyScriptPath = System.IO.Path.GetFullPath(@"./temp/ironpy_version.py");
            string csScriptPath = System.IO.Path.GetFullPath(@"./temp/CsVersion.cs");

            System.IO.File.WriteAllText(cPyScriptPath, "#! python3\nimport sys\nprint(sys.version)");
            System.IO.File.WriteAllText(ironPyScriptPath, "#! python2\nimport sys\nprint(sys.version)");
            System.IO.File.WriteAllText(csScriptPath, "using System;\n\nCsVersion.Main();\n\nclass CsVersion\n{\n\tstatic public void Main()\n\t{\n\t\tConsole.WriteLine(\"C# Runtime: \" + Environment.Version.ToString());\n\t\tConsole.WriteLine(\"platform: \" + Environment.OSVersion.ToString());\n\t}\n}");

            bool cPyIsRunning = RhinoApp.RunScript("_-ScriptEditor Run " + cPyScriptPath, true);
            bool ironPyIsRunning = RhinoApp.RunScript("_-ScriptEditor Run " + ironPyScriptPath, true);
            bool csIsRunning = RhinoApp.RunScript("_-ScriptEditor Run " + csScriptPath, true);

            System.IO.File.Delete(cPyScriptPath);
            System.IO.File.Delete(ironPyScriptPath);
            System.IO.File.Delete(csScriptPath);

            if (!cPyIsRunning || !ironPyIsRunning || !csIsRunning)
                return false;
            return true;
        }
    }
}
