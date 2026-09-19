using System;
using System.Collections;
using System.IO;
using System.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Cutie.CustomCommands;
using Cutie.Service;
using Microsoft.Extensions.DependencyInjection;
using ScriptPortal.Vegas;

namespace Cutie
{
    public class MainModule : ICustomCommandModule
    {
        public ICollection GetCustomCommands()
        {
            var cutieCommand = new CutieCommand(CommandCategory.Tools, "CutieCommand");
            return new CustomCommand[] { cutieCommand };
        }

        public void InitializeModule(Vegas vegas)
        {
            var context = SynchronizationContext.Current;
            VegasContext.Initialize(vegas, context);

            var sc = new ServiceCollection();

            string configPath = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "Vegas Pro", "cutieSettings.json");
            sc.AddSingleton<SettingsService>(new SettingsService(configPath));

            Ioc.Default.ConfigureServices(sc.BuildServiceProvider());
        }
    }
}
