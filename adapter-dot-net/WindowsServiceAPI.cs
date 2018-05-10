using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using static Registry;

namespace SystemControl
{
    public static class WindowsServiceAPI
    {

        #region "Win32 Service API"
        private const int STANDARD_RIGHTS_REQUIRED = 0xF0000;
        private const int SERVICE_WIN32_OWN_PROCESS = 0x00000010;

        [Flags]
        public enum ServiceManagerRights
        {
            Connect = 0x0001,
            CreateService = 0x0002,
            EnumerateService = 0x0004,
            Lock = 0x0008,
            QueryLockStatus = 0x0010,
            ModifyBootConfig = 0x0020,
            StandardRightsRequired = 0xF0000,
            AllAccess = (StandardRightsRequired | Connect | CreateService |
            EnumerateService | Lock | QueryLockStatus | ModifyBootConfig)
        }

        [Flags]
        public enum ServiceRights
        {
            QueryConfig = 0x1,
            ChangeConfig = 0x2,
            QueryStatus = 0x4,
            EnumerateDependants = 0x8,
            Start = 0x10,
            Stop = 0x20,
            PauseContinue = 0x40,
            Interrogate = 0x80,
            UserDefinedControl = 0x100,
            Delete = 0x00010000,
            StandardRightsRequired = 0xF0000,
            AllAccess = (StandardRightsRequired | QueryConfig | ChangeConfig |
            QueryStatus | EnumerateDependants | Start | Stop | PauseContinue |
            Interrogate | UserDefinedControl)
        }

        public enum ServiceBootFlag
        {
            Start = 0x00000000,
            SystemStart = 0x00000001,
            AutoStart = 0x00000002,
            DemandStart = 0x00000003,
            Disabled = 0x00000004
        }

        public enum ServiceState
        {
            SERVICE_STATE_UNKNOWN = -1,
            SERVICE_NOT_FOUND = 0,
            SERVICE_STOPPED = 0x00000001,
            SERVICE_START_PENDING = 0x00000002,
            SERVICE_STOP_PENDING = 0x00000003,
            SERVICE_RUNNING = 0x00000004,
            SERVICE_CONTINUE_PENDING = 0x00000005,
            SERVICE_PAUSE_PENDING = 0x00000006,
            SERVICE_PAUSED = 0x00000007,
        }

        public enum ServiceControl
        {
            Stop = 0x00000001,
            Pause = 0x00000002,
            Continue = 0x00000003,
            Interrogate = 0x00000004,
            Shutdown = 0x00000005,
            ParamChange = 0x00000006,
            NetBindAdd = 0x00000007,
            NetBindRemove = 0x00000008,
            NetBindEnable = 0x00000009,
            NetBindDisable = 0x0000000A
        }

        public enum ServiceError
        {
            Ignore = 0x00000000,
            Normal = 0x00000001,
            Severe = 0x00000002,
            Critical = 0x00000003
        }

