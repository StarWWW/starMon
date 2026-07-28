// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

namespace StarMon.AppService {

    // What the thermal guard has decided to do on this tick
    public enum ThermalAction {

        // Nothing to do
        None,

        // Crossed the high threshold: ask for maximum fans and say so
        Engage,

        // Still climbing despite maximum fans: drop every manual override so
        // the Embedded Controller's own thermal management takes over
        Panic,

        // Back below the low threshold: release the override and say so
        Release,

        // Protection was switched off while it was engaged: release the
        // override, but without a notification the user did not ask for
        ReleaseQuiet

    }

    // Automatic thermal protection, as a state machine with no hardware in it.
    //
    // Two thresholds with a gap between them, because a single one would make
    // the fans oscillate at the boundary: the guard engages at the high mark
    // and does not release until the temperature has come all the way back
    // down to the low one. A third, higher mark exists implicitly - if the
    // temperature is still climbing once maximum fans have been asked for,
    // asking again will not help, and the right answer is to stop interfering
    // at all and let the firmware handle it.
    //
    // This was inline in GuiTray, tangled with balloon tips and Platform
    // calls, which is why none of it could be tested. Here the caller passes
    // in a reading and is told what to do about it.
    public sealed class ThermalGuard {

        // How far above the high threshold counts as "this is not working".
        // Four degrees: close enough that the machine is genuinely still
        // getting hotter, far enough that sensor noise cannot trip it.
        public const int PanicMarginC = 4;

        // How long to wait before repeating a throttle notification. The
        // status bit toggles rapidly at the thermal limit, so an unlimited
        // notification would arrive several times a minute.
        public const int ThrottleNotifyIntervalMs = 300000;

        // Whether the guard currently holds the fans at maximum
        public bool IsActive { get; private set; }

        // Whether the overrides have already been dropped this episode, so
        // that is done once rather than on every tick above the panic mark
        public bool IsPanicApplied { get; private set; }

        // When the throttle notification was last shown, as an unsigned
        // millisecond tick count; zero means never
        private int LastThrottleNotifyTick;

        // Decides what to do with a temperature reading.
        //
        // A reading of zero means the sensors gave nothing back, and is not
        // treated as cold: no decision is taken at all until a plausible
        // reading arrives.
        public ThermalAction Step(bool enabled, byte temp, int highC, int lowC) {

            if(!enabled) {

                // The releases below only run while the toggle is still on, so
                // switching protection off mid-episode has to release here
                if(this.IsActive) {
                    this.IsActive = false;
                    this.IsPanicApplied = false;
                    return ThermalAction.ReleaseQuiet;
                }

                return ThermalAction.None;

            }

            if(temp == 0)
                return ThermalAction.None;

            if(!this.IsActive && temp >= highC) {
                this.IsActive = true;
                return ThermalAction.Engage;
            }

            if(this.IsActive && !this.IsPanicApplied && temp >= highC + PanicMarginC) {
                this.IsPanicApplied = true;
                return ThermalAction.Panic;
            }

            if(this.IsActive && temp <= lowC) {
                this.IsActive = false;
                this.IsPanicApplied = false;
                return ThermalAction.Release;
            }

            return ThermalAction.None;

        }

        // Whether a thermal-throttle notification is due, given that the
        // hardware is reporting one. Takes the current tick count rather than
        // reading the clock, so the rate limit can be tested.
        public bool ShouldNotifyThrottle(int nowTick) {

            if(this.LastThrottleNotifyTick != 0
                && unchecked(nowTick - this.LastThrottleNotifyTick) < ThrottleNotifyIntervalMs)
                return false;

            this.LastThrottleNotifyTick = nowTick;
            return true;

        }

        // Whether it is safe to keep re-extending a manually set fan state.
        //
        // Letting the Embedded Controller's countdown lapse hands fan control
        // back to its own automatic failsafe, which must always stay available.
        // So this requires a recent, plausible reading below the protection
        // threshold: no reading, failing sensors or running hot all mean no.
        public bool SafeToKeepManualFans(byte lastTemp, int highC) {
            return !this.IsActive && lastTemp > 0 && lastTemp < highC;
        }

    }

}
