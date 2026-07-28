// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

namespace StarMon.AppService {

    // The colour arithmetic behind the keyboard backlight modes. All of it is
    // pure: a colour in, a colour out, no hardware and no configuration.
    public static class BacklightColor {

        // The span the temperature-reactive mode maps across
        public const int CoolC = 40, HotC = 85;

        // Maps a temperature to a backlight colour: pure green at or below the
        // cool mark, sweeping through yellow to pure red at or above the hot
        // one. Red rises over the first half of the span and green falls over
        // the second, so the midpoint is full yellow rather than a dim olive.
        public static int FromTemperature(byte temp) {

            float t = temp <= CoolC ? 0f : temp >= HotC ? 1f
                : (temp - CoolC) / (float) (HotC - CoolC);

            int r = t < 0.5f ? (int) (510f * t + 0.5f) : 255;
            int g = t < 0.5f ? 255 : (int) (510f * (1f - t) + 0.5f);

            return r << 16 | g << 8;

        }

        // Converts a hue angle (0-360°, full saturation and brightness) into a
        // packed red-green-blue value
        public static int FromHue(float hue) {

            float h = hue / 60f;
            int i = (int) h % 6;
            float f = h - (int) h;
            int q = (int) (255f * (1f - f) + 0.5f);
            int t = (int) (255f * f + 0.5f);

            switch(i) {
                case 0:  return 0xFF0000 | t << 8;   // Red to yellow
                case 1:  return q << 16 | 0x00FF00;  // Yellow to green
                case 2:  return 0x00FF00 | t;        // Green to cyan
                case 3:  return q << 8 | 0x0000FF;   // Cyan to blue
                case 4:  return t << 16 | 0x0000FF;  // Blue to magenta
                default: return 0xFF0000 | q;        // Magenta to red
            }

        }

        // Scales each channel of a colour by the given brightness level
        public static int Scale(int color, float level) {

            int r = (int) (((color >> 16) & 0xFF) * level + 0.5f);
            int g = (int) (((color >> 8) & 0xFF) * level + 0.5f);
            int b = (int) ((color & 0xFF) * level + 0.5f);

            return r << 16 | g << 8 | b;

        }

    }

    // What the idle watch has decided to do on this tick
    public enum IdleAction {

        // Nothing to do
        None,

        // The user is back: put the backlight on again
        TurnOn,

        // Idle long enough: ask the hardware whether the backlight is on, and
        // report the answer back through Resolve(). The query is left to the
        // caller because it is the one thing here that touches hardware, and
        // it is deliberately not asked on every tick — a backlight the user
        // switched off by hand must be left alone without being re-queried
        // until they come back.
        Query

    }

    // Switches the keyboard backlight off after a spell without keyboard or
    // mouse input, and back on the moment the user returns.
    //
    // The subtlety this class exists to hold is the third state. There is not
    // just "we turned it off" and "we did not": there is also "we wanted to
    // turn it off but it was already off", which must be remembered, or every
    // following tick re-queries hardware to be told the same thing again.
    public sealed class IdleWatch {

        // How recent input has to be to count as the user being back. Two
        // seconds rather than zero because GetLastInputInfo has a coarse
        // resolution and the tick this runs on is a second wide.
        public const uint ActivityMs = 2000;

        // Whether this class is the reason the backlight is off
        public bool IsEngaged { get; private set; }

        // Whether the idle threshold was reached but the backlight turned out
        // to be off already, so it is not queried again until the user is back
        public bool IsSkipping { get; private set; }

        public IdleAction Step(uint idleMs, int offMinutes) {

            // Having switched it off, the only thing left to watch for is the
            // user coming back — or the feature being switched off while the
            // backlight is still dark, which must not strand it that way
            if(this.IsEngaged) {

                if(offMinutes <= 0 || idleMs < ActivityMs) {
                    this.IsEngaged = false;
                    return IdleAction.TurnOn;
                }

                return IdleAction.None;

            }

            if(offMinutes <= 0)
                return IdleAction.None;

            // Activity clears the marker left behind by a lapsed idle check
            if(idleMs < ActivityMs)
                this.IsSkipping = false;
            else if(this.IsSkipping)
                return IdleAction.None;

            return idleMs >= (uint) offMinutes * 60000u
                ? IdleAction.Query : IdleAction.None;

        }

        // Reports what the hardware said in answer to IdleAction.Query.
        // Returns true if the backlight should now be switched off.
        public bool Resolve(bool isBacklightOn) {

            if(isBacklightOn) {
                this.IsEngaged = true;
                return true;
            }

            // Already off, by the user's own hand: leave it, and stop asking
            this.IsSkipping = true;
            return false;

        }

    }

    // The animated backlight effects: a slow sweep around the hue circle, or a
    // breathing swell of whatever colour was in use when the effect started.
    //
    // Advanced once a second by the caller. The phase is kept here rather than
    // derived from the clock so the animation carries on from where it was
    // rather than jumping, and so it can be stepped in a test.
    public sealed class BacklightEffect {

        public const int None = 0, Cycle = 1, Breathe = 2;

        // Degrees of hue a second: one full lap in two minutes
        public const float CycleDegreesPerTick = 3f;

        // Seconds for one full breath, in and out
        public const float BreathePeriodTicks = 8f;

        // How fast the animation runs, as a multiple of the fixed base rate.
        // 1 is the rate the effects always ran at; the caller sets it from the
        // speed configuration each tick, so a change takes effect at once.
        public float Speed = 1f;

        // Where the animation has got to
        public float Phase { get; private set; }

        // The colour in use when the effect started. It doubles as the base
        // the breathing swells from, and is put back once the effects are
        // switched off. Negative means not captured yet.
        public int BaseColor = -1;

        // The last colour written, so an unchanged one is not written again
        public int LastColor = -1;

        // Winds the animation back to the start
        public void Reset() {
            this.Phase = 0f;
            this.LastColor = -1;
        }

        // Advances by one tick and returns the colour to show
        public int Step(int effect) {

            // Guard the multiplier so a bad configuration cannot stall the
            // animation or run it backwards
            float speed = this.Speed < 0.2f ? 0.2f : this.Speed > 3f ? 3f : this.Speed;

            if(effect == Cycle) {
                this.Phase = (this.Phase + CycleDegreesPerTick * speed) % 360f;
                return BacklightColor.FromHue(this.Phase);
            }

            // Breathing: a triangle wave between one quarter and full
            // brightness of the base colour. A triangle rather than a sine
            // because the backlight has few enough brightness steps that the
            // difference is not visible, and this one can be read. A faster
            // speed shortens the breath by advancing further each tick.
            float period = BreathePeriodTicks;
            this.Phase = (this.Phase + speed) % period;

            float half = period / 2f;
            float level = this.Phase < half
                ? this.Phase / half : (period - this.Phase) / half;

            return BacklightColor.Scale(this.BaseColor, 0.25f + 0.75f * level);

        }

    }

}
