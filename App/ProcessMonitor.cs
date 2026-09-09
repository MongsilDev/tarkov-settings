using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace tarkov_settings
{
    static class NativeMethods
    {
        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;

        public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        public static WinEventDelegate dele = null;

        private static IntPtr m_hhook;

        public static void SetHook()
        {
            m_hhook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                dele,
                0, 0, WINEVENT_OUTOFCONTEXT | 2);
        }

        public static void UnHook()
        {
            UnhookWinEvent(m_hhook);
        }

        #region Win32 API Calls
        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);
        #endregion

        public static int GetWindowProcessId(IntPtr hWnd)
        {
            GetWindowThreadProcessId(hWnd, out uint processID);
            return (int)processID;
        }
        // direct handle query: Process.ProcessName would parse a full system
        // snapshot on every foreground switch
        public static string GetActiveWindowTitle()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                GetWindowThreadProcessId(handle, out uint processID);
                if (processID == 0)
                    return null;

                IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)processID);
                if (process == IntPtr.Zero)
                    return null;
                try
                {
                    var path = new StringBuilder(1024);
                    int size = path.Capacity;
                    if (!QueryFullProcessImageName(process, 0, path, ref size))
                        return null;
                    return Path.GetFileNameWithoutExtension(path.ToString());
                }
                finally
                {
                    CloseHandle(process);
                }
            }
            catch
            {
                return null;
            }
        }
    }
    class ProcessMonitor
    {
        private NativeMethods.WinEventDelegate processHook;

        private readonly ColorController cController = ColorController.Instance;

        private HashSet<string> pTargets = new HashSet<string>();

        // the target process that most recently took focus; 0 when none is focused
        public int FocusedTargetPid { get; private set; }
        public string FocusedTargetName { get; private set; }
        public IntPtr FocusedTargetHwnd { get; private set; }

        #region Singleton Pattern implement
        private static readonly Lazy<ProcessMonitor> instance =
            new Lazy<ProcessMonitor>(() => new ProcessMonitor());

        public static ProcessMonitor Instance
        {
            get
            {
                return instance.Value;
            }
        }
        #endregion

        public MainForm Parent { get; set; }

        private ProcessMonitor() { }

        public void Add(string process)
        {
            this.pTargets.Add(process);
        }

        public void Remove(string process)
        {
            this.pTargets.Remove(process);
        }

        public bool IsTarget(string pName)
        {
            return this.pTargets.Contains(pName);
        }

        public void Init()
        {
            processHook = new NativeMethods.WinEventDelegate(WinEventProc);
            NativeMethods.dele += processHook;
            NativeMethods.SetHook();

            // Init ColorController
            cController.Init();
        }

        /**
         * Window Focus changed Event Handler
         */
        public void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // null when the focused process exited mid-switch
            string pName = NativeMethods.GetActiveWindowTitle();

            Console.WriteLine("Running Tasks : {0}", GetWorkingThreads());
            Console.WriteLine("Focused Process : {0}", pName);

            if (pName != null && this.pTargets.Contains(pName.ToLower()) && Parent.IsEnabled)
            {
                Console.WriteLine("[pMonitor] Target Process is focused");

                FocusedTargetPid = NativeMethods.GetWindowProcessId(hWnd);
                FocusedTargetName = pName;
                FocusedTargetHwnd = hWnd;
                Parent.SetHotkeysActive(true);
                Parent.FollowWindowDisplay(hWnd);
                ApplyCurrent();
            }
            else
            {
                Console.WriteLine("[pMonitor] Target Process is not focused");

                FocusedTargetPid = 0;
                FocusedTargetName = null;
                FocusedTargetHwnd = IntPtr.Zero;
                Parent.SetHotkeysActive(false);

                // skip GDI/NVAPI calls when switching between non-target windows
                if (!cController.IsApplied)
                    return;

                cController.ChangeColorRamp(reset: true);
                cController.ResetDVL();
            }
        }

        /**
         * True only if pid still exists, carries the expected name (guards against pid
         * reuse after the game exited) and that name is a target.
         */
        public bool IsTarget(int pid, string name)
        {
            if (pid <= 0 || name == null)
                return false;
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    return process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)
                        && this.pTargets.Contains(process.ProcessName.ToLower());
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /**
         * Kill only after re-verifying pid + name at the last moment.
         */
        public bool KillTarget(int pid, string name)
        {
            if (!IsTarget(pid, name))
            {
                Console.WriteLine("[pMonitor] Refusing to kill pid {0}: not the expected target", pid);
                return false;
            }
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    process.Kill();
                }
                Console.WriteLine("[pMonitor] Killed {0} ({1})", name, pid);
                return true;
            }
            catch (Exception e)
            {
                // already exited, or access denied (e.g. game running elevated)
                Console.WriteLine("[pMonitor] Kill failed: {0}", e.Message);
                return false;
            }
        }

        /**
         * Apply the current slider values unconditionally (target focused, or display switched)
         */
        public void ApplyCurrent()
        {
            var (b, c, g, dvl) = Parent.GetColorValue();
            cController.ChangeColorRamp(brightness: b,
                                        contrast: c,
                                        gamma: g,
                                        reset: false);
            cController.DVL = dvl;
        }

        /**
         * Push the current slider values to the display while a target is focused
         */
        public void Reapply()
        {
            if (Parent == null || !cController.IsApplied)
                return;
            ApplyCurrent();
        }

        /**
         * Reset to original color settings before exit
         */
        public void Close()
        {
            Console.WriteLine("[pMonitor] Remove Delegates");
            NativeMethods.dele -= processHook;
            NativeMethods.UnHook();

            Console.WriteLine("[pMonitor] Resetting Color");
            cController.Close();
        }

        private static int GetWorkingThreads()
        {
            System.Threading.ThreadPool.GetMaxThreads(out int maxThreads, out int _);
            System.Threading.ThreadPool.GetAvailableThreads(out int availableThreads, out _);
            return maxThreads - availableThreads;
        }
    }
}
