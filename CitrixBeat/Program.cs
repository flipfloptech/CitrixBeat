using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace CitrixBeat
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new ctxBeatSvc()
            };
            /*Debug
            ((ctxBeatSvc)ServicesToRun[0]).StartDebug();
            var StartTime = DateTime.Now;
            bool Timelapsed = false;
            while(DateTime.Now < StartTime.AddMinutes(5))
            {
                System.Threading.Thread.Sleep(1);
                Timelapsed = true;
            }
            if (Timelapsed)
                return;*/
            ServiceBase.Run(ServicesToRun);
        }
    }
}
