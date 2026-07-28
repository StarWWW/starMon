// StarMon: hardware monitoring and control
// Portions copyright © 2023-2024 Piotr Szczepański (GPL-3.0)

using System.Windows.Controls;

namespace StarMon.Ui.Views {

    // The application's own page: what it is, what it is running on, and
    // whose licence it is under. Shares SystemViewModel with the System
    // section because the machine facts are the same facts.
    public partial class AboutView : UserControl {

        public AboutView() {
            InitializeComponent();
        }

    }

}
