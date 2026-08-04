// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Ec;
using StarMon.Library;

namespace StarMon.Hardware.Platform {

#region Interface
    // Defines an interface for interacting with a fan
    public interface IFan {

        // Retrieves the fan type
        public BiosData.FanType GetFanType();

        public int GetLevel();  // Retrieves the fan level [krpm]
        public int GetRate();   // Retrieves the fan rate [%]
        public int GetSpeed();  // Retrieves the fan speed [rpm]

        public void SetLevel(int level);  // Sets the fan level [krpm]
        public void SetRate(int rate);    // Sets the fan rate [%]

        // Re-derives the believable-reading bounds from the current fan
        // ceiling, which is only known once the firmware has been asked
        public void RefreshConstraints();

    }
#endregion

    // Implements a mechanism for interacting with a fan
    public class Fan : IFan {

#region Implementation
        // Stores the fan type
        protected BiosData.FanType FanType;

        // Stores the level data component
        protected IPlatformReadWriteComponent Level;

        // Stores the rate data components (separate read and write)
        protected IPlatformReadComponent RateRead;
        protected IPlatformWriteComponent RateWrite;

        // Stores the speed data component
        protected IPlatformReadComponent Speed;

        // Where this fan sits in the firmware's own arrays.
        //
        // It used to be derived from the type, as type - 1, which works only
        // because the first two types happen to be numbered 1 and 2. A third
        // fan typed as Exhaust would have indexed 2 by luck and a fourth typed
        // as Intake would have indexed 4, past the end of a four-fan array. The
        // index is a fact about the board's ordering, not about what the fan
        // cools, so it is now given rather than inferred.
        protected int Index;

        // Constructs a fan instance.
        //
        // Any of the components may be null: a board with more fans than this
        // build has registers for still has those fans in the firmware's own
        // arrays, and the level and speed calls reach them by index. A fan with
        // no registers is driven entirely through the firmware, which is a
        // reduced fan rather than an absent one.
        public Fan(
            BiosData.FanType type,
            IPlatformReadWriteComponent level,
            IPlatformReadComponent rateRead,
            IPlatformWriteComponent rateWrite,
            IPlatformReadComponent speed,
            int index = -1) {

            this.FanType = type;
            this.Level = level;
            this.RateRead = rateRead;
            this.RateWrite = rateWrite;
            this.Speed = speed;
            this.Index = index >= 0 ? index : (int) type - 1;
            RefreshConstraints();

        }

        // Re-derives the bounds a reading has to fall inside to be believed.
        //
        // The speed bound comes from the fan ceiling, and the ceiling is not
        // known until the firmware has been asked — which happens after the
        // platform is built, and again later if a fan is seen running higher.
        // Computing it once in the constructor left every machine whose fans
        // are faster than the compiled default silently discarding its own
        // top-end speed readings.
        public virtual void RefreshConstraints() {

            if(this.RateRead != null)
                this.RateRead.SetConstraint(Config.MaxBelievablePercent);

            if(this.Speed != null)
                this.Speed.SetConstraint(
                    Config.FanLevelMax * (100 + Config.MaxBelievableFanSpeedPercentOverMax));

        }

        // Retrieves the fan type
        public virtual BiosData.FanType GetFanType() {
            return this.FanType;
        }

        // Retrieves the fan level [krpm]
        public virtual int GetLevel() {
            byte[] levels = CachedLevels();
            return levels != null && this.Index >= 0 && this.Index < levels.Length
                ? levels[this.Index] : 0;
        }

        // The firmware returns every fan's level in one call, so asking it
        // once per fan asked the same question twice a second for the same
        // answer. Held for a moment instead, which is shorter than the
        // interval anything reads it at and long enough to collapse the pair.
        private static byte[] LevelCache;
        private static int LevelCacheStamp;
        private static readonly object LevelLock = new object();

        private static byte[] CachedLevels() {

            lock(LevelLock) {

                int now = Environment.TickCount;
                if(LevelCache != null
                    && unchecked(now - LevelCacheStamp) < LevelCacheMs)
                    return LevelCache;

                try {
                    LevelCache = Hw.BiosGet(Hw.Bios.GetFanLevel);
                } catch {
                    LevelCache = null;  // Unsupported by WMI/BIOS
                }

                LevelCacheStamp = now;
                return LevelCache;

            }

        }

        private const int LevelCacheMs = 200;

        // Drops the cached levels, so a write is followed by a fresh read
        // rather than by the answer from just before it
        internal static void InvalidateLevels() {
            lock(LevelLock) { LevelCache = null; }
        }

        // Retrieves the fan rate [%]
        public virtual int GetRate() {

            if(this.RateRead == null)
                return 0;

            this.RateRead.Update();
            return this.RateRead.GetValue();

        }

        // Retrieves the fan speed [rpm]
        //
        // The firmware's own tachometer call is preferred, and the Embedded
        // Controller register is the fallback. The register differs between
        // boards and on some of them does not hold a true count; the BIOS
        // call is the same question asked in the one way every machine with
        // this interface answers. A board where it is unsupported throws, and
        // the register answers as it always did.
        public virtual int GetSpeed() {

            if(BiosSpeedWorks) {
                try {

                    int rpm = Hw.BiosGet(() => Hw.Bios.GetFanSpeed((byte) this.Index));

                    if(rpm > 0 && rpm < MaxBelievableRpm)
                        return rpm;

                    // A refusal does not always throw. Check() is the only
                    // thing that turns a bad status code into an exception,
                    // and it returns without doing so whenever
                    // BiosErrorReporting is off — which is precisely the
                    // setting meant for boards that do not implement every
                    // call. There, an unsupported tachometer came back as -1
                    // from the short-buffer guard, the catch never ran, and
                    // the failing round trip was repeated for both fans on
                    // every tick for the life of the process.
                    //
                    // Zero is left alone: a stopped fan is a real answer, not
                    // a refusal, and must not stand the call down.
                    if(rpm < 0 || rpm >= MaxBelievableRpm)
                        BiosSpeedWorks = false;

                } catch {
                    // Asked once, refused once: stop asking. A machine does
                    // not acquire the call while it is running, and retrying
                    // it every second costs a failed WMI round trip per fan.
                    BiosSpeedWorks = false;
                }
            }

            if(this.Speed == null)
                return 0;

            this.Speed.Update();
            return this.Speed.GetValue();

        }

        // Whether the firmware's tachometer call has answered so far. Shared
        // by both fans: the call either exists on a board or it does not.
        private static bool BiosSpeedWorks = true;

        // Above this, the answer is not a fan speed. No laptop fan turns this
        // fast, so a larger figure is a firmware returning something else in
        // those two bytes rather than a reading worth showing.
        private const int MaxBelievableRpm = 15000;

        // Sets the fan level [krpm]
        public virtual void SetLevel(int level) {

            // A fan this build has no setpoint register for is still driven,
            // through the firmware's own fan-level call, which takes every
            // fan at once. Writing nothing here is correct; inventing an
            // address to write to would not be.
            if(this.Level == null)
                return;

            this.Level.SetValue(level);
            InvalidateLevels();

        }

        // Sets the fan rate [%]
        public virtual void SetRate(int rate) {

            if(this.RateWrite == null)
                return;

            this.RateWrite.SetValue(rate);

        }
#endregion

    }

}