        [StructLayout(LayoutKind.Sequential)]
        public class SERVICE_STATUS
        {
            public int dwServiceType = 0;
            public ServiceState dwCurrentState = 0;
            public int dwControlsAccepted = 0;
            public int dwWin32ExitCode = 0;
            public int dwServiceSpecificExitCode = 0;
            public int dwCheckPoint = 0;
            public int dwWaitHint = 0;
        }

        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerA", SetLastError = true)]
        private static extern IntPtr OpenSCManager(string lpMachineName, string lpDatabaseName, ServiceManagerRights dwDesiredAccess);
        [DllImport("advapi32.dll", EntryPoint = "OpenServiceA", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, ServiceRights dwDesiredAccess);
        [DllImport("advapi32.dll", EntryPoint = "CreateServiceA", SetLastError = true)]
        private static extern IntPtr CreateService(IntPtr hSCManager, string lpServiceName, string lpDisplayName, ServiceRights dwDesiredAccess, int dwServiceType, ServiceBootFlag dwStartType, ServiceError dwErrorControl, string lpBinaryPathName, string lpLoadOrderGroup, IntPtr lpdwTagId, string lpDependencies, string lp, string lpPassword);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatus(IntPtr hService, SERVICE_STATUS lpServiceStatus);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DeleteService(IntPtr hService);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(IntPtr hService, ServiceControl dwControl, SERVICE_STATUS lpServiceStatus);
        [DllImport("advapi32.dll", EntryPoint = "StartServiceA", SetLastError = true)]
        private static extern bool StartService(IntPtr hService, int dwNumServiceArgs, int lpServiceArgVectors);

        public enum SERVICE_INFO_LEVEL
        {
            DESCRIPTION = 1,
            FAILURE_ACTIONS = 2,
            FAILURE_ACTIONS_FLAG = 4,
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChangeServiceConfig2(IntPtr hService, SERVICE_INFO_LEVEL dwInfoLevel, [MarshalAs(UnmanagedType.Struct)] ref SERVICE_DESCRIPTION lpInfo);

        public enum SC_ACTION_TYPE
        {
            SC_ACTION_NONE = 0,
            SC_ACTION_RESTART = 1,
            SC_ACTION_REBOOT = 2,
            SC_ACTION_RUN_COMMAND = 3,
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SC_ACTION
        {
            [MarshalAs(UnmanagedType.U4)]
            public SC_ACTION_TYPE Type;
            [MarshalAs(UnmanagedType.U4)]
            public UInt32 Delay;

            public SC_ACTION(SC_ACTION_TYPE type, uint delay) : this()
            {
                this.Type = type;
                this.Delay = delay;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_FAILURE_ACTIONS
        {
            [MarshalAs(UnmanagedType.U4)]
            public UInt32 dwResetPeriod;
            [MarshalAs(UnmanagedType.LPStr)]
            public String lpRebootMsg;
            [MarshalAs(UnmanagedType.LPStr)]
            public String lpCommand;
            [MarshalAs(UnmanagedType.U4)]
            public UInt32 cActions;
            //[MarshalAs(UnmanagedType.)]  // pointer to C-style array
            public IntPtr lpsaActions;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChangeServiceConfig2(IntPtr hService, SERVICE_INFO_LEVEL dwInfoLevel, [MarshalAs(UnmanagedType.Struct)] ref SERVICE_FAILURE_ACTIONS lpInfo);


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_FAILURE_ACTIONS_FLAG
        {
            public bool fFailureActionsOnNonCrashFailures;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChangeServiceConfig2(IntPtr hService, SERVICE_INFO_LEVEL dwInfoLevel, [MarshalAs(UnmanagedType.Struct)] ref SERVICE_FAILURE_ACTIONS_FLAG lpInfo);


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SERVICE_DESCRIPTION
        {
            public string lpDescription;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChangeServiceConfig(IntPtr hService, UInt32 nServiceType, UInt32 nStartType, UInt32 nErrorControl, String lpBinaryPathName, 
            String lpLoadOrderGroup, IntPtr lpdwTagId, String lpDependencies, String lpServiceStartName, String lpPassword, String lpDisplayName);

        #endregion

        #region "Public Service Methods"

        /// <summary>
        /// Enumeration of services whose executable lives inside folder
        /// </summary>
        /// <param name="folder">the root folder to check</param>
        /// <returns>an enumeration of matching services (as ServiceController)</returns>
        public static IEnumerable<ServiceController> ServicesInFolder(string folder)
        {
            // figure out the executable prefix including the final directory-separator:
            var prefix = folder.TrimEnd(Path.DirectorySeparatorChar).TrimEnd(Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            // enumerate all services and filter
            return WindowsServiceAPI.Enumerate().Where(service =>
            {
                // return only those with a run command that starts with our prefix
                // ignoring a possible leading quote
                return service.GetRunCommand().TrimStart('"').StartsWith(prefix);
            });
        }

        /// <summary>
        /// (stop and) delete a service
        /// </summary>
        /// <param name="svc">ServiceController representing the service to be deleted</param>
        public static void Delete(this ServiceController svc)
        {
            Delete(svc.ServiceName);
        }

        /// <summary>
        /// Try to (stop and) then delete the named windows service
        /// </summary>
        /// <param name="ServiceName">The windows service name to delete</param>
        public static void Delete(string ServiceName)
        {
            IntPtr scman = OpenSCManager(ServiceManagerRights.Connect);
            try
            {
                IntPtr service = OpenService(scman, ServiceName, ServiceRights.StandardRightsRequired | ServiceRights.Stop | ServiceRights.Delete | ServiceRights.QueryStatus);
                if (service == IntPtr.Zero)
                {
                    if (Marshal.GetLastWin32Error() == 1060)
                    {
                        // service does not exist - so we're done before we start.
                        return;
                    }
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                try
                {
                    if (GetServiceStatus(service) != ServiceState.SERVICE_STOPPED) {
                        StopService(service);
                    }
                    if (!DeleteService(service))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    CloseServiceHandle(service);
                }
            }
            finally
            {
                CloseServiceHandle(scman);
            }
        }

        /// <summary>
        /// Returns true if a service exists
        /// </summary>
        /// <param name="name">the name of the service</param>
        /// <returns>True if there is a service with that name, false otherwise</returns>
        public static bool IsInstalled(string name)
        {
            IntPtr scman = OpenSCManager(ServiceManagerRights.Connect);
            try
            {
                IntPtr service = OpenService(scman, name, ServiceRights.QueryStatus);
                if (service != IntPtr.Zero)
                {
                    CloseServiceHandle(service);
                }
                return service != IntPtr.Zero;
            }
            finally
            {
                CloseServiceHandle(scman);
            }
        }

        //
        public static bool IsRunning(string name, int maxmstowait = 3000)
        {
            if (name == null || name == "")
            {
                return false;
            }
            ServiceState state = GetState(name);
            for (int t = 0; t < maxmstowait && (state == ServiceState.SERVICE_START_PENDING); t += 200)
            {
                Thread.Sleep(200);
                state = GetState(name);
            }
            if (state == ServiceState.SERVICE_RUNNING)
            {
                return true;
            }
            return false;
        }

        public class ServiceSpec
        {
            public string ServiceName = null;
            public string DisplayName = null;
            public string Description = null;
            public ServiceRights Rights = ServiceRights.AllAccess;
            public int ServiceType = SERVICE_WIN32_OWN_PROCESS;
            public ServiceBootFlag BootType = ServiceBootFlag.AutoStart;
            public ServiceError ErrorControl = ServiceError.Normal;
            public string ExePath = null;
            public string LoadOrderGroup = null;
            public string[] Dependencies = null;
            public string Account = null;
            public string Password = null;
            public SC_ACTION[] recoveryActions = null;
            public uint resetFailCountAfterSeconds = 0;
            public bool enableActionsForStopsWithErrors = false;
        }

        public static void Install(ServiceSpec spec)
        {
            if (spec.ServiceName == null) throw new ArgumentNullException("ServiceSpec.ServiceName");
            if (spec.ExePath == null) throw new ArgumentNullException("ServiceSpec.ExePath");
            if (spec.DisplayName == null) spec.DisplayName = spec.ServiceName;
            if (spec.Description == null) spec.Description = spec.DisplayName + " Service";

            IntPtr scman = OpenSCManager(ServiceManagerRights.Connect | ServiceManagerRights.CreateService);
            try
            {
                IntPtr service = OpenService(scman, spec.ServiceName, ServiceRights.QueryStatus | ServiceRights.ChangeConfig | ServiceRights.Start);
                if (service != IntPtr.Zero)
                {
                    // service already exists - update configuration
                    if (!ChangeServiceConfig(service, (uint)spec.ServiceType, (uint)spec.BootType, (uint)spec.ErrorControl, spec.ExePath,
                        spec.LoadOrderGroup, IntPtr.Zero, StringArrayToMultiString(spec.Dependencies),
                        spec.Account, spec.Password, spec.DisplayName))
                    {
                        // changing the configuration failed for some reason.
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                else if (Marshal.GetLastWin32Error() == 1060)
                {
                    // service does not exist (service == IntPtr.Zero), try to create it
                    service = CreateService(scman, spec.ServiceName, spec.DisplayName,
                    spec.Rights, spec.ServiceType,
                    spec.BootType, spec.ErrorControl, spec.ExePath, spec.LoadOrderGroup, IntPtr.Zero,
                    StringArrayToMultiString(spec.Dependencies), spec.Account, spec.Password);
                } else {
                    // open service failed for some other reason.
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                // we have a valid service handle, either because it existed already when we tried to open it,
                // or because it didn't exist and we successfully created it.

                // set the description string
                SetDescription(service, spec.Description);

                // define recovery actions, if they are defined in the service spec
                SetRecoveryOptions(service, spec.recoveryActions, spec.enableActionsForStopsWithErrors, spec.resetFailCountAfterSeconds);

                CloseServiceHandle(service);
            }
            finally
            {
                CloseServiceHandle(scman);
            }
        }

        static string StringArrayToMultiString(ICollection<string> stringArray)
        {
            StringBuilder multiString = new StringBuilder();
            if (stringArray != null)
            {
                foreach (string s in stringArray)
                {
                    multiString.Append(s);
                    multiString.Append('\0');
                }
            }

            return multiString.ToString();
        }

        /// <summary>
        /// Installs an application as a Windows Service.
        /// </summary>
        /// <param name="ServiceName">Name (short name) of the service</param>
        /// <param name="DisplayName">Display name (user-friendly name) of the service</param>
        /// <param name="ExePath">Path of the executable of the service</param>
        /// <exception cref="Win32Exception">if any of the Win32 API calls fail</exception>
        public static void Install(string ServiceName, string DisplayName, string ExePath)
        {
            var spec = new ServiceSpec();
            spec.ServiceName = ServiceName;
            spec.DisplayName = DisplayName;
            spec.ExePath = ExePath;
            Install(spec);
        }

        public static void SetDescription(IntPtr service, string Description)
        {
            Debug.Assert(service != IntPtr.Zero);
            var info = new SERVICE_DESCRIPTION
            {
                lpDescription = Description
            };
            ChangeServiceConfig2(service, SERVICE_INFO_LEVEL.DESCRIPTION, ref info);
        }

        public static void SetRecoveryOptions(IntPtr service, SC_ACTION[] actions, bool applyOnErrorStop = false, uint failureResetSeconds = 3600, string command = null, string rebootMsg = null)
        {
            var recovery = new SERVICE_FAILURE_ACTIONS();
            var defaultAction = new SC_ACTION(SC_ACTION_TYPE.SC_ACTION_NONE, 0);
            recovery.cActions = 3;
            var size = Marshal.SizeOf(typeof(SC_ACTION));
            recovery.lpsaActions = Marshal.AllocHGlobal((System.Int32)(size * recovery.cActions));
            for (int ix = 0; ix < recovery.cActions; ++ix)
            {
                var action = (actions != null && ix < actions.Length) ? actions[ix] : defaultAction;
                Marshal.StructureToPtr(action, IntPtr.Add(recovery.lpsaActions, ix * size), false);
            }
            recovery.dwResetPeriod = failureResetSeconds;   // time to reset failure counter, seconds
            recovery.lpCommand = command;                   // command to execute
            recovery.lpRebootMsg = rebootMsg;               // reboot message (we don't use it)

            try
            {
                if (!ChangeServiceConfig2(service, SERVICE_INFO_LEVEL.FAILURE_ACTIONS, ref recovery))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                // clean up so no memory leak
                // Note, does nothing when given IntPtr.Zero
                Marshal.FreeHGlobal(recovery.lpsaActions);
            }

            // Set whether or not to apply recovery actions if service stops itself with non-0 Win32ExitCode
            var failure_actions_flag = new SERVICE_FAILURE_ACTIONS_FLAG();
            failure_actions_flag.fFailureActionsOnNonCrashFailures = applyOnErrorStop;
            ChangeServiceConfig2(service, SERVICE_INFO_LEVEL.FAILURE_ACTIONS_FLAG, ref failure_actions_flag);
        }

        /// <summary>
        /// Start a named service
        /// </summary>
        /// <param name="Name">The service name</param>
        public static void Start(string Name)
        {
            IntPtr scman = OpenSCManager(ServiceManagerRights.Connect);
            try
            {
                IntPtr hService = OpenService(scman, Name, ServiceRights.QueryStatus | ServiceRights.Start);
                if (hService == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                try
                {
                    StartService(hService);
                }
                finally
                {
                    CloseServiceHandle(hService);
                }
            }
            finally
            {
                CloseServiceHandle(scman);
            }
        }

        /// <summary>
        /// Stop a named service
        /// </summary>
        /// <param name="Name">The service name that will be stopped</param>
        public static void Stop(string Name)
        {
            IntPtr scman = OpenSCManager(ServiceManagerRights.Connect);
            try
            {
                IntPtr hService = OpenService(scman, Name, ServiceRights.QueryStatus | ServiceRights.Stop);
                if (hService == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                try
                {
                    StopService(hService);  // throws if not successful.
                }
                finally
                {
                    CloseServiceHandle(hService);
                }
            }
            finally
            {
                CloseServiceHandle(scman);
            }
        }

        /// <summary>
        /// Returns the <code>ServiceState</code> of the named service
        /// </summary>
        /// <param name="ServiceName">The name of the service</param>
        /// <returns>The ServiceState (NotFound, Stop, Run, ...) of the service</returns>
        public static ServiceState GetState(string ServiceName)
        {
            IntPtr scman = OpenSCManager(ServiceManagerRights.Connect);
            try
            {
                IntPtr hService = OpenService(scman, ServiceName, ServiceRights.QueryStatus);
                if (hService == IntPtr.Zero)
                {
                    if (Marshal.GetLastWin32Error() == 1060)
                    {   // service does not exist
                        return ServiceState.SERVICE_NOT_FOUND;
                    }
                    else
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                try
                {
                    return GetServiceStatus(hService);
                }
                finally
                {
                    CloseServiceHandle(hService);
                }
            }
            finally
            {
                CloseServiceHandle(scman);
            }
        }

        public static SERVICE_STATUS QueryStatus(string serviceName)
        {
            IntPtr scman = OpenSCManager(ServiceManagerRights.Connect);
            try
            {
                SERVICE_STATUS ssStatus = new SERVICE_STATUS();
                ssStatus.dwCurrentState = ServiceState.SERVICE_NOT_FOUND;
                IntPtr hService = OpenService(scman, serviceName, ServiceRights.QueryStatus);
                if (hService == IntPtr.Zero)
                {
                    if (Marshal.GetLastWin32Error() != 1060)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                else if (!QueryServiceStatus(hService, ssStatus))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                return ssStatus;
            }
            finally
            {
                CloseServiceHandle(scman);
            }
        }

        // return the executable (basically argv[0]) that runs the service.
        // It should be a valid filesystem Path, with any quoting or command-line arguments removed.
        public static string ExecutablePath(string serviceName)
        {
            string exe = GetRunCommand(serviceName);
            if (exe != null)
            {
                if (exe.StartsWith("\""))
                {
                    // quoted executable, have to extract & strip quotes
                    int q2 = exe.IndexOf('"', 1);
                    exe = exe.Substring(1, q2 - 1);
                }
                else
                {
                    // TODO: with spaces in instance folders, currently
                    // this does not work!
                    //int end = exe.IndexOf(' ');
                    //if (end > 0)
                    //{
                    //    exe = exe.Substring(0, end);
                    //}
                }
                exe = exe.Trim();
            }
            return exe;
        }

        public static string GetRunCommand(this ServiceController svc)
        {
            return GetRunCommand(svc.ServiceName);
        }

        /// <summary>
        /// Find and return the command that is run to start the specified service
        /// </summary>
        /// <param name="serviceName">name of service</param>
        /// <returns>the service's run-command, or null if no such service</returns>
        /// <exception cref="Win32Exception">for any other error condition</exception>
        public static string GetRunCommand(string serviceName)
        {
            string exePath = null;
            string subkey = @"SYSTEM\CurrentControlSet\Services\" + serviceName;
            const string value = "ImagePath";

            IntPtr key;
            int error = RegOpenKeyEx(HKEY_LOCAL_MACHINE, subkey, 0, KEY_READ | KEY_WOW64_32KEY, out key);
            // 0 means success, 2 means key-not-found
            if (error == 0)
            {
                try
                {
                    exePath = (string)RegQueryValue(key, value);
                }
                finally
                {
                    RegCloseKey(key);
                }
            } else if (error != 2)
            {
                throw new Win32Exception(error);
            }
            return exePath;
        }


        public static ServiceController[] Enumerate()
        {
            return ServiceController.GetServices();
        }

        #endregion

        #region "Private wrappers for Win32 API"

        /// <summary>
        /// Starts the provided windows service
        /// </summary>
        /// <param name="hService">The handle to the windows service</param>
        private static void StartService(IntPtr hService)
        {
            if (!StartService(hService, 0, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            WaitForServiceStatus(hService, ServiceState.SERVICE_START_PENDING, ServiceState.SERVICE_RUNNING);
        }

        /// <summary>
        /// Stops the provided windows service
        /// </summary>
        /// <param name="hService">The handle to the windows service</param>
        private static void StopService(IntPtr hService)
        {
            SERVICE_STATUS status = new SERVICE_STATUS();
            if (!ControlService(hService, ServiceControl.Stop, status))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            WaitForServiceStatus(hService, ServiceState.SERVICE_STOP_PENDING, ServiceState.SERVICE_STOPPED);
        }

        /// <summary>
        /// Returns the service state of the specified windows service
        /// </summary>
        /// <param name="hService">The handle to the service</param>
        /// <returns>The <code>ServiceState</code> of the service</returns>
        private static ServiceState GetServiceStatus(IntPtr hService)
        {
            SERVICE_STATUS ssStatus = new SERVICE_STATUS();
            if (!QueryServiceStatus(hService, ssStatus))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return ssStatus.dwCurrentState;
        }

        /// <summary>
        /// Returns true when the service status changes from wait status to desired status
        /// After 1-10 seconds of waiting (it depends), returns false. 
        /// </summary>
        /// <param name="hService">The handle to the service</param>
        /// <param name="WaitStatus">The current state of the service</param>
        /// <param name="DesiredStatus">The desired state of the service</param>
        /// <returns>bool if the service has successfully changed states within the allowed timeline</returns>
        private static bool WaitForServiceStatus(IntPtr hService, ServiceState WaitStatus, ServiceState DesiredStatus)
        {
            SERVICE_STATUS ssStatus = new SERVICE_STATUS();
            int dwOldCheckPoint;
            int dwStartTickCount;

            if (!QueryServiceStatus(hService, ssStatus))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if (ssStatus.dwCurrentState == DesiredStatus) return true;
            dwStartTickCount = Environment.TickCount;
            dwOldCheckPoint = ssStatus.dwCheckPoint;

            while (ssStatus.dwCurrentState == WaitStatus)
            {
                // Do not wait longer than the wait hint. A good interval is
                // one tenth the wait hint, but no less than 1 second and no
                // more than 10 seconds.

                int dwWaitTime = ssStatus.dwWaitHint / 10;

                if (dwWaitTime < 1000) dwWaitTime = 1000;
                else if (dwWaitTime > 10000) dwWaitTime = 10000;

                System.Threading.Thread.Sleep(dwWaitTime);

                // Check the status again.
                if (!QueryServiceStatus(hService, ssStatus))
                {
                    break;
                }
                if (ssStatus.dwCheckPoint > dwOldCheckPoint)
                {
                    // The service is making progress.
                    dwStartTickCount = Environment.TickCount;
                    dwOldCheckPoint = ssStatus.dwCheckPoint;
                }
                else
                {
                    if (Environment.TickCount - dwStartTickCount > ssStatus.dwWaitHint)
                    {
                        // No progress made within the wait hint
                        break;
                    }
                }
            }
            return (ssStatus.dwCurrentState == DesiredStatus);
        }

        /// <summary>
        /// Opens the service manager
        /// </summary>
        /// <param name="Rights">The service manager rights</param>
        /// <returns>the handle to the service manager</returns>
        private static IntPtr OpenSCManager(ServiceManagerRights Rights)
        {
            IntPtr scman = OpenSCManager(null, null, Rights);
            if (scman == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return scman;
        }

        #endregion

    }
}
