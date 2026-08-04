// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using StarMon.Hardware.Ec;
using StarMon.Library;

namespace StarMon.Test {

    // Exercises the Embedded Controller wait-and-retry protocol against a fake
    // I/O port, with no kernel driver and no real controller involved.
    //
    // These cover the failure that made the application desynchronize the EC
    // protocol and, through it, corrupt the ACPI battery fuel-gauge readings:
    // a wait loop with no delay in it exhausted every iteration in microseconds
    // and reported failure, after which a fail-open bypass latched permanently
    // and every subsequent read went out blind.
    [TestSuite(Order = 40)]
    public static class TestEmbeddedController {

#region Fake Controller
        // A stand-in for the controller's two I/O ports. The behaviour of the
        // status port is scripted by the test, which is the whole point: the
        // interesting cases are the ones a healthy controller never produces.
        internal class FakeEc : EmbeddedControllerAbstract {

            // Where in a read exchange the controller currently is. A real one
            // only raises the output-buffer-full flag once it has been asked
            // for a register and has the answer ready, which is what makes a
            // flush before the exchange starts safe.
            private enum Phase { Idle, AwaitingRegister, DataReady }
            private Phase State = Phase.Idle;
            private byte OutByte;

            // Whether the controller ever completes a read. When false it
            // accepts the command but never produces an answer, which is the
            // pathological case the wait loop has to cope with.
            internal bool Answers = true;

            // Reports the output buffer as permanently full, for the tests
            // that drive the wait routines directly rather than through a
            // whole exchange
            internal bool ForceOutFull = false;

            // A byte left over in the output buffer from an earlier, abandoned
            // exchange, which a new one must discard rather than return
            internal bool HasStaleOutput = false;

            // How many times the status and data ports have been read
            internal int StatusReads = 0;
            internal int DataReads = 0;

            // The values the data port hands back, in order
            internal Queue<byte> DataQueue = new Queue<byte>();

            // Everything written to the data port, in order
            internal List<byte> Written = new List<byte>();

            public override void Initialize() { IsInitialized = true; }
            public override void Close() { IsInitialized = false; }
            public override bool Request(int timeout) { return true; }
            public override void Release() { }

            protected override byte ReadIoPort(Port port) {

                if(port == Port.Command) {
                    StatusReads++;

                    byte status = 0;
                    if(ForceOutFull || HasStaleOutput || State == Phase.DataReady)
                        status |= (byte) Status.OutFull;

                    // The input buffer is never reported full, so WaitWrite
                    // always succeeds and the tests stay focused on reads
                    return status;
                }

                DataReads++;

                // A stale byte is whatever is drained first, and must never be
                // mistaken for the value of the register just requested
                if(HasStaleOutput) {
                    HasStaleOutput = false;
                    return 0xEE;
                }

                if(State == Phase.DataReady) {
                    State = Phase.Idle;
                    return OutByte;
                }

                return 0;

            }

            protected override void WriteIoPort(Port port, byte value) {

                if(port == Port.Command) {
                    if(value == (byte) Command.Read)
                        State = Phase.AwaitingRegister;
                    return;
                }

                Written.Add(value);

                // The register being asked for completes the request, and the
                // controller makes the answer available
                if(State == Phase.AwaitingRegister) {
                    if(Answers) {
                        OutByte = DataQueue.Count > 0 ? DataQueue.Dequeue() : (byte) 0;
                        State = Phase.DataReady;
                    } else {
                        State = Phase.Idle;
                    }
                }

            }

            // Test access to the protected wait and flush routines
            internal bool CallWaitRead(byte register = 0) { return WaitRead(register); }
            internal void CallFlushObf() { FlushObf(); }
            internal int FailCount(byte register = 0) { return WaitReadFailCount(register); }

        }
#endregion

        public static void Run() {

            SelfTest.Group("Embedded Controller: wait protocol");

            TestWaitActuallyWaits();
            TestWaitRespectsItsBudget();
            TestTransactionFitsInsideTheMutexTimeout();
            TestWaitReadSelfHeals();
            TestBypassDoesNotLeakBetweenRegisters();
            TestFlushDropsStaleByte();
            TestFailedReadIsReported();

        }

        // A wait for a condition that never arrives has to actually wait. The
        // regression this catches is a loop with no delay in it, which burns
        // through its whole budget in microseconds and so reports a merely
        // busy controller as a broken one.
        private static void TestWaitActuallyWaits() {

            FakeEc ec = new FakeEc { Answers = false };

            Stopwatch clock = Stopwatch.StartNew();
            bool result = ec.CallWaitRead();
            clock.Stop();

            SelfTest.Check(!result,
                "a wait for a flag that never rises fails");

            SelfTest.Check(ec.StatusReads > 1,
                "the wait polls the status port repeatedly ("
                    + ec.StatusReads + " polls)");

            // Allow the timer a little slack at the bottom; the point is that
            // it is nowhere near instant
            long floor = Math.Max(1, Config.EcWaitTimeoutMs / 2);
            SelfTest.Check(clock.ElapsedMilliseconds >= floor,
                "the wait takes at least " + floor + " ms (took "
                    + clock.ElapsedMilliseconds + " ms)");

        }

        // The other half of the same property, and the one that actually bit:
        // the wait must not overrun its budget either. Sleeping between polls
        // looks like it costs a millisecond and really costs a scheduler tick
        // of about fifteen, which multiplies out into transactions that hold
        // the shared lock for seconds.
        private static void TestWaitRespectsItsBudget() {

            FakeEc ec = new FakeEc { Answers = false };

            Stopwatch clock = Stopwatch.StartNew();
            ec.CallWaitRead();
            clock.Stop();

            // Generous, to stay reliable on a loaded machine, but far below
            // what a per-iteration sleep would produce
            long ceiling = Config.EcWaitTimeoutMs * 3 + 50;

            SelfTest.Check(clock.ElapsedMilliseconds <= ceiling,
                "the wait stays within " + ceiling + " ms (took "
                    + clock.ElapsedMilliseconds + " ms); a budget counted in "
                    + "iterations rather than time overruns here");

        }

