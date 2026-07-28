// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using Microsoft.Management.Infrastructure;
using StarMon.Library;

namespace StarMon.Hardware {

    // Provides internal display brightness control
    public static class DisplayBrightness {

        private const string Scope = "root\\wmi";

        // A single local session is kept for reuse (establishing one is the
        // slowest part of a query); the lock guards it against concurrent
        // use from background threads
        private static readonly object Lock = new object();
        private static CimSession Session;

        // Returns the shared session, creating it on first use
        private static CimSession GetSession() {
            if(Session == null)
                Session = CimSession.Create(null);
            return Session;
        }

        // Discards the shared session after a failure,
        // so that the next call starts over with a new one
        // (identical consecutive log entries are stacked by the logger)
        private static void DropSession(string reason) {
            Logger.Warning("Brightness", "WMI brightness call failed, session reset", reason);
            try { if(Session != null) Session.Dispose(); } catch { }
            Session = null;
        }

        // Returns the current brightness percentage (0-100), or -1 if unavailable
        public static int Get() {
            lock(Lock) {
                try {
                    foreach(CimInstance instance in GetSession().EnumerateInstances(Scope, "WmiMonitorBrightness"))
                        using(instance) {
                            object value = instance.CimInstanceProperties["CurrentBrightness"]?.Value;
                            if(value != null)
                                return Convert.ToInt32(value);
                        }
                } catch(Exception e) { DropSession(e.Message); }
                return -1;
            }
        }

        // Sets the brightness percentage (0-100); returns false when unavailable
        public static bool Set(int percent) {
            if(percent < 0) percent = 0;
            if(percent > 100) percent = 100;

            lock(Lock) {
                try {
                    foreach(CimInstance instance in GetSession().EnumerateInstances(Scope, "WmiMonitorBrightnessMethods"))
                        using(instance) {
                            using(CimMethodParametersCollection args = new CimMethodParametersCollection {
                                CimMethodParameter.Create("Timeout", (uint)0, CimType.UInt32, CimFlags.In),
                                CimMethodParameter.Create("Brightness", (byte)percent, CimType.UInt8, CimFlags.In)
                            })
                            using(GetSession().InvokeMethod(Scope, instance, "WmiSetBrightness", args)) { }
                            return true;
                        }
                } catch(Exception e) { DropSession(e.Message); }
                return false;
            }
        }

    }

}
