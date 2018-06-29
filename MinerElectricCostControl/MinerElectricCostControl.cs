using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Configuration;

namespace MinerElectricCostControl
{
    public enum ServiceState
    {
        SERVICE_STOPPED = 0x00000001,
        SERVICE_START_PENDING = 0x00000002,
        SERVICE_STOP_PENDING = 0x00000003,
        SERVICE_RUNNING = 0x00000004,
        SERVICE_CONTINUE_PENDING = 0x00000005,
        SERVICE_PAUSE_PENDING = 0x00000006,
        SERVICE_PAUSED = 0x00000007,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceStatus
    {
        public int dwServiceType;
        public ServiceState dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public int dwCheckPoint;
        public int dwWaitHint;
    };

    public partial class MinerElectricCostControl : ServiceBase
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus serviceStatus);

        public class PriceData
        {
            public string millisUTC { get; set; }
            public decimal price { get; set; }
        }

        private bool minerStatus;

        private decimal priceLimit;

        private string[] minerURL, minerPort, minerPWD;
        private string priceRequest, comedURL;
        private Int32 timeInterval;

        public MinerElectricCostControl(string[] args)
        {
            InitializeComponent();

            // set initial miner status to running
            minerStatus = true;

            // load values from config file
            minerURL = ConfigurationManager.AppSettings["minerURL"].Split(',');
            minerPort = ConfigurationManager.AppSettings["minerPort"].Split(',');
            minerPWD = ConfigurationManager.AppSettings["minerPWD"].Split(',');
            priceLimit = decimal.Parse(ConfigurationManager.AppSettings["priceLimit"]);
            priceRequest = ConfigurationManager.AppSettings["priceRequest"];
            comedURL = ConfigurationManager.AppSettings["comedURL"];
            timeInterval = Int32.Parse(ConfigurationManager.AppSettings["timeInterval"]);
        }

        protected override void OnStart(string[] args)
        {
            // Update the service state to Start Pending.  
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_START_PENDING;
            serviceStatus.dwWaitHint = 100000;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);

            // Set up a timer to trigger based on settings.  
            System.Timers.Timer timer = new System.Timers.Timer();
            timer.Interval = timeInterval; // set timer interval to value loaded from config file 
            timer.Elapsed += new System.Timers.ElapsedEventHandler(this.OnTimer);
            timer.Start();

            // Update the service state to Running.  
            serviceStatus.dwCurrentState = ServiceState.SERVICE_RUNNING;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }

        public void OnTimer(object sender, System.Timers.ElapsedEventArgs args)
        {
            // check the price after the timer runs
            minerStatus = checkPrice(minerURL, minerPort, minerPWD, priceRequest, priceLimit, minerStatus);
        }

        // function to comapte price data
        public bool checkPrice(string[] url, string[] port, string[] pwd, string request, decimal priceLimit, bool status)
        {
            // Get price data for the current hour
            var pricedata = getPrice(request);

            // if price is too high and miners are on, turn off miners
            if (pricedata[0].price > priceLimit && status == true)
            {
                // Turn off miners
                for (int i = 0; i < url.Length; i++)
                {
                    controlMiner(url[i], port[i], pwd[i], 0);
                }
                return false;
            }
            else if (pricedata[0].price <= priceLimit && status == false)
            {
                // Turn on miners
                for (int i = 0; i < url.Length; i++)
                {
                    controlMiner(url[i], port[i], pwd[i], 1);
                }
                return true;
            }
            else
            {
                // preform no action if price is below limit and miners are running
                return status;
            }
        }

        // function to get current comed price data
        public List<PriceData> getPrice(string request)
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                // HTTP POST
                client.BaseAddress = new Uri(comedURL);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                var response = client.GetAsync(request).Result;

                using (HttpContent content = response.Content)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        // Read the string and convert to list of PriceData
                        Task<string> result = content.ReadAsStringAsync();
                        return JsonConvert.DeserializeObject<List<PriceData>>(result.Result);
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }

        // function to turn gpus on or off
        protected void controlMiner(string minerURL, string minerPort, string minerPSW, int minerState)
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.Connect(minerURL, int.Parse(minerPort));
                socket.Send(Encoding.ASCII.GetBytes("{\"id\":0,\"psw\":\"" + minerPSW + "\",\"jsonrpc\":\"2.0\",\"method\":\"control_gpu\",\"params\":[\"-1\", \"" + minerState + "\"]}"));
            }

        }

        protected override void OnStop()
        {
            // Update the service state to Running.  
            ServiceStatus serviceStatus = new ServiceStatus();
            serviceStatus.dwCurrentState = ServiceState.SERVICE_STOPPED;
            SetServiceStatus(this.ServiceHandle, ref serviceStatus);
        }
    }
}
