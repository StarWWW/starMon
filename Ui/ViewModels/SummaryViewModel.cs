// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;

namespace StarMon.Ui.ViewModels {

    // The strip under the tabs: what the machine is doing, on every page.
    //
    // The old window put the live figures on the dashboard and nowhere else,
    // so reading the log or changing a setting meant losing sight of the
    // temperature that sent you there. This carries the four readings the
    // window is actually opened for across every section.
    //
    // It also carries the three badges, which is the part that was missing
    // entirely. Reading.IsThermalProtectionActive has been set by the poller
    // all along and read by nothing: when the guard forced the fans to
    // maximum, the fan selector moved on its own with nothing anywhere to say
    // the application had done it rather than the user.
    public sealed class SummaryViewModel : Observable {

        // Three minutes at the three-second cadence the poller runs while the
        // window is visible. Long enough to show the shape of a load arriving,
        // short enough that the strip reacts rather than averages.
        private const int TrendSamples = 60;

        private readonly double[] CpuRing = new double[TrendSamples];
        private readonly double[] GpuRing = new double[TrendSamples];
        private int Filled;

        private double[] CpuTrendValue = new double[0];
        private double[] GpuTrendValue = new double[0];

        private bool IsThermalProtectionValue;
        private bool IsProgramRunningValue;
        private bool IsThrottlingValue;
        private string ProgramNameValue = "";
        private string ThrottleTextValue = "";

        public SummaryViewModel() {

            // The captions are set by the controller from the locale, the same
            // way the dashboard's are, so the strip and the cards never
            // disagree about what a reading is called
            this.Cpu = new ReadingViewModel("CPU");
            this.Gpu = new ReadingViewModel("GPU");
            this.Fan = new ReadingViewModel("FAN");
            this.Battery = new ReadingViewModel("BAT");

            for(int i = 0; i < TrendSamples; i++) {
                this.CpuRing[i] = double.NaN;
                this.GpuRing[i] = double.NaN;
            }

        }

        public ReadingViewModel Cpu { get; private set; }
        public ReadingViewModel Gpu { get; private set; }
        public ReadingViewModel Fan { get; private set; }
        public ReadingViewModel Battery { get; private set; }

        // Oldest first, ready for a Sparkline. Handed out as a fresh array
        // rather than the ring itself: the control reads it during a render
        // that may not have happened yet when the next reading arrives.
        public double[] CpuTrend {
            get { return this.CpuTrendValue; }
            private set { Set(ref this.CpuTrendValue, value); }
        }

        public double[] GpuTrend {
            get { return this.GpuTrendValue; }
            private set { Set(ref this.GpuTrendValue, value); }
        }

        // The application has taken the fans over to protect the machine. The
        // one badge that says "not your doing".
        public bool IsThermalProtection {
            get { return this.IsThermalProtectionValue; }
            set { Set(ref this.IsThermalProtectionValue, value); }
        }

        public bool IsProgramRunning {
            get { return this.IsProgramRunningValue; }
            set { Set(ref this.IsProgramRunningValue, value); }
        }

        public string ProgramName {
            get { return this.ProgramNameValue; }
            set {
                if(Set(ref this.ProgramNameValue, value ?? ""))
                    Raise("ProgramBadge");
            }
        }

        // The badge's text: the program's own name where it has one.
        //
        // A badge reading "PROGRAM" says a program is running, which the user
        // can already tell from the fans. Which program is the part worth the
        // room, and this strip is on every page — so it is the one place the
        // answer is always to hand.
        public string ProgramBadge {
            get {
                return this.ProgramNameValue.Length > 0
                    ? this.ProgramNameValue.ToUpperInvariant()
                    : Library.Config.Locale.Get("GuiWpfChipProgram");
            }
        }

        // The processor is being held back — by heat, or by its own power
        // limit. Worth saying plainly: a machine that is slow because it is
        // hot looks identical to one that is simply slow.
        public bool IsThrottling {
            get { return this.IsThrottlingValue; }
            set { Set(ref this.IsThrottlingValue, value); }
        }

        public string ThrottleText {
            get { return this.ThrottleTextValue; }
            set { Set(ref this.ThrottleTextValue, value ?? ""); }
        }

        // A reading of zero is a gap rather than a value — the same rule the
        // history buffer follows, so a sensor that drops out for a tick leaves
        // a break in the trend instead of a dive to the floor
        public void Push(double cpu, double gpu) {

            Shift(this.CpuRing, cpu > 0 ? cpu : double.NaN);
            Shift(this.GpuRing, gpu > 0 ? gpu : double.NaN);

            if(this.Filled < TrendSamples)
                this.Filled++;

            this.CpuTrend = Snapshot(this.CpuRing);
            this.GpuTrend = Snapshot(this.GpuRing);

        }

        private static void Shift(double[] ring, double value) {
            Array.Copy(ring, 1, ring, 0, ring.Length - 1);
            ring[ring.Length - 1] = value;
        }

        // Only the part that has been written. Before the ring has filled, the
        // untouched tail is NaN, and handing the whole array over would draw
        // the trend crushed into the right-hand third of the strip.
        private double[] Snapshot(double[] ring) {

            double[] copy = new double[this.Filled];
            Array.Copy(ring, ring.Length - this.Filled, copy, 0, this.Filled);
            return copy;

        }

    }

}
