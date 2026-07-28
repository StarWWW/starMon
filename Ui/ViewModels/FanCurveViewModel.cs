// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;

namespace StarMon.Ui.ViewModels {

    // The temperature-to-fan-speed curve the user draws.
    //
    // The columns are fixed and the heights are what move. A curve with
    // draggable points in both axes sounds more capable and is worse to use:
    // points slide past one another, the ordering has to be defended, and the
    // thing being drawn — how hard the fans work as the machine warms up —
    // is not clearer for it.
    public sealed class FanCurveViewModel : Observable {

        // The columns, in degrees. Chosen to cover the span where a fan curve
        // actually does anything: below forty the fans are off whatever the
        // curve says, and above ninety the firmware's own protection has taken
        // over regardless.
        public static readonly int[] Columns = { 40, 50, 60, 70, 80, 90 };

        // The default ramp, as a percentage of the hardware's own ceiling.
        // Expressed as a proportion rather than as levels because the ceiling
        // is a property of the machine: a curve built against a made-up
        // maximum either never reaches full speed or asks for a level the
        // firmware rejects.
        public static readonly int[] DefaultPercent = { 36, 46, 57, 70, 86, 100 };

        private readonly int[] PercentValues = (int[]) DefaultPercent.Clone();

        private double CurrentTemperatureValue;
        private string StatusValue = "";
        private bool IsRunningValue;

        public FanCurveViewModel() {
            this.ApplyCommand = new RelayCommand(() => OnApply());
            this.StopCommand = new RelayCommand(() => OnStop());
            this.ResetCommand = new RelayCommand(Reset);
        }

        // Raised when the user asks for the curve to be applied or stopped.
        // The view model does not touch hardware: whatever owns it does.
        public event Action Applied;
        public event Action Stopped;

        public RelayCommand ApplyCommand { get; private set; }
        public RelayCommand StopCommand { get; private set; }
        public RelayCommand ResetCommand { get; private set; }

        // The fan speed at each column, as a percentage of the ceiling
        public int[] Percent {
            get { return (int[]) this.PercentValues.Clone(); }
            set {
                if(value == null || value.Length != this.PercentValues.Length)
                    return;
                for(int i = 0; i < value.Length; i++)
                    this.PercentValues[i] = Clamp(value[i]);
                Raise("Percent");
            }
        }

        // Reads and writes one column, which is what dragging a point does
        public int this[int column] {
            get { return this.PercentValues[column]; }
            set {
                int clamped = Clamp(value);
                if(this.PercentValues[column] == clamped)
                    return;
                this.PercentValues[column] = clamped;
                Raise("Percent");
            }
        }

        // Where the machine currently sits, so the curve can show it rather
        // than leaving the user to work out which part of it is in force. This
        // is the one thing a drawn curve cannot say on its own, and it is the
        // question anyone looking at one is actually asking.
        public double CurrentTemperature {
            get { return this.CurrentTemperatureValue; }
            set { Set(ref this.CurrentTemperatureValue, value); }
        }

        public bool IsRunning {
            get { return this.IsRunningValue; }
            set { Set(ref this.IsRunningValue, value); }
        }

        public string Status {
            get { return this.StatusValue; }
            set { Set(ref this.StatusValue, value); }
        }

        public void Reset() {
            this.Percent = DefaultPercent;
        }

        // The fan level a percentage maps to, given the hardware's ceiling.
        //
        // The arithmetic lives in the service layer beside the curve-to-
        // program conversion that uses it, so there is one implementation
        // rather than two that can drift apart — a rounding difference between
        // them would show up as a saved curve reading back a point off from
        // where it was drawn.
        public static byte ToLevel(int percent, int ceiling) {
            return AppService.FanCurve.ToLevel(percent, ceiling);
        }

        public static int ToPercent(byte level, int ceiling) {
            return AppService.FanCurve.ToPercent(level, ceiling);
        }

        private static int Clamp(int percent) {
            return percent < 0 ? 0 : percent > 100 ? 100 : percent;
        }

        private void OnApply() {
            Action handler = this.Applied;
            if(handler != null) handler();
        }

        private void OnStop() {
            Action handler = this.Stopped;
            if(handler != null) handler();
        }

    }

}
