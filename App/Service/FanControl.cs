// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using StarMon.Hardware.Bios;
using StarMon.Hardware.Platform;
using StarMon.Library;

namespace StarMon.AppService {

    // What the user asked the fans to do
    public enum FanRequest {
        Automatic,   // Hand back to the firmware, in a named mode
        Constant,    // Hold both fans at chosen levels
        Maximum,     // Both fans at the hardware ceiling
        Off,         // Both fans stopped
        Program      // Run a saved fan program
    }

    // How much power the graphics chip is allowed to draw
    public enum GpuPower {
        Base,    // The base TGP the card ships with
        Custom,  // The chassis' own higher limit, where there is one
        Boost    // That, plus the firmware's dynamic boost
    }

    // What a Constant request actually resolves to.
    //
    // The firmware will not accept every combination of levels: it insists at
    // least one fan keeps turning, so "both at zero" has to be expressed as
    // the off state rather than as levels of zero. And both at the ceiling is
    // better asked for as the maximum state than as two levels that happen to
    // equal it. Everything between is ordinary levels.
    public enum ConstantAction {
        SwitchOff,
        SwitchToMaximum,
        SetLevels
    }

    // Converts between a drawn curve and a saved fan program.
    //
    // Here rather than in the view model that owns the curve, for one reason:
    // it is the piece most likely to be silently wrong. A curve saved and read
    // back that does not match is not a crash, it is a machine that quietly
    // cools itself differently from the picture on the screen — so it is put
    // where the tests can reach it without a window.
    public static class FanCurve {

        // The curve as a program the firmware runner understands.
        //
        // The key at zero degrees is what makes a temperature below the first
        // column resolve to a level rather than to nothing at all. Both fans
        // get the same level: the firmware drives them together on every board
        // seen so far, and pretending otherwise invents a distinction the
        // hardware does not honour.
        public static FanProgramData ToProgram(string name, int[] columns,
            int[] percent, int ceiling, BiosData.FanMode mode,
            BiosData.GpuPowerLevel power) {

            System.Collections.Generic.SortedDictionary<byte, byte[]> levels =
                new System.Collections.Generic.SortedDictionary<byte, byte[]>();

            byte first = ToLevel(percent.Length > 0 ? percent[0] : 0, ceiling);
            levels[0] = new byte[] { first, first };

            for(int i = 0; i < columns.Length && i < percent.Length; i++) {
                byte level = ToLevel(percent[i], ceiling);
                levels[(byte) columns[i]] = new byte[] { level, level };
            }

            return new FanProgramData(name, mode, power, levels);

        }

        // A saved program back as curve percentages.
        //
        // A program's steps are whatever temperatures its author chose, which
        // need not be the editor's columns, so each column takes the level in
        // force at that temperature — the last step at or below it. That is
        // how the program is followed at runtime, so what the editor draws is
        // what the machine would actually do.
        public static int[] ReadCurve(FanProgramData program, int[] columns, int ceiling) {

            int[] percent = new int[columns.Length];

            if(program == null || program.Level == null)
                return percent;

            for(int i = 0; i < columns.Length; i++) {

                byte level = 0;

                foreach(System.Collections.Generic.KeyValuePair<byte, byte[]> step
                    in program.Level)
                    if(step.Key <= columns[i] && step.Value != null && step.Value.Length > 0)
                        level = step.Value[0];

                percent[i] = ToPercent(level, ceiling);

            }

            return percent;

        }

        public static byte ToLevel(int percent, int ceiling) {

            int level = (int) Math.Round(Clamp(percent) / 100.0 * ceiling);

            if(level < 0) level = 0;
            if(level > ceiling) level = ceiling;

            return (byte) level;

        }

        public static int ToPercent(byte level, int ceiling) {
            return ceiling <= 0 ? 0
                : Clamp((int) Math.Round(level / (double) ceiling * 100.0));
        }

