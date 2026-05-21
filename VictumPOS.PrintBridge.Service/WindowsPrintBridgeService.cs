using System.ServiceProcess;
using VictumPOS.PrintBridge.Service.Services;

namespace VictumPOS.PrintBridge.Service
{
    internal sealed class WindowsPrintBridgeService : ServiceBase
    {
        private readonly BridgeOptions _options;
        private PrintBridgeServer _server;

        public WindowsPrintBridgeService(BridgeOptions options)
        {
            _options = options;
            ServiceName = "VictumPOSPrintBridge";
            CanStop = true;
            CanShutdown = true;
        }

        protected override void OnStart(string[] args)
        {
            _server = new PrintBridgeServer(_options);
            _server.Start();
        }

        protected override void OnStop()
        {
            if (_server != null)
            {
                _server.Dispose();
                _server = null;
            }
        }

        protected override void OnShutdown()
        {
            OnStop();
        }
    }
}
