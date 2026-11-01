// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Concurrent;
using System.Threading;
using StarMon.Library;

namespace StarMon.AppService {

    // What the user asks of the hardware, carried out off the thread that
    // draws, one request at a time and in the order they were made.
    //
    // A click on the dashboard used to make its firmware calls right there on
    // the interface thread. Each is a WMI round trip of tens of milliseconds,
    // a change of fan mode is several of them, and the graphics power is two
    // writes with a deliberate 200 ms pause between - so the window stopped
    // repainting for the length of whatever had been asked. On one thread of
    // its own the window answers at once, and the requests still land in the
    // order they were made: a fan mode and the graphics power it raises can
    // never be applied the wrong way round.
    public sealed class HardwareQueue {

        public static readonly HardwareQueue Default = new HardwareQueue();

        private readonly BlockingCollection<Work> Pending =
            new BlockingCollection<Work>(new ConcurrentQueue<Work>());

        private Thread Worker;
        private readonly object StartLock = new object();
        private int Busy;

        private sealed class Work {
            public Action Action;
            public string What;
        }

        // Whether anything is waiting or running
        public bool IsIdle {
            get { return this.Pending.Count == 0 && Volatile.Read(ref this.Busy) == 0; }
        }

        // Queues a request. Refused, and said so, once the queue is closed:
        // nothing may reach the hardware after the fans have been handed back.
        public bool Post(Action action, string what) {

            if(action == null)
                return false;

            EnsureWorker();

            try {
                this.Pending.Add(new Work { Action = action, What = what ?? "" });
                return true;
            } catch(InvalidOperationException) {
                Logger.Warning("Hardware", "Request after shutdown dropped", what ?? "");
                return false;
            }

        }

        // Stops taking requests and waits for the ones already queued, up to
        // the timeout. Called before the fans are handed back on exit, so no
        // late request can undo the hand-back.
        public bool Drain(int timeoutMs) {

            this.Pending.CompleteAdding();

            Thread worker = this.Worker;
            return worker == null || worker.Join(timeoutMs);

        }

        private void EnsureWorker() {

            if(this.Worker != null)
                return;

            lock(this.StartLock) {

                if(this.Worker != null)
                    return;

                this.Worker = new Thread(Loop) {
                    IsBackground = true,
                    Name = "StarMon hardware requests"
                };
                this.Worker.Start();

            }

        }

        private void Loop() {

            foreach(Work work in this.Pending.GetConsumingEnumerable()) {

                Interlocked.Exchange(ref this.Busy, 1);

                try {
                    work.Action();
                    if(work.What.Length > 0)
                        Logger.Gui("Hardware", work.What);
                } catch(Exception e) {
                    Logger.Error("Hardware", "Request failed: " + work.What, e.Message);
                } finally {
                    Interlocked.Exchange(ref this.Busy, 0);
                }

            }

        }

    }

}
