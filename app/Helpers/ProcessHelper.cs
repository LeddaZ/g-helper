using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace GHelper.Helpers
{
    public static class ProcessHelper
    {
        // Session scoped on purpose. A Global\ name lets an instance in another logon session
        // (or any other process on the machine) terminate this one.
        private const string ExitEventName = "Local\\GHelperApp-Exit";
        private const string StartupMutexName = "Local\\GHelperApp-Startup";
        private static EventWaitHandle? exitEvent;
        private static long lastAdmin;

        private static readonly Lazy<bool> _isSystem = new Lazy<bool>(() =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.IsSystem;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        public static bool IsRunningAsSystem() => _isSystem.Value;

        public static void CheckAlreadyRunning()
        {
            // Serialise the whole handover. Two instances starting at the same time used to
            // enumerate each other and both call Kill(), leaving no instance running at all.
            Mutex? startupMutex = null;
            bool holdsMutex = false;

            try
            {
                startupMutex = new Mutex(false, StartupMutexName);
                try { holdsMutex = startupMutex.WaitOne(TimeSpan.FromSeconds(10)); }
                catch (AbandonedMutexException) { holdsMutex = true; }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Startup mutex failed: " + ex.Message);
            }

            try
            {
                TakeOver();
            }
            finally
            {
                if (holdsMutex)
                {
                    try { startupMutex?.ReleaseMutex(); } catch { }
                }
                startupMutex?.Dispose();
            }
        }

        private static void TakeOver()
        {
            bool created = OpenExitEvent();
            bool signalled = false;

            // Ask a running instance to quit. Left signalled on purpose - the old code reset it
            // immediately, so the incumbent could miss the pulse entirely. We reset it below,
            // once the others are gone and before we start waiting on it ourselves.
            if (!created && exitEvent is not null)
            {
                try
                {
                    exitEvent.Set();
                    signalled = true;
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Broadcast exit failed: " + ex.Message);
                    exitEvent = null;
                }
            }

            if (!KillOtherInstances(signalled)) return;

            if (exitEvent is not null)
            {
                try
                {
                    exitEvent.Reset();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Can't reset exit event: " + ex.Message);
                    exitEvent = null;
                }
            }

            if (exitEvent is not null)
                ThreadPool.RegisterWaitForSingleObject(exitEvent, (_, _) =>
                {
                    Logger.WriteLine("Quitting: another instance took over");
                    Application.Exit();
                }, null, Timeout.Infinite, true);
        }

        /// <returns>False if this instance should give up and stop starting</returns>
        private static bool KillOtherInstances(bool signalled)
        {
            using Process currentProcess = Process.GetCurrentProcess();
            int currentSession = currentProcess.SessionId;

            Process[] processes = Process.GetProcessesByName(currentProcess.ProcessName);
            try
            {
                var others = new List<Process>();
                foreach (Process process in processes)
                {
                    if (process.Id == currentProcess.Id) continue;

                    // An instance in another logon session is not ours to kill
                    try { if (process.SessionId != currentSession) continue; }
                    catch { continue; }

                    others.Add(process);
                }

                if (others.Count == 0) return true;

                // We just asked them to quit, give them a moment before resorting to Kill
                if (signalled)
                    for (int i = 0; i < 20 && others.Exists(p => !HasExited(p)); i++)
                        Thread.Sleep(100);

                var failed = new List<Process>();
                foreach (Process process in others)
                {
                    if (HasExited(process)) continue;
                    try
                    {
                        process.Kill();
                        Logger.WriteLine($"Stopped previous instance PID {process.Id}");
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine($"Can't kill PID {process.Id}: {ex.Message}");
                        failed.Add(process);
                    }
                }

                if (failed.Count == 0) return true;

                Thread.Sleep(2000);

                foreach (var p in failed)
                    if (!HasExited(p))
                    {
                        Logger.WriteLine($"Quitting: PID {p.Id} is still running and can't be stopped");
                        MessageBox.Show(Properties.Strings.AppAlreadyRunningText, Properties.Strings.AppAlreadyRunning, MessageBoxButtons.OK);
                        Application.Exit();
                        return false;
                    }

                return true;
            }
            finally
            {
                foreach (Process p in processes) p.Dispose();
            }
        }

        private static bool OpenExitEvent()
        {
            bool created = false;
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();

                var sec = new EventWaitHandleSecurity();
                sec.AddAccessRule(new EventWaitHandleAccessRule(
                    identity.User!,
                    EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify,
                    AccessControlType.Allow));

                exitEvent = EventWaitHandleAcl.Create(false, EventResetMode.ManualReset, ExitEventName, out created, sec);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Can't create exit event: " + ex.Message);
                try { exitEvent = EventWaitHandle.OpenExisting(ExitEventName); }
                catch { exitEvent = null; }
            }

            return created;
        }

        private static bool HasExited(Process process)
        {
            try { return process.HasExited; }
            catch { return false; }
        }

        public static bool IsUserAdministrator()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static void RunAsAdmin(string? param = null, bool force = false)
        {

            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeMilliseconds() - lastAdmin) < 2000) return;
            lastAdmin = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            // Check if the current user is an administrator
            if (!IsUserAdministrator() || force)
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.UseShellExecute = true;
                startInfo.WorkingDirectory = Environment.CurrentDirectory;
                startInfo.FileName = Application.ExecutablePath;
                startInfo.Arguments = param;
                startInfo.Verb = "runas";
                try
                {
                    Process.Start(startInfo);
                    Logger.WriteLine($"Quitting: relaunching as admin ({(string.IsNullOrEmpty(param) ? "no args" : param)})");
                    Application.Exit();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                }
            }
        }


        public static void KillByName(string name)
        {
            int currentPid = Environment.ProcessId;
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in processes)
                {
                    // Callers pass names discovered at runtime (GPU app lists), never kill ourselves
                    if (process.Id == currentPid) continue;

                    try
                    {
                        process.Kill();
                        Logger.WriteLine($"Stopped: {process.ProcessName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine($"Failed to stop: {process.ProcessName} {ex.Message}");
                    }
                }
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }

        public static void KillSmartDisplayControl()
        {
            KillByName("ASUSSmartDisplayControl");
        }

        public static void KillByProcess(Process process)
        {
            try
            {
                process.Kill();
                Logger.WriteLine($"Stopped: {process.ProcessName}");
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Failed to stop: {process.ProcessName} {ex.Message}");
            }
        }

        public static void StopDisableService(string serviceName, string disable = "Disabled")
        {
            try
            {
                string script = $"Get-Service -Name \"{serviceName}\" | Stop-Service -Force -PassThru | Set-Service -StartupType {disable}";
                Logger.WriteLine(script);
                RunCMD("powershell", script);
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.ToString());
            }
        }

        public static void StartEnableService(string serviceName, bool automatic = true)
        {
            try
            {
                string script = $"Set-Service -Name \"{serviceName}\" -Status running" + (automatic? " -StartupType Automatic":"");
                Logger.WriteLine(script);
                RunCMD("powershell", script);
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.ToString());
            }
        }

        public static string RunCMD(string name, string args, string? directory = null, int timeoutMs = 0)
        {
            using var cmd = new Process();
            cmd.StartInfo.UseShellExecute = false;
            cmd.StartInfo.CreateNoWindow = true;
            cmd.StartInfo.RedirectStandardOutput = true;
            cmd.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            cmd.StartInfo.FileName = name;
            cmd.StartInfo.Arguments = args;
            if (directory != null) cmd.StartInfo.WorkingDirectory = directory;
            cmd.Start();

            var watch = Stopwatch.StartNew();
            string result;

            if (timeoutMs > 0)
            {
                var readTask = cmd.StandardOutput.ReadToEndAsync();
                if (!readTask.Wait(timeoutMs))
                {
                    try { cmd.Kill(entireProcessTree: true); } catch { }
                    watch.Stop();
                    Logger.WriteLine(name + " " + args);
                    Logger.WriteLine($"{watch.ElapsedMilliseconds} ms: TIMEOUT after {timeoutMs} ms");
                    return string.Empty;
                }
                result = readTask.Result.Replace(Environment.NewLine, " ").Trim(' ');
            }
            else
            {
                result = cmd.StandardOutput.ReadToEnd().Replace(Environment.NewLine, " ").Trim(' ');
            }

            watch.Stop();
            Logger.WriteLine(name + " " + args);
            Logger.WriteLine(watch.ElapsedMilliseconds + " ms: " + result);
            cmd.WaitForExit();

            return result;
        }

        public static void SetPriority(ProcessPriorityClass priorityClass = ProcessPriorityClass.Normal)
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                    p.PriorityClass = priorityClass;
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.ToString());
            }
        }


    }
}
