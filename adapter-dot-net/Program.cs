using System;
using System.ComponentModel;
using System.Reflection;
using System.ServiceProcess;
using SystemControl;
using static SystemControl.WindowsServiceAPI;

namespace ExampleAdapter
{
    static class Program
    {

        const string SERVICE_NAME = "MTConnect Adapter C# example";

        const string SERVICE_DESCRIPTION = "MTConnect adapter example in C#";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static int Main(string[] args)
        {
            int exitCode = 255;
            string exeFileName = Assembly.GetExecutingAssembly().Location;
            bool runAsWinService = (args.Length == 0);
            string runMode = runAsWinService ? "service" : "console";
            DefaultCulture.Set(new System.Globalization.CultureInfo("en-US"));

            try
            {
                Console.WriteLine("{0} {1}",
                    SERVICE_NAME,
                    System.Diagnostics.FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion
                    );
                Console.WriteLine($"  platform:      {Environment.OSVersion.Platform}");
                Console.WriteLine($"  executable:    {exeFileName}");
                Console.WriteLine($"  runMode:       {runMode}");
                Console.Out.Flush();
                var adapter = new AdapterService(SERVICE_NAME, runAsWinService);
                adapter.Initialize();

                if (runAsWinService)
                {
                    ServiceBase.Run(adapter);
                    // This apparenly returns when the service has been stopped.
                    // "your executable is expected to exit promptly once it returns"
                    Console.WriteLine("ServiceBase.Run returned");
                    Console.Out.Flush();
                    exitCode = adapter.ExitCode;
                }
                else
                {
                    // running 'interactively' - presumably in a console
                    exitCode = 0;
                    switch (args[0])
                    {
                        case "console":
                            adapter.RunInConsole();
                            exitCode = adapter.ExitCode;
                            if (!runAsWinService)
                            {
                                Console.WriteLine("exitCode({0}). Press [enter].", exitCode);
                                Console.Read();
                            }
                            break;
                        case "status":
                            var status = WindowsServiceAPI.QueryStatus(adapter.ServiceName);
                            Console.WriteLine("CurrentState:   " + status.dwCurrentState.ToString());
                            Console.WriteLine("ServiceType:    " + status.dwServiceType);
                            Console.WriteLine("Win32ExitCode:  " + status.dwWin32ExitCode);
                            Console.WriteLine("CheckPoint:     " + status.dwCheckPoint);
                            Console.WriteLine("WaitHint:       " + status.dwWaitHint);
                            break;
                        case "install":
                            {
                                var svc = new WindowsServiceAPI.ServiceSpec();
                                svc.ServiceName = adapter.ServiceName;
                                svc.ExePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                                svc.Description = SERVICE_DESCRIPTION;
                                // list any services this service depends on:
                                svc.Dependencies = new string[] { };
                                // Set the recovery actions on 1st, 2nd and 3rd+ failure
                                svc.recoveryActions = new SC_ACTION[]
                                {
                                    // restart the service after 1 minute
                                    new SC_ACTION(SC_ACTION_TYPE.SC_ACTION_RESTART, 60 * 1000),
                                    new SC_ACTION(SC_ACTION_TYPE.SC_ACTION_RESTART, 60 * 1000),     // not sure this delay is respected
                                    // after two failures, don't restart.
                                };
                                // reset the failure count after 1 hour:
                                svc.resetFailCountAfterSeconds = 60 * 60;
                                // treat a stop with exit code != 0 as a failure:
                                svc.enableActionsForStopsWithErrors = true;

                                WindowsServiceAPI.Install(svc);
                                Console.WriteLine("Service Installed: " + adapter.ServiceName);
                            }
                            break;
                        case "remove":
                            WindowsServiceAPI.Delete(adapter.ServiceName);
                            Console.WriteLine("Service Removed: " + adapter.ServiceName);
                            break;
                        case "start":
                            WindowsServiceAPI.Start(adapter.ServiceName);
                            Console.WriteLine("Service Started: " + adapter.ServiceName);
                            break;
                        case "stop":
                            WindowsServiceAPI.Stop(adapter.ServiceName);
                            Console.WriteLine("Service Stopped: " + adapter.ServiceName);
                            break;
                        default:
                            Console.Error.WriteLine("invalid subcommand '{0}'", args[0]);
                            exitCode = 1639;    // ERROR_INVALID_COMMAND_LINE
                            break;
                    }
                }
            }
            catch (System.Exception e)
            {
                Console.Out.Flush();
                Console.Error.WriteLine("Exception: {0}", e.Message); Console.Error.Flush();
                if (e is Win32Exception)
                {
                    exitCode = (e as Win32Exception).NativeErrorCode;
                }
                else
                {
                    exitCode = 1;
                }
            }
            if (exitCode != 0)
            {
                // abnormal error exit - write exit code to error output
                // incl the full path & filename of this application
                Console.Error.WriteLine("exit({0}) from {1}", exitCode, exeFileName);
            }
            Console.Error.Flush();
            Console.Out.Flush();
            return exitCode;
        }

    }
}
