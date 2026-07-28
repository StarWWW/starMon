// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

namespace StarMon.Ui.ViewModels {

    // One reading, as a stat card shows it: a caption, a large figure with its
    // unit, a quieter line of supporting detail, and how worried to be.
    //
    // The figure is a string rather than a number because the source decides
    // how to format it — a temperature has no decimals, a clock speed has one,
    // and a reading that is simply unavailable shows a dash. Putting that
    // decision behind a binding converter would spread one small piece of
    // knowledge across two places.
    public sealed class ReadingViewModel : Observable {

        private string CaptionValue = "";
        private string FigureValue = "-";
        private string UnitValue = "";
        private string DetailValue = "";
        private string SecondValue = "";
        private Health HealthValue = Health.Neutral;
        private bool IsAvailableValue = true;
        private double PortionValue = -1;

        public ReadingViewModel(string caption) {
            this.CaptionValue = caption;
        }

        public string Caption {
            get { return this.CaptionValue; }
            set { Set(ref this.CaptionValue, value); }
        }

        public string Figure {
            get { return this.FigureValue; }
            set { Set(ref this.FigureValue, value); }
        }

        public string Unit {
            get { return this.UnitValue; }
            set { Set(ref this.UnitValue, value); }
        }

        public string Detail {
            get { return this.DetailValue; }
            set { Set(ref this.DetailValue, value); }
        }

        // A second figure of the same kind, for a card reporting a pair —
        // the two fans, which the firmware drives together and which say very
        // little apart. Empty on every other card.
        public string Second {
            get { return this.SecondValue; }
            set { Set(ref this.SecondValue, value); }
        }

        public Health Health {
            get { return this.HealthValue; }
            set { Set(ref this.HealthValue, value); }
        }

        // Whether this machine reports the reading at all. A card for a sensor
        // the hardware does not have is dimmed rather than removed: the layout
        // staying put between machines is worth more than the space, and an
        // absent card reads as a bug.
        public bool IsAvailable {
            get { return this.IsAvailableValue; }
            set { Set(ref this.IsAvailableValue, value); }
        }

        // The reading as a fraction of its own full scale, for the ring gauge
        // that draws it: a temperature out of 100 °C, a fan or charge out of
        // 100 %. Negative means unknown, and the gauge draws only its track.
        public double Portion {
            get { return this.PortionValue; }
            set { Set(ref this.PortionValue, value); }
        }

        // Sets a temperature reading and its band in one step, since the two
        // must never disagree
        public void SetTemperature(double celsius, string detail) {

            if(celsius <= 0) {
                this.Figure = "-";
                this.Health = Health.Neutral;
                this.Portion = -1;
            } else {
                this.Figure = ((int) (celsius + 0.5)).ToString();
                this.Health = HealthScale.FromTemperature(celsius);
                this.Portion = celsius >= 100 ? 1 : celsius / 100.0;
            }

            this.Detail = detail;

        }

    }

}