        private static int Clamp(int percent) {
            return percent < 0 ? 0 : percent > 100 ? 100 : percent;
        }

    }

    // Applies fan settings, in the order the firmware needs them.
    //
    // The ordering is not obvious and getting it wrong fails quietly: the
    // maximum-speed and switched-off states are overrides that sit above the
    // levels, so setting a level while either is in force changes a number
    // nothing is reading. Every path here therefore clears whichever of those
    // is active before it asks for anything else.
    public static class FanControl {

        // Works out which setting the hardware is actually in, from a reading.
        //
        // Neither levels nor overrides alone can say. Fans held at zero and
        // fans left to the firmware both report levels of zero; and — the
        // subtler half — fans left to the firmware still report the levels
        // they happen to be turning at, so a nonzero level does not mean the
        // user set one. The Embedded Controller's manual toggle is the bit
        // that records whose levels they are, and it is the deciding signal.
        // Getting this wrong does not look like a wrong answer: it looks like
        // the interface refusing the user's request, because a second later
        // the selector jumps back to whatever this returned.
        public static FanRequest Identify(bool isProgramRunning,
            bool isMax, bool isOff, bool isManual,
            int levelCpu, int levelGpu, int ceiling) {

            if(isProgramRunning)
                return FanRequest.Program;

            // Maximum is applied as manual levels at the ceiling rather than
            // through the firmware's own maximum state, deliberately — so it
            // has to be recognised both ways. The firmware driving its own
            // fans flat out under load is not it: that is still automatic.
            if(isMax || (isManual && ceiling > 0
                && levelCpu >= ceiling && levelGpu >= ceiling))
                return FanRequest.Maximum;

            // Switched off is a constant setting of zero, and the manual bit
            // set means the levels on show are the user's held levels
            if(isOff || isManual)
                return FanRequest.Constant;

            return FanRequest.Automatic;

        }

        // Which of the three shapes a Constant request takes
        public static ConstantAction ResolveConstant(
            int levelCpu, int levelGpu, int ceiling) {

            if(levelCpu <= 0 && levelGpu <= 0)
                return ConstantAction.SwitchOff;

            if(levelCpu >= ceiling && levelGpu >= ceiling)
                return ConstantAction.SwitchToMaximum;

            return ConstantAction.SetLevels;

        }

        // Asks the firmware for a graphics power level, if this machine has
        // one to ask for.
        //
        // The level is a request, not a setting: what the firmware does with
        // it depends on the chassis, the power source and its own thermal
        // headroom. Several models — this Victus among them — report no
        // support at all, and on those the call is skipped rather than made
        // and silently ignored, so the interface can say so.
        public static bool ApplyGpuPower(Platform platform, GpuPower level) {

            try {

                // Deliberately NOT gated on GpuPowerSupported.
                //
                // That flag means one thing only: GetGpuPower — the *read* —
                // was refused. This board refuses it, and the write was being
                // skipped on the strength of that, so the extra graphics power
                // was never asked for at all. Whether a board will report its
                // TGP and whether it will accept a new one are two separate
                // questions, and inferring the second from the first is what
                // cost this machine twenty watts: its card defaults to 60 W
                // and will go to 80 W, and only this call releases it.
                //
                // A write the firmware does not implement is refused and
                // swallowed, which costs nothing. A write never attempted
                // costs the headroom.
                BiosData.GpuPowerLevel wanted =
                    level == GpuPower.Boost ? BiosData.GpuPowerLevel.Maximum
                    : level == GpuPower.Custom ? BiosData.GpuPowerLevel.Medium
                    : BiosData.GpuPowerLevel.Minimum;

                platform.System.SetGpuPower(new BiosData.GpuPowerData(wanted));
                return true;

            } catch(Exception e) {

                Logger.Error("Fan", "Setting the graphics power level failed",
                    e.Message);
                return false;

            }

        }

