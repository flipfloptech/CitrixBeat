using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Management.Infrastructure;
using Nest;
using Serilog;
using Serilog.Core;

namespace CitrixBeat
{
    class BeatMonitor
    {
        //ManagementScope _system = new ManagementScope("\\\\localhost\\CIMv2");
        private string _machinename = Environment.MachineName;
        private CimSession _cimSession = CimSession.Create("localhost");
        private System.Timers.Timer _timer = new System.Timers.Timer();
        private BeatGeoIPLookup _geoiplookup = new BeatGeoIPLookup();
        private Logger _log = null;

        public BeatMonitor(Logger _Logger)
        {
            if (_Logger != null)
                _log = _Logger;
            else
                throw new NullReferenceException("_Logger cannot be null");
        }
        public void StartPoller(double PollingInterval = 5000)
        {
            _log.Information("[BEATMON] Starting Poller");
            _timer.Interval = PollingInterval;
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
            _log.Information("[BEATMON] Started Poller");
        }

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            DataPoller();
        }

        public void StopPoller()
        {
            _log.Information("[BEATMON] Stopping Poller");
            _timer.Stop();
            _timer.Close();
            _timer.Dispose();
            _log.Information("[BEATMON] Stopped Poller"); 
        }
        private void DataPoller()
        {
            _log.Information("[BEATMON] Polling Data");
            _log.Information("[BEATMON] Citrix_Euem_ClientConnect");
            IEnumerable <CimInstance> _ClientConnect = _cimSession.QueryInstances(@"root\Citrix\euem", "WQL", @"Select SessionID,ClientMachineIP,UserName FROM Citrix_Euem_ClientConnect");
            _log.Information("[BEATMON] Citrix_Euem_RoundTrip");
            IEnumerable <CimInstance> _RoundTrip = _cimSession.QueryInstances(@"root\Citrix\euem", "WQL", @"Select SessionID,InputBandwidthAvailable,InputBandwidthUsed,NetworkLatency,OutputBandwidthAvailable,OutputBandwidthUsed,RoundTripTime FROM Citrix_Euem_RoundTrip");
            _log.Information("[BEATMON] Citrix_Client_Enum");
            IEnumerable <CimInstance> _Clients = _cimSession.QueryInstances(@"root\Citrix\hdx", "WQL", @"Select SessionID,Address,Name,Version FROM Citrix_Client_Enum");
            _log.Information("[BEATMON] Win32_Processor");
            IEnumerable <CimInstance> _Processors = _cimSession.QueryInstances(@"root\CIMv2", "WQL", @"Select LoadPercentage FROM Win32_Processor");
            _log.Information("[BEATMON] Win32_OperatingSystem");
            IEnumerable <CimInstance> _Memory = _cimSession.QueryInstances(@"root\CIMv2", "WQL", @"Select TotalVisibleMemorySize,FreePhysicalMemory,TotalVirtualMemorySize,FreeVirtualMemory FROM Win32_OperatingSystem");
            _log.Information("[BEATMON] Polled Data");
            //Prep Data for All Logs
            _log.Information("[BEATMON] Creating Polling Event");
            DataPollerEventArgs _poller_data = new DataPollerEventArgs();

            _log.Information("[BEATMON] Setting machine name and timestamp");
            _poller_data.ServerMachineName = _machinename;
            _poller_data.TimeStamp = DateTime.UtcNow;
            _log.Information("[BEATMON] Set machine name: {0}, timestamp {1}", _machinename, _poller_data.TimeStamp);

            UInt16 _TotalCpuUtilization = 0;
  
            try
            {
                _log.Information("[BEATMON] Averaging CPU Utilization for {0} processors", _Processors.Count());
                foreach (CimInstance _proc in _Processors)
                {
                    _TotalCpuUtilization += (UInt16)_proc.CimInstanceProperties[@"LoadPercentage"].Value;
                }
                _TotalCpuUtilization = (UInt16)(_TotalCpuUtilization / _Processors.Count());
                _poller_data.TotalCpuUtilization = _TotalCpuUtilization;
                _log.Information("[BEATMON] Average CPU Utilization for {0} processors calculated to {1}", _Processors.Count(), _TotalCpuUtilization);
                _log.Information("[BEATMON] Gathering System Memory Utilization");
                foreach (CimInstance _mem in _Memory)
                {
                    _poller_data.FreeVisibleMemory = (UInt64)_mem.CimInstanceProperties[@"FreePhysicalMemory"].Value;
                    _poller_data.FreeVirtualMemory = (UInt64)_mem.CimInstanceProperties[@"FreeVirtualMemory"].Value;
                    _poller_data.TotalVisibleMemory = (UInt64)_mem.CimInstanceProperties[@"TotalVisibleMemorySize"].Value;
                    _poller_data.TotalVirtualMemory = (UInt64)_mem.CimInstanceProperties[@"TotalVirtualMemorySize"].Value;
                }
                _log.Information("[BEATMON] Gathered System Memory Utilization");
            }
            catch(Exception _e) {
                _log.Error(_e,"[BEATMON] ERROR PROCESSING SYSTEM METRICS");
                return;
            }

            //Iterate sessions, flattening records. since kibana can't support nested objects how we need them to be supported.
            _log.Information("[BEATMON] Flatting {0} session records", _ClientConnect.Count());
            foreach (CimInstance _client in _ClientConnect)
            {
                _poller_data.SessionID = 0;
                _poller_data.ClientMachinePrivateIP = "";
                _poller_data.UserName = "";
                _log.Information("[BEATMON] Retreiving SessionID, ClientMachinePrivateIP, and Username");
                try
                {
                    _poller_data.SessionID = (UInt32)_client.CimInstanceProperties[@"SessionID"].Value;
                    _poller_data.ClientMachinePrivateIP = (string)_client.CimInstanceProperties[@"ClientMachineIP"].Value;
                    _poller_data.UserName = (string)_client.CimInstanceProperties[@"UserName"].Value;
                    _log.Information("[BEATMON] Retreived SessionID, ClientMachinePrivateIP, and Username");
                }
                catch(Exception _e)
                {
                    _log.Error(_e,"[BEATMON] Failed to retreive SessionID, ClientMachinePrivateIP, and Username");
                    continue;
                }
                _poller_data.InputBandwidthAvailable = 0;
                _poller_data.InputBandwidthUsed = 0;
                _poller_data.OutputBandwidthAvailable = 0;
                _poller_data.OutputBandwidthUsed = 0;
                _poller_data.NetworkLatency = 0;
                _poller_data.RoundTripTime = 0;
                try
                {
                    _log.Information("[BEATMON] Retreiving Input/Output Bandwidth Used/Available, Network Latency, RoundTripTime");
                    CimInstance _cur_roundtrip = _RoundTrip.First(_instance => ((UInt32)_instance.CimInstanceProperties[@"SessionID"].Value) == _poller_data.SessionID);
                    _poller_data.InputBandwidthAvailable = (UInt32)_cur_roundtrip.CimInstanceProperties[@"InputBandwidthAvailable"].Value;
                    _poller_data.InputBandwidthUsed = (UInt32)_cur_roundtrip.CimInstanceProperties[@"InputBandwidthUsed"].Value;
                    _poller_data.OutputBandwidthAvailable = (UInt32)_cur_roundtrip.CimInstanceProperties[@"OutputBandwidthAvailable"].Value;
                    _poller_data.OutputBandwidthUsed = (UInt32)_cur_roundtrip.CimInstanceProperties[@"OutputBandwidthUsed"].Value;
                    _poller_data.NetworkLatency = (UInt32)_cur_roundtrip.CimInstanceProperties[@"NetworkLatency"].Value;
                    _poller_data.RoundTripTime = (UInt32)_cur_roundtrip.CimInstanceProperties[@"RoundTripTime"].Value;
                    _log.Information("[BEATMON] Retreived Input/Output Bandwidth Used/Available, Network Latency, RoundTripTime");
                }
                catch
                {
                    _log.Error("[BEATMON] Failed to retreive Input/Output Bandwidth Used/Available, Network Latency, RoundTripTime");
                }

                _poller_data.ClientMachinePublicIP = "";
                _poller_data.ClientMachineName = "";
                _poller_data.IsDisconnected = true;
                try
                {
                    _log.Information("[BEATMON] Retreiving ClientMachinePublicIP, ClientMachineName, IsDisconnected");
                    CimInstance _cur_client = _Clients.First(_instance => ((UInt32)_instance.CimInstanceProperties[@"SessionID"].Value) == _poller_data.SessionID);
                    _poller_data.ClientMachinePublicIP = (string)_cur_client.CimInstanceProperties[@"Address"].Value;
                    if (_IsPrivateIP(_poller_data.ClientMachinePublicIP) || _poller_data.ClientMachinePublicIP == _poller_data.ClientMachinePrivateIP)
                        _poller_data.ClientMachinePublicIP = "";
                    _poller_data.ClientMachineName = (string)_cur_client.CimInstanceProperties[@"Name"].Value;
                    _poller_data.IsDisconnected = false;
                    _log.Information("[BEATMON] Retreived ClientMachinePublicIP, ClientMachineName, IsDisconnected");
                }
                catch (Exception _e)
                {
                    _log.Warning(_e,"[BEATMON] Failed to retreive ClientMachinePublicIP, ClientMachineName, IsDisconnected, generally session disconnected");
                }

                _poller_data.ClientLocation = null;
                try
                {
                    if (!String.IsNullOrWhiteSpace(_poller_data.ClientMachinePublicIP))
                    {
                        _log.Information("[BEATMON] Retreiving GeoIP Information for {0}", _poller_data.ClientMachinePublicIP);
                        BeatGeoIPLookup.BeatGeoIP _geoip = _geoiplookup.QueryGeographicalLocation(_poller_data.ClientMachinePublicIP);
                        if (_geoip != null)
                        {
                            _poller_data.ClientLocation = new GeoLocation(_geoip.Latitude, _geoip.Longitude);
                            _log.Information("[BEATMON] Retreived GeoIP Information for {0}",_poller_data.ClientMachinePublicIP);
                        }
                    }
                }
                catch(Exception _e)
                {
                    _log.Error(_e,"[BEATMON] Failed to retreive GeoIP Information for ClientMachinePublicIP");
                }
                _log.Information("[BEATMON] Calling OnDataPolled EventHandler");
                OnDataPolled(_poller_data);
            }
        }
        private bool _IsPrivateIP(string ipAddress)
        {
            int[] ipParts = ipAddress.Split(new String[] { "." }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => int.Parse(s)).ToArray();
            // in private ip range
            if (ipParts[0] == 10 ||
                (ipParts[0] == 192 && ipParts[1] == 168) ||
                (ipParts[0] == 169 && ipParts[1] == 254) ||
                (ipParts[0] == 172 && (ipParts[1] >= 16 && ipParts[1] <= 31)))
            {
                return true;
            }

            // IP Address is probably public.
            // This doesn't catch some VPN ranges like OpenVPN and Hamachi.
            return false;
        }
        protected virtual void OnDataPolled(DataPollerEventArgs e)
        {
            _log.Information("[BEATMON] OnDataPolled Entered");
            EventHandler<DataPollerEventArgs> event_handler = DataPolled;
            if (event_handler != null)
            {
                event_handler(this, e);
                _log.Information("[BEATMON] OnDataPolled Event Handler Called");
            }
        }
        public event EventHandler<DataPollerEventArgs> DataPolled;

    }

    [ElasticsearchType(Name = "CitrixBeat",IdProperty = "Id")]
    public class DataPollerEventArgs : EventArgs
    {
        public string Id { get; set; }

        [Date(Name ="@timestamp")]
        public DateTime TimeStamp { get; set; }

        [Text(Name = "ServerMachineName")]
        public string ServerMachineName { get; set; }

        [Number(NumberType.Integer,Name= "CpuUtilization")]
        public UInt16 TotalCpuUtilization { get; set; }

        [Number(NumberType.Double,Name="FreeVisibleMemory")]
        public UInt64 FreeVisibleMemory { get; set; }

        [Number(NumberType.Double, Name = "TotalVisibleMemory")]
        public UInt64 TotalVisibleMemory { get; set; }

        [Number(NumberType.Double, Name = "FreeVirtualMemory")]
        public UInt64 FreeVirtualMemory { get; set; }

        [Number(NumberType.Double, Name = "TotalVirtualMemory")]
        public UInt64 TotalVirtualMemory { get; set; }

        [Number(NumberType.Long, Index = true, Name = "SessionID")]
        public UInt32 SessionID { get; set; }

        [Number(NumberType.Long, Name = "InputBandwidthAvailable")]
        public UInt32 InputBandwidthAvailable { get; set; }

        [Number(NumberType.Long, Name = "InputBandwidthUsed")]
        public UInt32 InputBandwidthUsed { get; set; }

        [Number(NumberType.Long, Name = "NetworkLatency")]
        public UInt32 NetworkLatency { get; set; }

        [Number(NumberType.Long, Name = "OutputBandwidthAvailable")]
        public UInt32 OutputBandwidthAvailable { get; set; }

        [Number(NumberType.Long, Name = "OutputBandwidthUsed")]
        public UInt32 OutputBandwidthUsed { get; set; }

        [Number(NumberType.Long, Name = "RoundTripTime")]
        public UInt32 RoundTripTime { get; set; }

        [Text(Name = "ClientMachinePrivateIP")]
        public string ClientMachinePrivateIP { get; set; }

        [Text(Name = "ClientMachinePublicIP")]
        public string ClientMachinePublicIP { get; set; }

        [Text(Name = "ClientMachineName")]
        public string ClientMachineName { get; set; }

        [Text(Name = "Username")]
        public string UserName { get; set; }

        [Boolean(Name = "IsDisconnected")]
        public bool IsDisconnected { get; set; }

        [GeoPoint(Name = "ClientLocation")]
        public Nest.GeoLocation ClientLocation { get; set; }



    }
}
