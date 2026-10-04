using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace OpsViz
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
            DispatcherUnhandledException += (s, e) =>
            {
                try { Infra.Logger.Log("FATAL UI: " + e.Exception.GetBaseException().Message); }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    Infra.Logger.Log("FATAL: " + (ex != null ? ex.GetBaseException().Message : "?"));
                }
                catch { }
            };
        }
    }
}
