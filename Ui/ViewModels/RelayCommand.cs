// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System;
using System.Windows.Input;

namespace StarMon.Ui.ViewModels {

    // A command backed by a delegate.
    //
    // The twenty lines an MVVM framework would have supplied. Buttons bind to
    // these rather than raising Click into code-behind, which is what lets the
    // tray menu and the window drive the same actions without either of them
    // knowing about the other — the alternative being two code paths that
    // gradually stop agreeing.
    public sealed class RelayCommand : ICommand {

        private readonly Action<object> Execute;
        private readonly Func<object, bool> CanRun;

        public RelayCommand(Action run, Func<bool> canRun = null) {
            this.Execute = parameter => run();
            this.CanRun = canRun == null ? (Func<object, bool>) null
                : parameter => canRun();
        }

        public RelayCommand(Action<object> run, Func<object, bool> canRun = null) {
            this.Execute = run;
            this.CanRun = canRun;
        }

        public bool CanExecute(object parameter) {
            return this.CanRun == null || this.CanRun(parameter);
        }

        void ICommand.Execute(object parameter) {
            this.Execute(parameter);
        }

        // WPF re-asks every command whether it can run whenever the input
        // focus moves or the user presses a key. Routing the event through
        // CommandManager rather than keeping a list of handlers means a
        // command whose availability changed for a reason nothing announced
        // still catches up, at the cost of asking rather more often than
        // strictly needed — which for a handful of buttons is nothing.
        public event EventHandler CanExecuteChanged {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

    }

}
