using System;
using System.Net;
using System.Net.Sockets;

namespace ScriptSync
{
    /// <summary>
    /// Helpers to find a free TCP port for a Rhino instance.
    ///
    /// The first port of the range is preferred so that a single Rhino instance keeps
    /// the historic behaviour (and every existing tool that assumes port 58258).
    /// </summary>
    public static class ScriptSyncPortAllocator
    {
        /// <summary>
        /// The first port of the range that Rhino instances try to bind.
        /// </summary>
        public const int DefaultPort = 58258;

        /// <summary>
        /// The last port of the range that Rhino instances try to bind.
        /// </summary>
        public const int MaxPort = 58279;

        /// <summary>
        /// Find the first port in the range that can be bound on the loopback address.
        /// </summary>
        /// <param name="ip">The address to bind to.</param>
        /// <param name="startPort">The first port to try.</param>
        /// <param name="endPort">The last port to try (inclusive).</param>
        /// <param name="chosenPort">The port that was successfully bound, or -1.</param>
        /// <returns>True when a port could be bound.</returns>
        public static bool TryFindFreePort(string ip, int startPort, int endPort, out int chosenPort)
        {
            chosenPort = -1;
            for (int port = startPort; port <= endPort; port++)
            {
                TcpListener probe = null;
                try
                {
                    probe = new TcpListener(IPAddress.Parse(ip), port);
                    probe.Start();
                    probe.Stop();
                    chosenPort = port;
                    return true;
                }
                catch (SocketException)
                {
                    // Port already in use, try the next one.
                }
                catch (Exception)
                {
                    // Any other failure (invalid address, permissions): skip this port.
                }
                finally
                {
                    if (probe != null)
                    {
                        try { probe.Stop(); } catch { /* ignore */ }
                        try { probe.Server.Close(); } catch { /* ignore */ }
                    }
                }
            }
            return false;
        }
    }
}
