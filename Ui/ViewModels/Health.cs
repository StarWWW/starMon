// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

namespace StarMon.Ui.ViewModels {

    // How worried to be about a reading.
    //
    // The interface has exactly four of these and they are reserved: they are
    // never used as a series colour on a chart, so a red mark always means
    // something is wrong rather than "this happens to be the sixth line".
    public enum Health {

        // No judgement: a reading that is neither good nor bad, like a clock
        // speed or a fan level
        Neutral,

        Good,
        Warning,
        Serious,
        Critical

    }

    public static class HealthScale {

        // The temperature bands, carried over from the WinForms build. They
        // are wide on purpose: a scale that changes colour every few degrees
        // makes a machine sitting still look like it is in trouble.
        public const int WarningC = 60, SeriousC = 75, CriticalC = 88;

        public static Health FromTemperature(double celsius) {
            if(celsius <= 0) return Health.Neutral;
            if(celsius < WarningC) return Health.Good;
            if(celsius < SeriousC) return Health.Warning;
            if(celsius < CriticalC) return Health.Serious;
            return Health.Critical;
        }

        // Battery charge, where the scale runs the other way
        public static Health FromCharge(double percent) {
            if(percent > 40) return Health.Good;
            if(percent > 20) return Health.Warning;
            if(percent > 10) return Health.Serious;
            return Health.Critical;
        }

    }

}
