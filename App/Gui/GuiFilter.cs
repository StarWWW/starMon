// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;

namespace StarMon.AppGui {

    // Handles the message one instance of the application sends to another.
    //
    // Only one copy may run, because two of them writing to the Embedded
    // Controller is exactly the contention its lock exists to prevent. So a
    // second copy does not open a window: it broadcasts, and the running one
    // answers by showing itself.
    //
    // This used to be an IMessageFilter added to the Windows Forms message
    // pump. It is now a hook on the notification icon's own window, which is
    // the only top-level window this application reliably owns — and broadcast
    // messages are delivered to top-level windows and nothing else.
    public class GuiFilter {

        // Last-received identifier, to distinguish duplicate messages
        private IntPtr LastId;

        // Actions depend on the previously-received message as well
        private Gui.MessageParam LastParam;

        // When that message arrived
        private int LastParamAt;

        // How long an automatic start goes on suppressing a request to show
        // the window.
        //
        // The suppression is for one specific sequence: a scheduled task
        // starts this application and broadcasts, and a copy launched by that
        // same task must not raise a window the task meant to come up quietly.
        // Those two arrive within moments of each other. A person
        // double-clicking the executable is minutes or hours away, and is
        // asking for the window on purpose.
        private const int QuietStartMs = 5000;

        private readonly GuiTray Context;

        public GuiFilter(GuiTray context) {
            this.Context = context;
        }

        // Whether a request to show the window should be honoured.
        //
        // It used to be "the last message was not an automatic start", with no
        // bound on how long ago that was — and the last message is remembered
        // for the life of the process. So on any machine with Start with
        // Windows turned on, the startup broadcast set that state and nothing
        // ever cleared it: double-clicking StarMon.exe to bring the window up
        // did nothing at all. The second attempt worked, because the first
        // one's own message had by then replaced the state. "Click it twice"
        // is the kind of thing people work around rather than report.
        //
        // Pure, and takes its clock, so both halves can be checked without
        // waiting for one.
        internal static bool ShouldRaiseWindow(Gui.MessageParam previous,
            int previousAt, int now, int quietMs) {

            if(previous != Gui.MessageParam.Gui && previous != Gui.MessageParam.Key)
                return true;

            return unchecked(now - previousAt) >= quietMs;

        }

        // Returns true when the message was ours
        public bool Handle(int message, IntPtr wParam, IntPtr lParam) {

            if(message != Gui.MessageId)
                return false;

            // The same broadcast arrives more than once — it goes to every
            // top-level window, and this process may own several. The sender
            // puts its process identifier in wParam so the repeats can be told
            // apart from a genuine second attempt.
            if(wParam == this.LastId)
                return true;

            switch((Gui.MessageParam) lParam) {

                case Gui.MessageParam.AnotherInstance:

                    // Unless this instance was itself started by a task a
                    // moment ago, in which case it is meant to come up quietly
                    if(ShouldRaiseWindow(this.LastParam, this.LastParamAt,
                        Environment.TickCount, QuietStartMs))
                        this.Context.BringFocus();

                    break;

                case Gui.MessageParam.Gui:
                    break;

                case Gui.MessageParam.Key:
                    this.Context.Op.KeyHandler(this.LastParam);
                    break;

            }

            this.LastId = wParam;
            this.LastParam = (Gui.MessageParam) lParam;
            this.LastParamAt = Environment.TickCount;

            return true;

        }

    }

}
