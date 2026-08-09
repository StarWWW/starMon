// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Threading;
using StarMon.Library;

namespace StarMon.AppService {

    // Runs the periodic hardware work off the interface thread.
    //
    // The application keeps a once-a-second heartbeat, and on every beat of it
    // there is hardware to attend to: the fan program decides a level and
    // writes it, the failsafe countdown is extended, the thermal guard reads
    // the hottest sensor and may command the fans, the selected fan mode and
    // graphics power are re-asserted, the backlight effect advances, and the
    // notification icon asks for a temperature.
    //
    // All of that used to happen on the dispatcher, which is the thread that
    // draws. Each of those calls is a round trip — through a mutex shared with
    // the firmware and every other monitoring application on the machine, or
    // through WMI — and one of them, re-asserting the graphics power, sleeps
    // on purpose between two writes because the firmware needs it to. On a
    // machine where the controller is contended, that is the interface not
    // repainting until the hardware answers.
    //
    // Nothing here decides anything. Which work is due is still worked out on
    // the dispatcher, where the counters live; this only carries it out.
    //
    // A beat that arrives while the previous one is still working is dropped
    // rather than queued, for the same reason the Poller drops a reading: the
    // work is a live response to a live machine, so a backlog of it has
    // nothing in it anybody wants, and queueing turns a slow machine into one
    // that never catches up.
    public sealed class Maintainer {

        private int Busy;
        private int Closed;

        // How long the way out waits for a beat still in flight [ms].
        //
        // Comfortably longer than a beat takes on a healthy machine, and short
        // enough that a controller which has stopped answering does not hold
        // the process open. A single Embedded Controller transaction is
        // bounded by Config.EcMutexTimeout, and a beat is several.
        public const int DrainTimeoutMs = 3000;

        // How many beats have been dropped because the previous one was still
        // running. A machine dropping them steadily is one whose hardware is
        // answering more slowly than the heartbeat, which is worth knowing.
        public int Dropped { get; private set; }

        // Whether work is being carried out right now
        public bool IsBusy {
            get { return Thread.VolatileRead(ref this.Busy) != 0; }
        }

        // Whether the way out has been taken and nothing further will run
        public bool IsClosed {
            get { return Thread.VolatileRead(ref this.Closed) != 0; }
        }

        // Hands one beat's work to a background thread, unless the previous
        // beat is still going. Returns whether it was taken up.
        public bool Request(Action work) {

            if(work == null)
                return false;

            if(Interlocked.CompareExchange(ref this.Busy, 1, 0) != 0) {
                this.Dropped++;
                return false;
            }

            // Read after the claim rather than before it, so a beat arriving
            // as the process is closing either loses the claim to Drain or is
            // waited for by it — never slips between the two
            if(Thread.VolatileRead(ref this.Closed) != 0) {
                Interlocked.Exchange(ref this.Busy, 0);
                return false;
            }

            ThreadPool.QueueUserWorkItem(delegate {

                try {

                    work();

                } catch(Exception e) {

                    // A hardware hiccup during periodic maintenance must never
                    // take the application down. It used to be caught on the
                    // dispatcher by the timer handler; the catch has to move
                    // with the work, or an exception here reaches a thread-pool
                    // thread with nobody to answer for it and ends the process.
                    Logger.Error("Maintainer", "Periodic hardware update failed",
                        e.Message);

                } finally {

                    Interlocked.Exchange(ref this.Busy, 0);

                }

            });

            return true;

        }

        // Refuses any further beats and waits for the one in flight.
        //
        // Called on the way out, and it is not optional. Shutdown hands the
        // fans back to the firmware: it clears the overrides, drops the manual
        // toggle and lets the mode go automatic. A beat still running while
        // that happens re-asserts the very things being cleared — one more fan
        // level from a program, or the sticky fan mode being put back — and
        // the machine is left in exactly the state the handback exists to
        // prevent, with nothing in the log to say why.
        public bool Drain(int timeoutMs = DrainTimeoutMs) {

            Interlocked.Exchange(ref this.Closed, 1);

            int waited = 0;
            while(Thread.VolatileRead(ref this.Busy) != 0 && waited < timeoutMs) {
                Thread.Sleep(10);
                waited += 10;
            }

            bool finished = Thread.VolatileRead(ref this.Busy) == 0;

            if(!finished)
                Logger.Warning("Maintainer",
                    "A hardware update was still running at exit",
                    "waited " + waited + " ms; the fans are being handed back "
                        + "regardless");

            return finished;

        }

    }

}
