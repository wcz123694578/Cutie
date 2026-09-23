using CommunityToolkit.Mvvm.Messaging;
using Cutie.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Cutie.Views
{
    /// <summary>
    /// MainView.xaml 的交互逻辑
    /// </summary>
    public partial class MainView : UserControl
    {
        private bool _isMcpServerStarted = false;
        private static McpHttpServer _server;

        public MainView()
        {
            InitializeComponent();

            WeakReferenceMessenger.Default.Register<OpenSettingsViewMessage>(this, (r, m) =>
            {
                new SettingsView().ShowDialog();
            });
        }

        private void McpServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMcpServerStarted)
            {
                McpServerButton.Content = "开启MCP服务器";

                if (_server != null)
                {
                    _server.ToolCalled -= McpServer_ToolCalled;
                    _server.Stop();
                    _server = null;
                }
                _isMcpServerStarted = false;
            }
            else
            {
                McpServerButton.Content = "停止MCP服务器";

                if (_server == null)
                {
                    _server = new McpHttpServer(
                        "http://127.0.0.1:57231/");
                    _server.ToolCalled += McpServer_ToolCalled;

                    _server.Start();
                }
                _isMcpServerStarted = true;
            }
        }

        private void McpServer_ToolCalled(object sender, McpToolCalledEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => McpServer_ToolCalled(sender, e)));
                return;
            }
            (DataContext as MainViewModel)?.AddToolCall(e.Message);
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            var vegas = VegasContext.Current;
            VegasContext.InvokeAsync<object>(() =>
            {
                vegas.SaveSnapshot("D:\\Code\\csharp\\VEGAS\\Cutie\\preview-snapshot-manual.png", ScriptPortal.Vegas.ImageFileFormat.PNG, VegasToolSupport.Time(3000));
                return null;
            });
        }
    }
}