        public static void Apply(Platform platform, FanProgram program,
            FanRequest request, int levelCpu, int levelGpu, string programName) {

            bool isMax = Read(() => platform.Fans.GetMax());
            bool isOff = Read(() => platform.Fans.GetOff());

            switch(request) {

                case FanRequest.Program:

                    if(string.IsNullOrEmpty(programName)
                        || !Config.FanProgram.ContainsKey(programName))
                        return;

                    platform.ClearFanModeSticky();
                    Release(platform, isMax, isOff);
                    program.Run(programName);
                    break;

                case FanRequest.Off:

                    platform.ClearFanModeSticky();
                    program.Terminate();

                    if(!isOff) {
                        if(isMax) platform.Fans.SetMax(false);
                        platform.Fans.SetOff(true);
                    }

                    break;

                case FanRequest.Maximum:

                    program.Terminate();
                    Release(platform, isMax, isOff);

                    // Driven as an ordinary level at the ceiling rather than
                    // through the firmware's own maximum-speed state, so what
                    // is in force is something the fan program logic can
                    // reason about rather than a mode that overrides it
                    platform.Fans.SetLevels(new byte[] {
                        (byte) Config.FanLevelMax, (byte) Config.FanLevelMax });

                    // Maximum fans is a request for the machine to work hard,
                    // not just to be loud. The firmware holds the processor
                    // and graphics limits down outside its performance mode,
                    // so asking for the fans alone buys cooling for headroom
                    // that has not been released — which is the noise without
                    // the speed. The mode goes with them.
                    try {
                        platform.SetFanModeSticky(BiosData.FanMode.Performance);
                    } catch(Exception e) {
                        Logger.Error("Fan", "Performance mode could not be set",
                            e.Message);
                    }

                    break;

                case FanRequest.Constant:

                    platform.ClearFanModeSticky();
                    program.Terminate();

                    switch(ResolveConstant(levelCpu, levelGpu, Config.FanLevelMax)) {

                        case ConstantAction.SwitchOff:
                            if(!isOff) platform.Fans.SetOff(true);
                            break;

                        case ConstantAction.SwitchToMaximum:
                            if(!isMax) platform.Fans.SetMax(true);
                            break;

                        default:

                            Release(platform, isMax, isOff);

                            platform.Fans.SetLevels(new byte[] {
                                (byte) Clamp(levelCpu), (byte) Clamp(levelGpu) });

                            // Re-asserting the mode is what makes the levels
                            // take effect; without it they are accepted and
                            // then ignored
                            platform.Fans.SetMode(platform.Fans.GetMode());

                            break;

                    }

                    break;

                default:

                    program.Terminate();
                    Release(platform, isMax, isOff);

                    // 0xFF clears any custom level, which is what hands
                    // control of the speeds back to the firmware
                    platform.Fans.SetLevels(new byte[] {
                        Byte.MaxValue, Byte.MaxValue });

                    // SetLevels raised the manual toggle on the way in; the
                    // whole point of this branch is that nothing manual is
                    // left in force, and the toggle is also how the state is
                    // read back — left set, every later reading would say the
                    // firmware's own levels were the user's
                    try { platform.Fans.SetManual(false); } catch { }

                    BiosData.FanMode mode;
                    if(!Enum.TryParse(programName ?? "", out mode))
                        mode = Read(() => platform.Fans.GetMode());

                    platform.SetFanModeSticky(mode);

                    break;

            }

        }

        // Clears whichever override is in force, so that whatever is asked
        // for next is the thing that actually takes effect
        private static void Release(Platform platform, bool isMax, bool isOff) {
            if(isMax) platform.Fans.SetMax(false);
            if(isOff) platform.Fans.SetOff(false);
        }

        private static int Clamp(int level) {
            if(level < 0) return 0;
            return level > Config.FanLevelMax ? Config.FanLevelMax : level;
        }

        private static T Read<T>(Func<T> read) {
            try { return read(); } catch { return default(T); }
        }

    }

}