        // The invariant that ties the wait budget to the lock timeout. A whole
        // failed word read happens inside one acquisition of the shared mutex,
        // so if it can outlast EcMutexTimeout, every other caller starts
        // reporting that it could not acquire the lock.
        private static void TestTransactionFitsInsideTheMutexTimeout() {

            FakeEc ec = new FakeEc { Answers = false };

            Stopwatch clock = Stopwatch.StartNew();
            ushort value;
            ec.TryReadWord(0xB0, out value);
            clock.Stop();

            SelfTest.Check(clock.ElapsedMilliseconds < Config.EcMutexTimeout,
                "a failed word read (" + clock.ElapsedMilliseconds
                    + " ms) completes inside the mutex timeout ("
                    + Config.EcMutexTimeout + " ms), so concurrent callers "
                    + "do not time out waiting for the lock");

        }

        // Once the failure count is past the limit, reads are allowed through
        // blind as a last resort. That bypass must not latch: every call still
        // attempts a real wait first, so a controller that recovers is noticed.
        private static void TestWaitReadSelfHeals() {

            FakeEc ec = new FakeEc { Answers = false };

            // Drive the failure count past the limit
            for(int i = 0; i <= Config.EcFailLimit; i++)
                ec.CallWaitRead();

            SelfTest.Check(ec.FailCount() > Config.EcFailLimit,
                "repeated failures push the count past the limit");

            SelfTest.Check(ec.CallWaitRead(),
                "past the limit, a read is allowed through blind");

            // The controller starts answering again
            ec.ForceOutFull = true;
            int before = ec.StatusReads;

            bool healed = ec.CallWaitRead();

            SelfTest.Check(healed,
                "a recovered controller satisfies the wait");

            SelfTest.Check(ec.StatusReads > before,
                "the status port is still polled while over the limit "
                    + "(the bypass must not skip the wait entirely)");

            SelfTest.Equal(0, ec.FailCount(),
                "a successful wait clears the failure count");

        }

        // The bypass is per register, and has to stay that way.
        //
        // With one counter for the whole controller, a board missing a handful
        // of the configured temperature probes drove the count past the limit
        // inside a single polling pass — and the next read of an entirely
        // healthy register was then let through blind, reporting whatever byte
        // was left in the port as that register's value. A register that has
        // never failed must never inherit another one's failures.
        private static void TestBypassDoesNotLeakBetweenRegisters() {

            FakeEc ec = new FakeEc { Answers = false };

            // One absent register fails far past the limit
            for(int i = 0; i <= Config.EcFailLimit + 4; i++)
                ec.CallWaitRead(0x4B);

            SelfTest.Check(ec.FailCount(0x4B) > Config.EcFailLimit,
                "the absent register's own count is past the limit");

            SelfTest.Equal(0, ec.FailCount(0xB0),
                "a register that has not been read carries no failures");

            SelfTest.Check(!ec.CallWaitRead(0xB0),
                "a different register's first failed wait is still a failure, "
                    + "not a blind read borrowed from the absent one");

        }

        // A byte left in the output buffer by an abandoned exchange has to be
        // discarded before a new one starts, or it is handed back as though it
        // were the value of the register just requested, and every following
        // read stays one step behind.
        private static void TestFlushDropsStaleByte() {

            FakeEc ec = new FakeEc { HasStaleOutput = true };

            ec.CallFlushObf();

            SelfTest.Check(!ec.HasStaleOutput,
                "the stale output byte is drained");

            SelfTest.Check(ec.DataReads == 1,
                "draining reads the data port exactly once");

            // A flush with nothing to drain must not touch the data port
            FakeEc clean = new FakeEc();
            clean.CallFlushObf();

            SelfTest.Equal(0, clean.DataReads,
                "a clean output buffer is left alone");

            // The case that matters: a full read on a controller with a byte
            // left over must return the register's value, not the leftover.
            // Without the flush this returns 0xEE, and every later read is
            // one exchange behind for as long as the application runs.
            FakeEc stale = new FakeEc { HasStaleOutput = true };
            stale.DataQueue.Enqueue(0x42);

            byte value;
            bool ok = stale.TryReadByte(0x57, out value);

            SelfTest.Check(ok, "a read succeeds despite a stale output byte");
            SelfTest.Equal((byte) 0x42, value,
                "the read returns the register value, not the stale byte");

        }

        // A read that fails every retry returns zero, which is a perfectly
        // plausible register value. Callers that care have to be able to tell
        // the two apart, which is what the Try form is for.
        private static void TestFailedReadIsReported() {

            FakeEc broken = new FakeEc { Answers = false };

            byte value;
            bool ok = broken.TryReadByte(0x57, out value);

            SelfTest.Check(!ok,
                "a read that never completes reports failure");
            SelfTest.Equal((byte) 0, value,
                "a failed read yields zero");
            SelfTest.Equal((byte) 0, broken.ReadByte(0x57),
                "the plain read form still returns zero on failure");

            FakeEc working = new FakeEc();
            working.DataQueue.Enqueue(0x42);

            SelfTest.Check(working.TryReadByte(0x57, out value),
                "a read from a responsive controller succeeds");
            SelfTest.Equal((byte) 0x42, value,
                "the value read is the one the controller supplied");

        }

    }

}
