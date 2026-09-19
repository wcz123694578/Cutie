using System;
using System.Threading;
using System.Windows.Threading;
using ScriptPortal.Vegas;

namespace Cutie
{
    public class EntryPoint
    {
        [Obsolete]
        public void FromVegas(Vegas vegas)
        {
            var context = SynchronizationContext.Current;
            MainView mainView = new MainView();
            context = context
                ?? new DispatcherSynchronizationContext(mainView.Dispatcher);
            VegasContext.Initialize(vegas, context);
            mainView.Show();
        }
    }
}
