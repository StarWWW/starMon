// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StarMon.Ui.ViewModels {

    // The base every view model sits on.
    //
    // There is no MVVM framework here and there cannot be one: the build
    // cannot reach NuGet. That turns out to be about twenty lines of loss, so
    // this is them.
    //
    // Set() rather than a plain setter, because the interface is driven by a
    // poller that writes the same reading second after second. Raising a
    // change notification for a value that did not change makes WPF re-evaluate
    // bindings, re-run converters and invalidate layout for nothing, once a
    // second, forever. Comparing first is what keeps a window showing eleven
    // live readings from costing anything while it sits idle.
    public abstract class Observable : INotifyPropertyChanged {

        public event PropertyChangedEventHandler PropertyChanged;

        // Assigns a field and reports the change, if there was one
        protected bool Set<T>(ref T field, T value,
            [CallerMemberName] string property = null) {

            if(EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            Raise(property);
            return true;

        }

        // Reports a change in a property that is computed rather than stored
        protected void Raise([CallerMemberName] string property = null) {

            PropertyChangedEventHandler handler = this.PropertyChanged;
            if(handler != null)
                handler(this, new PropertyChangedEventArgs(property));

        }

    }

}
