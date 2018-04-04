using Elasticsearch.Net;
using Nest;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace CitrixBeat
{
    public partial class ctxBeatSvc : ServiceBase
    {
        private string _elastichost = null;
        private int _elasticport = 0;
        private string _elasticuser = null;
        private string _elasticpass = null;
        private BeatMonitor _ctxBeatMonitor = null;
        private ElasticClient _elasticClient = null;
        private ConnectionSettings _elasticSettings = null;
        private Logger _log = null;
        private LoggerConfiguration _logconfig = null;
        public ctxBeatSvc()
        {
            _logconfig = new LoggerConfiguration()
            .WriteTo.RollingFile(Properties.Settings.Default.LogDirectory + "\\citrixbeat-{Date}.txt");
            switch (Properties.Settings.Default.LogLevel.ToLower())
            {
                case "information":
                case "info":
                default:
                    _logconfig.MinimumLevel.Information();
                    break;
                case "warning":
                case "warn":
                    _logconfig.MinimumLevel.Warning();
                    break;
                case "error":
                    _logconfig.MinimumLevel.Error();
                    break;
            }
            _log = _logconfig.CreateLogger();
            _log.Information("[CITRIXBEAT] Starting Initialization");
            InitializeComponent();
            _log.Information("[CITRIXBEAT] Reading Settings");
            _elastichost = Properties.Settings.Default.ElasticHost;
            _elasticport = Properties.Settings.Default.ElasticPort;
            _elasticuser = Properties.Settings.Default.ElasticUser;
            _log.Information("[CITRIXBEAT] Decrypting Password");
            _elasticpass = BeatEncryption.DecryptString(Properties.Settings.Default.ElasticPassword, System.Reflection.Assembly.GetExecutingAssembly().GetName().Name);
            _log.Information("[CITRIXBEAT] Creating NEST Connection Settings");
            _elasticSettings = new ConnectionSettings(new Uri("https://" + _elastichost + ":" + Convert.ToString(_elasticport)));
            _elasticSettings.ServerCertificateValidationCallback(CertificateValidations.AllowAll);
            _elasticSettings.RequestTimeout(new TimeSpan(0, 0, 2));
            _elasticSettings.MaxRetryTimeout(new TimeSpan(0, 0, 4));
            _elasticSettings.BasicAuthentication(_elasticuser, _elasticpass);
            _log.Information("[CITRIXBEAT] Creating NEST Client");
            _elasticClient = new ElasticClient(_elasticSettings);
            _log.Information("[CITRIXBEAT] Creating BeatMonitor");
            _ctxBeatMonitor = new BeatMonitor(_log);
            _ctxBeatMonitor.DataPolled += _ctxBeatMonitor_DataPolled;
            _log.Information("[CITRIXBEAT] Initialized");
        }

        private void _ctxBeatMonitor_DataPolled(object sender, DataPollerEventArgs e)
        {
            _log.Information("[CITRIXBEAT] Received Data Poll");
            string _DailyIndex = "citrixbeat-" + DateTime.Now.ToString("yyyy.MM.dd");
            try
            {
                _log.Information("[CITRIXBEAT] Checking for Index: {0}", _DailyIndex);
                if (!_elasticClient.IndexExists(_DailyIndex).Exists)
                {
                    _log.Information("[CITRIXBEAT] Index Does Not Exist: {0}, Creating",_DailyIndex);
                    ICreateIndexResponse _createIndexResponse = _elasticClient.CreateIndex(_DailyIndex, idx => idx.Mappings(ms => ms.Map<DataPollerEventArgs>(m => m.AutoMap())));
                    if (!_createIndexResponse.Acknowledged && !_createIndexResponse.ShardsAcknowledged)
                    {
                        _log.Warning("[CITRIXBEAT] Index Creation Failed: {0}, Reason: {1}", _DailyIndex, _createIndexResponse.DebugInformation);
                       
                        return;
                    }
                }
                IIndexResponse _indexResponse = _elasticClient.Index(e, idx => idx.Index(_DailyIndex));
                if (_indexResponse.Result != Result.Created)
                {
                    _log.Error("[CITRIXBEAT] Indexing Record Failed: {0}", _indexResponse.DebugInformation);
                    return;
                }
            }
            catch (Exception _e)
            {
                _log.Error(_e,"[CITRIXBEAT] Data Poll Event Processing Failure");
            }
        }

        public void StartDebug()
        { _log.Information("[CITRIXBEAT] Start Debug");  OnStart(null); }
        protected override void OnStart(string[] args)
        {
            _log.Information("[CITRIXBEAT] Starting Service");
            if (String.IsNullOrWhiteSpace(_elastichost) || String.IsNullOrWhiteSpace(_elasticuser) || String.IsNullOrWhiteSpace(_elasticpass) || _elasticport <= 0 || _elasticport >= 65535) { OnStop(); return; }
            if (Properties.Settings.Default.PollingInterval >= 5000) // don't allow polling less then every 5 seconds.
                _ctxBeatMonitor.StartPoller(Properties.Settings.Default.PollingInterval);
            else
                _ctxBeatMonitor.StartPoller();
            base.OnStart(args);
            _log.Information("[CITRIXBEAT] Started Service");
        }

        protected override void OnStop()
        {
            _log.Information("[CITRIXBEAT] Stopping Service");
            _ctxBeatMonitor.StopPoller();
            base.OnStop();
            _log.Information("[CITRIXBEAT] Stopped Service");
        }
    }
}
