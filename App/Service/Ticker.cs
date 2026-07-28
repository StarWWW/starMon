// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

namespace StarMon.AppService {

    // One periodic slot on the application's once-a-second heartbeat.
    //
    // The tray context runs five of these at different cadences: the fan
    // program, the thermal guard, the foreground refresh, the background
    // history recording, and the notification icon. The arithmetic was written
    // inline in GuiTray.Update() and is easy to get wrong by a tick in either
    // direction, so it lives here where it can be tested.
    //
    // A tick has two passes, and keeping them separate is not incidental:
    //
    //   Rewind()   winds a counter that has run its interval back to zero
    //   Due()      answers whether this is the tick to do the work on, and
    //              advances the counter
    //
    // Every slot is rewound each tick, but not every slot is asked. The
    // foreground refresh and the background recording are mutually exclusive —
    // only one of them runs, depending on whether the window is visible — and
    // the one that is not asked keeps its counter at zero. That is what makes
    // the history recording fire on the very first tick after the window is
    // hidden, rather than up to an interval later. Folding the rewind into
    // Due() would quietly lose that.
    public sealed class Ticker {

        // How many ticks apart the work is done
        public int Interval;

        // Ticks elapsed since the work was last done. Public because the fan
        // program slot is written from outside: applying a fan curve sets it
        // so the next tick picks the program up, and the notification icon
        // reads it to tell whether the program already refreshed the sensors.
        public int Count;

        public Ticker(int interval) {
            this.Interval = interval;
        }

        // Winds the counter back once it has run the full interval
        public void Rewind() {
            if(this.Count >= this.Interval)
                this.Count = 0;
        }

        // Whether this is the tick to do the work on, advancing the counter
        public bool Due() {
            return this.Count++ == 0;
        }

    }

}
