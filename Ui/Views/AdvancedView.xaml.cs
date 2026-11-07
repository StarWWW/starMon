// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Windows.Controls;

namespace StarMon.Ui.Views {

    // The firmware commands the command line could send and the window could
    // not. Everything it does goes through its view model and the controller.
    public partial class AdvancedView : UserControl {

        public AdvancedView() {
            InitializeComponent();
        }

    }

}
