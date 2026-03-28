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
        public int Port = 58259;

        public ScriptSyncStart()
        {
            Instance = this;
        }

        public static ScriptSyncStart Instance { get; private set; }

        public override string EnglishName => "ScriptSyncStart";

        protected override Rhino.Commands.Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("Starting ScriptSync..");
            // check if the IP is already in use
            try
            {
                TcpListener check = new TcpListener(IPAddress.Parse(Ip), Port);
                check.Start();
                check.Stop();
            }
            catch (Exception e)
            {
                if (e.Message.Contains("Only one usage of each socket address"))
                {
                    RhinoApp.WriteLine("Error: there are two instances of Rhino running script-sync, only one is allowed.");
                }
                else
                {
                    RhinoApp.WriteLine("Error: " + e.Message);
                }
                return Rhino.Commands.Result.Failure;
            }
            
            // if it is already in use by the instance of this Rhino
            if (IsRunning)
            {
                RhinoApp.WriteLine("Server already running");
                return Rhino.Commands.Result.Success;
            }
            _server = new TcpListener(IPAddress.Parse(Ip), Port);
            IsRunning = false;

            Thread WorkerThread = new Thread(new ThreadStart(Run));
            WorkerThread.Start();

            return Rhino.Commands.Result.Success;
        }

        /// <summary>
        /// It is called on a thread to run the server and listen for incoming paths to run.
        /// </summary>
        public void Run()
        {
            _server.Start();
            IsRunning = true;

            while (IsRunning)
            {
                TcpClient client = _server.AcceptTcpClient();
                client.NoDelay = true;
                byte[] data = new byte[4096];
                NetworkStream stream = client.GetStream();
                int bytesRead = stream.Read(data, 0, data.Length);
                string scriptPath = Encoding.ASCII.GetString(data, 0, bytesRead).Trim();

                if (bytesRead == 0)
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
                    try { if (File.Exists(errorFilePath)) File.Delete(errorFilePath); } catch { }

                    string wrappedScriptPath = System.IO.Path.Combine(scriptDir, ".__scsy_wrapper__.py");
                    string originalCode = File.ReadAllText(cleanPath, Encoding.UTF8);
                    string shebang = "";
                    if (originalCode.StartsWith("#!"))
                    {
                        int newlineIdx = originalCode.IndexOf('\n');
                        shebang = originalCode.Substring(0, newlineIdx + 1);
                        originalCode = originalCode.Substring(newlineIdx + 1);
                    }

                    string wrappedCode = shebang + 
                        "import sys, traceback\n" +
                        "try:\n" +
                        "    " + originalCode.Replace("\n", "\n    ") + "\n" +
                        "except Exception:\n" +
                        "    with open(r\"" + errorFilePath + "\", 'w') as f:\n" +
                        "        f.write(traceback.format_exc())\n" +
                        "    raise\n";

                    File.WriteAllText(wrappedScriptPath, wrappedCode, Encoding.UTF8);

                    RhinoApp.InvokeOnUiThread(new Action(() =>
                    {
                        try
                        {
                            RhinoApp.RunScript("_-ScriptEditor _Run \"" + wrappedScriptPath + "\"", true);
                        }
                        catch { }
                    }));

                    Thread.Sleep(500);

                    try { if (File.Exists(wrappedScriptPath)) File.Delete(wrappedScriptPath); } catch { }
                }
                else
                {
                    RhinoApp.InvokeOnUiThread(new Action(() =>
                    {
                        try
                        {
                            RhinoApp.RunScript("_-ScriptEditor _Run \"" + cleanPath + "\"", true);
                        }
                        catch { }
                    }));
                }

                Thread.Sleep(100);

                byte[] responseBytes = Encoding.ASCII.GetBytes(resultJson);
                try
                {
                    stream.Write(responseBytes, 0, responseBytes.Length);
                    stream.Flush();
                }
                catch { }

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
