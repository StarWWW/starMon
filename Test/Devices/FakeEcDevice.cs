// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Collections.Generic;
using StarMon.Hardware.Ec;

namespace StarMon.Test.Devices {

    // An Embedded Controller backed by a register file, for standing in for a
    // whole machine rather than for a protocol.
    //
    // TestEmbeddedController already has a fake, and it is the right one for
    // what it does: it scripts the status port to produce the exchanges a
    // working controller never produces. This one answers a different
    // question. The register map this application compiles in comes from one
    // board's ACPI tables — Hardware/EcData.cs says so in a comment — and the
    // failures that follow from that on other boards are not protocol
    // failures. They are a register that holds something else, or nothing at
    // all, while every exchange succeeds.
    //
    // So this models the board: 256 registers, each of which may be present
    // and hold a value, or be absent. An absent register is not an error the
    // caller can see directly — the controller simply never raises the
    // output-buffer flag for it, which is what a real one does when asked for
    // a register its firmware does not implement.
    //
    // Every access is counted, per register. That is the point of it: a claim
    // like "the second fan is never polled on a single-fan board" is only
    // worth making if something can check it.
    internal sealed class FakeEcDevice : EmbeddedControllerAbstract {

        // The board's registers. Words are little-endian across two of them,
        // low byte first, which is how ReadWordImpl assembles them.
        private readonly byte[] Cell = new byte[256];

        // Registers this board does not implement
        private readonly HashSet<byte> Absent = new HashSet<byte>();

        // Reads and writes seen, per register
        private readonly int[] Reads = new int[256];
        private readonly int[] Writes = new int[256];

        // Every write in order, so that a sequence can be asserted and not
        // just an end state. Fan control's whole reason for existing is the
        // order it does things in.
        internal readonly List<KeyValuePair<byte, byte>> WriteLog =
            new List<KeyValuePair<byte, byte>>();

        // Set to have the controller accept commands and never answer, which
        // is what a busy or wedged one looks like
        internal bool Answers = true;

        // Set to have the lock never be granted, as when another application
        // is holding the EC mutex
        internal bool LockAvailable = true;

        // How many times the lock was asked for and refused
        internal int LockRefusals;

        private enum Phase { Idle, ReadRegister, WriteRegister, WriteValue, DataReady }
        private Phase State = Phase.Idle;
        private byte Pending;
        private byte OutByte;

        public override void Initialize() { IsInitialized = true; }
        public override void Close() { IsInitialized = false; }

        public override bool Request(int timeout) {
            if(LockAvailable)
                return true;
            LockRefusals++;
            return false;
        }

        public override void Release() { }

#region Board description
        // Gives a register a value, marking it present
        internal FakeEcDevice Set(byte register, byte value) {
            Cell[register] = value;
            Absent.Remove(register);
            return this;
        }

        internal FakeEcDevice Set(EmbeddedControllerData.Register register, byte value) {
            return Set((byte) register, value);
        }

        // Gives a register pair a word value, low byte first
        internal FakeEcDevice SetWord(EmbeddedControllerData.Register register, ushort value) {
            byte at = (byte) register;
            Set(at, (byte) (value & 0xFF));
            Set((byte) (at + 1), (byte) (value >> 8));
            return this;
        }

        // Marks a register as one this board does not carry. Reading it
        // succeeds at the protocol level and produces nothing, which is the
        // case the dormancy mechanism exists for.
        internal FakeEcDevice Remove(EmbeddedControllerData.Register register) {
            Absent.Add((byte) register);
            return this;
        }

        internal FakeEcDevice RemoveWord(EmbeddedControllerData.Register register) {
            byte at = (byte) register;
            Absent.Add(at);
            Absent.Add((byte) (at + 1));
            return this;
        }

        internal bool IsAbsent(EmbeddedControllerData.Register register) {
            return Absent.Contains((byte) register);
        }
#endregion

#region Observation
        internal byte Peek(EmbeddedControllerData.Register register) {
            return Cell[(byte) register];
        }

        internal int ReadCount(EmbeddedControllerData.Register register) {
            return Reads[(byte) register];
        }

        internal int WriteCount(EmbeddedControllerData.Register register) {
            return Writes[(byte) register];
        }

        internal int TotalReads {
            get {
                int sum = 0;
                for(int i = 0; i < Reads.Length; i++) sum += Reads[i];
                return sum;
            }
        }

        internal void ResetCounts() {
            Array.Clear(Reads, 0, Reads.Length);
            Array.Clear(Writes, 0, Writes.Length);
            WriteLog.Clear();
        }
#endregion

#region Port protocol
        protected override byte ReadIoPort(Port port) {

            if(port == Port.Command) {

                // The input buffer is never reported full: this fake is about
                // what the registers hold, and TestEmbeddedController already
                // covers what happens when the handshake itself misbehaves.
                return State == Phase.DataReady ? (byte) Status.OutFull : (byte) 0;

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
                    State = Phase.ReadRegister;
                else if(value == (byte) Command.Write)
                    State = Phase.WriteRegister;

                return;

            }

            switch(State) {

                case Phase.ReadRegister:

                    Reads[value]++;

                    // An absent register leaves the controller with nothing to
                    // hand back. It does not fail the exchange, it just never
                    // completes it — so the caller's wait runs out.
                    if(Absent.Contains(value) || !Answers) {
                        State = Phase.Idle;
                    } else {
                        OutByte = Cell[value];
                        State = Phase.DataReady;
                    }

                    break;

                case Phase.WriteRegister:

                    Pending = value;
                    State = Phase.WriteValue;
                    break;

                case Phase.WriteValue:

                    Writes[Pending]++;
                    WriteLog.Add(new KeyValuePair<byte, byte>(Pending, value));

                    // A write to a register the board does not carry is
                    // accepted and discarded, which is what makes writing to
                    // the wrong address so quiet a failure on real hardware.
                    if(!Absent.Contains(Pending))
                        Cell[Pending] = value;

                    State = Phase.Idle;
                    break;

            }

        }
#endregion

    }

}
