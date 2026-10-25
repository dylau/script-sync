using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ScriptSync
{
    /// <summary>
    /// Describes one running Rhino instance that hosts a ScriptSync listener.
    /// </summary>
    public class ScriptSyncInstance
    {
        public int Pid { get; set; }
        public int Port { get; set; }
        public string Title { get; set; }
        public string DocumentPath { get; set; }
        public string StartedAt { get; set; }

        /// <summary>
        /// A short human readable label, used by the VSCode instance picker.
        /// Falls back to the port when Rhino has no document open yet.
        /// It is computed, not stored, so it is not part of the registry file.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public string DisplayLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(Title))
                    return Title;
                return "Rhino (port " + Port + ")";
            }
        }
    }

    /// <summary>
    /// A shared registry of the Rhino instances that currently listen for scripts.
    ///
    /// The registry lives in a single JSON file so that the VSCode extension (and the
    /// standalone python helpers) can discover which Rhino instances are available and
    /// on which port each one listens. Every Rhino instance owns exactly one entry,
    /// keyed by its process id.
    ///
    /// Several Rhino instances may write to this file concurrently, so all writes go
    /// through a lock file and are performed atomically (write temp, then replace).
    /// </summary>
    public static class ScriptSyncInstanceRegistry
    {
        private const string DirectoryName = "script-sync";
        private const string FileName = "instances.json";
        private const string LockFileName = "instances.lock";
        private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The folder holding the registry file.
        /// </summary>
        public static string RegistryDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DirectoryName); }
        }

        /// <summary>
        /// The full path of the registry file.
        /// </summary>
        public static string RegistryPath
        {
            get { return Path.Combine(RegistryDirectory, FileName); }
        }

        private static string LockPath
        {
            get { return Path.Combine(RegistryDirectory, LockFileName); }
        }

        /// <summary>
        /// Register (or refresh) the entry of the current Rhino process.
        /// </summary>
        /// <param name="port">The TCP port this instance listens on.</param>
        /// <param name="title">The Rhino window / document title.</param>
        /// <param name="documentPath">The full path of the open document, if any.</param>
        public static void Register(int port, string title, string documentPath)
        {
            int pid = CurrentProcessId();
            EnsureDirectory();

            // The read and the write must happen under the same lock, otherwise two
            // Rhino instances starting at the same time would both read the old list
            // and the second write would drop the entry of the first one.
            using (FileStream stream = AcquireLock())
            {
                try
                {
                    List<ScriptSyncInstance> instances = ReadAllUnlocked();
                    instances.RemoveAll(i => i.Pid == pid);

                    ScriptSyncInstance instance = new ScriptSyncInstance
                    {
                        Pid = pid,
                        Port = port,
                        Title = title ?? string.Empty,
                        DocumentPath = documentPath ?? string.Empty,
                        StartedAt = DateTime.UtcNow.ToString("o")
                    };
                    instances.Add(instance);

                    WriteAllUnlocked(instances);
                }
                finally
                {
                    ReleaseLock(stream);
                }
            }
        }

        /// <summary>
        /// Remove the entry belonging to the current Rhino process.
        /// </summary>
        public static void UnregisterCurrent()
        {
            int pid = CurrentProcessId();
            EnsureDirectory();

            using (FileStream stream = AcquireLock())
            {
                try
                {
                    List<ScriptSyncInstance> instances = ReadAllUnlocked();
                    int removed = instances.RemoveAll(i => i.Pid == pid);
                    if (removed > 0)
                        WriteAllUnlocked(instances);
                }
                finally
                {
                    ReleaseLock(stream);
                }
            }
        }

        /// <summary>
        /// Read all the registered instances, dropping the ones whose process is gone.
        /// Stale entries are removed because a crashed Rhino never runs ScriptSyncStop.
        /// </summary>
        public static List<ScriptSyncInstance> ReadLive()
        {
            List<ScriptSyncInstance> instances = ReadAll();
            List<ScriptSyncInstance> live = instances.Where(IsProcessAlive).ToList();

            // Persist the pruning so the file does not grow forever.
            if (live.Count != instances.Count)
            {
                try { WriteAll(live); } catch { /* best effort */ }
            }
            return live;
        }

        /// <summary>
        /// Read the registry file without pruning stale entries.
        /// </summary>
        public static List<ScriptSyncInstance> ReadAll()
        {
            EnsureDirectory();
            using (FileStream stream = AcquireLock())
            {
                try
                {
                    return ReadAllUnlocked();
                }
                finally
                {
                    ReleaseLock(stream);
                }
            }
        }

        /// <summary>
        /// Read the registry file. The caller must hold the lock.
        /// </summary>
        private static List<ScriptSyncInstance> ReadAllUnlocked()
        {
            List<ScriptSyncInstance> instances = new List<ScriptSyncInstance>();
            try
            {
                if (!File.Exists(RegistryPath))
                    return instances;

                string content = File.ReadAllText(RegistryPath, Encoding.UTF8);
                if (string.IsNullOrEmpty(content))
                    return instances;

                Newtonsoft.Json.Linq.JArray array = Newtonsoft.Json.Linq.JArray.Parse(content);
                foreach (Newtonsoft.Json.Linq.JToken token in array)
                {
                    ScriptSyncInstance instance = token.ToObject<ScriptSyncInstance>();
                    if (instance != null)
                        instances.Add(instance);
                }
            }
            catch (Exception e)
            {
                // A corrupt registry must never prevent Rhino from starting.
                Log("could not read instance registry: " + e.Message);
            }
            return instances;
        }

        /// <summary>
        /// Log a message to the Rhino command line, falling back to the debug output
        /// when Rhino is not available (for instance when the registry is used
        /// outside of a running Rhino session).
        /// </summary>
        private static void Log(string message)
        {
            try
            {
                Rhino.RhinoApp.WriteLine("ScriptSync: " + message);
            }
            catch (Exception)
            {
                System.Diagnostics.Debug.WriteLine("ScriptSync: " + message);
            }
        }

        private static void WriteAll(List<ScriptSyncInstance> instances)
        {
            EnsureDirectory();
            using (FileStream stream = AcquireLock())
            {
                try
                {
                    WriteAllUnlocked(instances);
                }
                finally
                {
                    ReleaseLock(stream);
                }
            }
        }

        /// <summary>
        /// Write the registry file. The caller must hold the lock.
        /// </summary>
        private static void WriteAllUnlocked(List<ScriptSyncInstance> instances)
        {
            EnsureDirectory();
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(instances, Newtonsoft.Json.Formatting.Indented);
            // Write to a temporary file first, then swap it in, so that a reader
            // never observes a half-written registry.
            string tempPath = RegistryPath + ".tmp." + CurrentProcessId();
            File.WriteAllText(tempPath, json, Encoding.UTF8);

            if (File.Exists(RegistryPath))
                File.Delete(RegistryPath);
            File.Move(tempPath, RegistryPath);
        }

        private static void EnsureDirectory()
        {
            if (!Directory.Exists(RegistryDirectory))
                Directory.CreateDirectory(RegistryDirectory);
        }

        /// <summary>
        /// Acquire an exclusive lock on the registry. Returns a stream that must be
        /// passed to ReleaseLock.
        ///
        /// When the lock cannot be taken within the timeout the stream is still
        /// returned, but opened in a mode that does not exclude other writers. This
        /// is a last resort: the registry is a convenience, so a stuck lock must
        /// never block Rhino from starting.
        /// </summary>
        private static FileStream AcquireLock()
        {
            EnsureDirectory();
            DateTime deadline = DateTime.UtcNow + LockTimeout;
            while (true)
            {
                try
                {
                    FileStream stream = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    return stream;
                }
                catch (IOException)
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        // Give up waiting for the lock rather than hanging Rhino.
                        return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                    }
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                }
            }
        }

        private static void ReleaseLock(FileStream stream)
        {
            try { stream.Close(); } catch { /* ignore */ }
        }

        /// <summary>
        /// Check whether the process of an instance is still running.
        /// </summary>
        public static bool IsProcessAlive(ScriptSyncInstance instance)
        {
            if (instance == null || instance.Pid <= 0)
                return false;
            try
            {
                Process process = Process.GetProcessById(instance.Pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                // No process with that id.
                return false;
            }
            catch (Exception)
            {
                // Access denied or similar: assume alive so we do not drop a valid entry.
                return true;
            }
        }

        private static int CurrentProcessId()
        {
            try { return Process.GetCurrentProcess().Id; }
            catch { return 0; }
        }
    }
}
