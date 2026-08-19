using System;
using System.Collections.Generic;

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
    public class ScriptSyncStop : Command
    {
        public ScriptSyncStop()
        {
            Instance = this;
        }

        public static ScriptSyncStop Instance { get; private set; }

        public override string EnglishName => "ScriptSyncStop";

        protected override Rhino.Commands.Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Always run cleanup. If IsRunning is false, the worker thread
            // has already died but the OS socket may still be bound by this
            // Rhino process (e.g. crash without finally). The hard path in
            // Stop() releases that socket regardless of IsRunning.
            if (!ScriptSyncStart.Instance.IsRunning)
            {
                RhinoApp.WriteLine("ScriptSync not running — forcing socket release");
            }
            Stop();
            return Rhino.Commands.Result.Success;
        }

        public bool IsRunning()
        {
            return ScriptSyncStart.Instance != null && ScriptSyncStart.Instance.IsRunning;
        }

        /// <summary>
        /// Stops the listener cleanly. Always tries to release the OS-level
        /// socket so a subsequent ScriptSyncStart can rebind to the same port,
        /// even if the worker thread has died and IsRunning is stale.
        /// </summary>
        public void Stop()
        {
            ScriptSyncStart.Instance.IsRunning = false;

            // Soft path: poke the accept loop so it exits cleanly.
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    client.Connect(IPAddress.Parse(ScriptSyncStart.Instance.Ip), ScriptSyncStart.Instance.Port);
                }
            }
            catch
            {
                // Connection refused / timed out -> listener already dead.
            }

            // Hard path: directly close the TcpListener so the OS releases
            // the bound port even if the worker thread crashed without
            // running its finally block (e.g. older builds before the
            // try/finally was added).
            try
            {
                var listener = typeof(ScriptSyncStart)
                    .GetField("_server", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(ScriptSyncStart.Instance) as System.Net.Sockets.TcpListener;
                if (listener != null)
                {
                    listener.Stop();
                    listener.Server.Close();
                    RhinoApp.WriteLine("ScriptSync: socket released");
                }
                else
                {
                    RhinoApp.WriteLine("ScriptSync: _server field is null, cannot release");
                }
            }
            catch (Exception e)
            {
                RhinoApp.WriteLine("ScriptSync: error releasing socket: " + e.Message);
            }
        }
    }
}