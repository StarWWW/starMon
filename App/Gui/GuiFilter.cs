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

        private readonly GuiTray Context;

        public GuiFilter(GuiTray context) {
            this.Context = context;
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

                    // Unless this instance was itself started by a task, in
                    // which case it is meant to come up quietly
                    if(this.LastParam != Gui.MessageParam.Gui
                        && this.LastParam != Gui.MessageParam.Key)
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

            return true;

        }

    }

}
