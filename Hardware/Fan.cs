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

            string complaint = null;
            byte[] levels;

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
                levels = LevelCache;

                if(levels != null)
                    complaint = Verify(levels, now);

            }

            // Logged outside the lock: every fan reading in the application
            // goes through it, and writing to the log is not something to hold
            // it for
            if(complaint != null)
                Logger.Warning("Fan", "Fan level write did not take", complaint);

            return levels;

        }

        private const int LevelCacheMs = 200;

        // Drops the cached levels, so a write is followed by a fresh read
        // rather than by the answer from just before it
        internal static void InvalidateLevels() {
            lock(LevelLock) { LevelCache = null; }
        }

#region Verifying a Write
        // What was last asked for, and when.
        //
        // Not every board takes a fan level. Some accept the write and ignore
        // it, some clamp it to a ceiling that is not the one this build worked
        // out, and one in the device matrix does the first of those — which
        // until now was indistinguishable from the write having worked. The
        // fan curve then went on commanding a speed nothing was applying, and
        // the only symptom was a laptop that ran hot for no stated reason.
        //
        // Checked against the reading the application already takes rather
        // than by reading back straight after the write. That costs no extra
        // access to the hardware, and it avoids the trap of asking a firmware
        // what its fans are doing in the same millisecond it was told to
        // change them: the honest answer then is still the old one, and a
        // check that treated it as a refusal would cry wolf on every machine.
        private static byte[] Requested;
        private static int RequestedStamp;
        private static bool Reported;

        // The reading before this one, kept because a single sample cannot
        // tell a board that refused from fans that are still spinning up
        private static byte[] LastSeen;

        // Whether the board is reporting the level it was given.
        //
        // A request above the ceiling is answered by the ceiling, which is the
        // firmware doing its job rather than ignoring the write - the Maximum
        // button asks for Config.FanLevelMax precisely so it lands there.
        internal static bool Agrees(byte[] asked, byte[] actual, int ceiling) {

            if(asked == null || actual == null)
                return false;

            int count = Math.Min(asked.Length, actual.Length);
            if(count == 0)
                return false;

            for(int i = 0; i < count; i++) {

                if(asked[i] == actual[i])
                    continue;

                // Clamped to the ceiling: asked for more than the board will
                // give, and given everything it has
                if(ceiling > 0 && asked[i] > ceiling && actual[i] >= ceiling)
                    continue;

                return false;

            }

            return true;

        }

        // Whether a write can be said to have been ignored.
        //
        // Three things have to hold, and each of them is here because leaving
        // it out produced a warning on hardware doing exactly as it was told:
        //
        //   The reading disagrees with the request, and not by having been
        //   clamped to the ceiling.
        //
        //   The reading has stopped moving. Fans take seconds to spin up, and
        //   the level register reports where they are rather than where they
        //   were sent - so a board asked for 56 answers 40, then 48, then 56.
        //   Two readings that agree with each other are fans that have
        //   settled; two that differ are fans on their way.
        //
        //   There is a previous reading to compare against at all. Without one
        //   nothing can be concluded, and concluding anyway is what produced
        //   "asked for 56/56, the firmware reports 40/38" about a board that
        //   reached 56 four seconds later.
        internal static bool DidNotTake(byte[] asked, byte[] actual,
            byte[] previous, int ceiling) {

            if(asked == null || actual == null || previous == null)
                return false;

            // 0xFF is a release rather than a level, and there is nothing to
            // have taken. Checked here as well as where a request is recorded:
            // this method is what says whether a board misbehaved, and it
            // should mean that for whatever it is handed rather than only for
            // what one caller happens to pass it.
            if(HasRelease(asked))
                return false;

            if(Agrees(asked, actual, ceiling))
                return false;

            return Same(actual, previous);

        }

        private static bool Same(byte[] a, byte[] b) {

            if(a == null || b == null || a.Length != b.Length)
                return false;

            for(int i = 0; i < a.Length; i++)
                if(a[i] != b[i])
                    return false;

            return true;

        }

        // How many writes have been seen not to take. Counted so a test can
        // assert the detection rather than assume it.
        internal static int LevelWriteMismatches { get; private set; }

        // Records what a write asked for, so the next reading can be compared
        // against it
        internal static void NoteLevelRequest(byte[] levels) {

            lock(LevelLock) {

                // 0xFF is not a fan level. It is the sentinel that clears any
                // custom level and hands the speeds back to the firmware, so
                // there is nothing for the board to have taken and nothing to
                // compare a reading against. Checking it anyway is how a
                // perfectly ordinary switch back to Automatic came out in the
                // log as "the firmware reports 50/50" against a request for
                // 255 - a complaint about a number nobody asked for.
                if(levels == null || HasRelease(levels)) {
                    Requested = null;
                    LastSeen = null;
                    return;
                }

                Requested = (byte[]) levels.Clone();
                RequestedStamp = Environment.TickCount;
                LastSeen = null;

            }

        }

        private static bool HasRelease(byte[] levels) {

            for(int i = 0; i < levels.Length; i++)
                if(levels[i] == byte.MaxValue)
                    return true;

            return false;

        }

        // Compares a fresh reading against what was last asked for, and
        // returns what to say about it, or null when there is nothing to say.
        //
        // Called with the level lock held.
        private static string Verify(byte[] actual, int now) {

            if(Requested == null || Config.FanLevelVerifyDelayMs < 0)
                return null;

            // Too soon to tell. The board is allowed a moment to act on the
            // write before being accused of having ignored it.
            if(unchecked(now - RequestedStamp) < Config.FanLevelVerifyDelayMs)
                return null;

            byte[] asked = Requested;
            byte[] previous = LastSeen;

            LastSeen = (byte[]) actual.Clone();

            if(!DidNotTake(asked, actual, previous, Config.FanLevelMax)) {

                // Agreement is only conclusive when the reading matches; a
                // reading that is still on its way to the requested level is
                // neither an agreement nor a refusal, and the request has to
                // stay on the books until the fans have stopped moving
                if(Agrees(asked, actual, Config.FanLevelMax)) {
                    Requested = null;
                    Reported = false;
                }

                return null;

            }

            // Judged. The next write starts the question again.
            Requested = null;

            LevelWriteMismatches++;

            // Said once per episode. A fan curve holding a steady level
            // rewrites it whenever the temperature crosses a threshold, and a
            // board that ignores one write ignores all of them — so the
            // alternative is this line every few seconds for the life of the
            // process.
            if(Reported)
                return null;

            Reported = true;

            int count = Math.Min(asked.Length, actual.Length);

            return "asked for " + Describe(asked, count)
                + ", the firmware reports " + Describe(actual, count)
                + " — this board is not applying the level it is given, so the "
                + "fan curve is not driving the fans";

        }

        private static string Describe(byte[] levels, int count) {

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            for(int i = 0; i < count; i++) {
                if(i > 0)
                    sb.Append('/');
                sb.Append(levels[i]);
            }

            return sb.ToString();

        }

        // Forgets what was asked for, so a test can start again
        internal static void ResetLevelVerification() {
            lock(LevelLock) {
                Requested = null;
                LastSeen = null;
                Reported = false;
                LevelWriteMismatches = 0;
            }
        }
#endregion

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
