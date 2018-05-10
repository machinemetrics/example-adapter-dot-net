using MTConnect;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using Debug = System.Diagnostics.Debug;

namespace ExampleAdapter
{
    public partial class AdapterService : ServiceBase
    {
        /// <summary>
        /// true if running as a (Windows) service, false otherwise e.g. when running in console
        /// </summary>
        public readonly bool winService;

        // The TCP/IP port that delivers SHDR output - 7878 is default per MTConnect
        public int Port = 7878;

        /// <summary>
        /// basic 'cycle time' of the main scan loop
        /// </summary>
        public int ScanInterval = 1000;

        /// <summary>
        /// flag set true to request orderly stop/shutdown
        /// </summary>
        private bool stopped = false;

        /// <summary>
        /// Main thread: scan, send, sleep, repeat
        /// </summary>
        private Thread mThread;

        /// <summary>
        /// MTConnect SHDR manager
        /// </summary>
        private MTConnect.Adapter adapter;

        /// <summary>
        /// Construct a Service object
        /// </summary>
        /// <param name="name">name of service</param>
        /// <param name="winService">true if running as a service, false in console mode</param>
        public AdapterService(string name, bool winService)
        {
            this.ServiceName = name;
            this.winService = winService;

            // automatically log start, stop, pause & continue to EventLog
            base.AutoLog = true;
        }

        /// <summary>
        /// Initialize the service - do things that could fail.
        /// </summary>
        public void Initialize()
        {
            if (winService)
            {
                Console.WriteLine($"  ServiceName:   {ServiceName}");
            }
            Console.WriteLine($"  SHDR Port:     {Port}");
            Console.WriteLine();
            // make our MTConnect adapter to report our state on SHDR
            adapter = new MTConnect.Adapter(Port);
            Console.Out.Flush();
        }

        private delegate bool ConsoleCtrlHandlerDelegate(System.UInt32 sig);

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerDelegate handler, bool add);

        static ConsoleCtrlHandlerDelegate _consoleCtrlHandler;

        /// <summary>
        /// Called to 'do our thing' when not running as a service.
        /// </summary>
        /// <returns>exit code</returns>
        public void RunInConsole()
        {
            Debug.Assert(!winService);
            // start 'er up
            OnStart(null);
            // set up Ctrl+C or SIGINT handling
            _consoleCtrlHandler += sig =>
            {
                stopped = true;
                Console.Error.WriteLine("* CtrlHandler({0})", sig);
                Console.WriteLine("* CtrlHandler({0})", sig);
                Console.Out.Flush();
                Thread.Sleep(10000);
                // means please let me continue to execute:
                return true;
            };
            SetConsoleCtrlHandler(_consoleCtrlHandler, true);
            // wait for shutdown (broken)
            while (!stopped && mThread.IsAlive)
            {
                Thread.Sleep(100);
            }
            OnStop();
            mThread.Join(3000);
        }

        /// <summary>
        /// Start up this Service. Called by Service Manager OR when running in console.
        /// </summary>
        /// <remarks>Expected to return quickly, after starting up any
        /// long-running activities as background threads.</remarks>
        /// <param name="args">command-line arguments, if any</param>
        protected override void OnStart(string[] args)
        {
            Console.WriteLine("Service.OnStart...");
            stopped = false;
            mThread = new Thread(ScanAndSendSHDR);
            // shouldn't happen, but in case - allow app to exit with this thread alive:
            mThread.IsBackground = true;
            mThread.Start();
            Console.WriteLine("Service.OnStart completed."); Console.Out.Flush();
        }

        /// <summary>
        /// Service call-back, 'please stop this service'
        /// </summary>
        protected override void OnStop()
        {
            Console.WriteLine("Service.OnStop called...");
            stopped = true;
            // send out final state to SHDR
            // (wait for it to actually go out)
            //adapter.SendSync();
            //adapter.RequestStop();
            adapter.Stop();

            Console.WriteLine("Service.OnStop completed."); Console.Out.Flush();
        }

        private Condition mSystem = new Condition("system");
        private Event mCompanyName = new Event("company_name");
        private Event mExecution = new Event("execution");
        private Event mControllerMode = new Event("mode");
        private Event mServoBlock = new Event("block");
        private Event mProgram = new Event("program");

        /// <summary>
        /// thread to scan machine state and send SHDR updates
        /// </summary>
        private void ScanAndSendSHDR()
        {
            // name this thread, just for debugging purposes
            Thread.CurrentThread.Name = "ScanAndSendSHDR";
            Console.WriteLine($"{Thread.CurrentThread.Name} thread started.");
            // anything that throws past the forever loop means
            // shutdown the app, which should be only: ThreadAbortException.
            try
            {
                // set up SHDR output items
                // Path1 (Path) is the main 'servo' process, that modifies the gateway's configuration
                // Path2 is the targetMonitor, that tracks the goal configuration from the cloud.
                var mAvail = new Event("avail");
                var mAdapterInfo = new Event("adapter_info");
                var mMachineName = new Event("machine_name");

                // attach all our SHDR datums to the SHDR server
                adapter.AddDataItem(mAdapterInfo);
                adapter.AddDataItem(mAvail);
                adapter.AddDataItem(mControllerMode);
                adapter.AddDataItem(mExecution);
                adapter.AddDataItem(mServoBlock);
                adapter.AddDataItem(mProgram);
                adapter.AddDataItem(mSystem);

                mAdapterInfo.Value = ServiceName;

                mAvail.Value = "AVAILABLE";
                mSystem.Normal();

                // start the SHDR server running on its port:
                adapter.Start();
                Thread.Sleep(100);

                DateTime lastScan = DateTime.Now;
                while (!stopped)
                {
                    // by default, sleep this long each time around the loop
                    lastScan = DateTime.Now;
                    adapter.Begin();
                    ScanMachineState();
                    adapter.SendChanged();
                    // sleep for a while, how long depends on what's going on
                    int timeLeft = ScanInterval;
                    while (timeLeft > 0 && !stopped)
                    {
                        Thread.Sleep(100); timeLeft -= 100;
                    }
                }
            }
            catch (ThreadAbortException)
            {
                Thread.ResetAbort();
                Console.WriteLine($"{Thread.CurrentThread.Name} thread abort.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("{Thread.CurrentThread.Name} thread: {ex}");
                Console.Out.Flush();
                ExitCode = (ex is Win32Exception) ? (ex as Win32Exception).NativeErrorCode : 1;
            }
            // stop the SHDR server and disconnect from all TCP/IP clients
            adapter.Stop();
            Console.WriteLine($"{Thread.CurrentThread.Name} thread exit."); Console.Out.Flush();
        }

        private void ScanMachineState()
        {
            mExecution.Value = "ACTIVE";
            mServoBlock.Value = DateTime.Now.Second.ToString();
        }
    }
}
