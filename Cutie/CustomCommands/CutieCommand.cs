using System;
using System.Threading;
using System.Windows.Forms.Integration;
using System.Windows.Threading;
using Cutie.Resources;
using ScriptPortal.MediaSoftware.Skins;
using ScriptPortal.Vegas;

namespace Cutie.CustomCommands
{
    public class CutieCommand : CustomCommand
    {
        public CutieCommand(CommandCategory category, string name) : base(category, name)
        {
            InitializeCommand();
        }

        private void InitializeCommand()
        {
            this.Invoked += CutieCommand_Invoked;
        }

        private void CutieCommand_Invoked(object sender, EventArgs e)
        {
            if (VegasContext.Current.ActivateDockView("CutieView"))
            {
                return;
            }

            var dock = new DockableControl("CutieView");

            dock.Closed += CutieView_AppWindowClosed;

            var elementHost = new ElementHost();
            dock.Controls.Add(elementHost);

            elementHost.Dock = System.Windows.Forms.DockStyle.Fill;


            var mainView = new Views.MainView();
            VegasSkinsManager.Apply(mainView.Resources, ScriptPortal.MediaSoftware.Skins.Skins.Colors);
            elementHost.Child = mainView;

            VegasContext.Initialize(VegasContext.Current, SynchronizationContext.Current ?? new DispatcherSynchronizationContext(mainView.Dispatcher));
            VegasContext.Current.LoadDockView(dock);
        }

        private void CutieView_AppWindowClosed(object sender, EventArgs e)
        {
            int a = 1;
        }
    }
}
