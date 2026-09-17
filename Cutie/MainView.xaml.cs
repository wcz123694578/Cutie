using ScriptPortal.Vegas;
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
using System.Windows.Shapes;

namespace Cutie
{
    /// <summary>
    /// MainView.xaml 的交互逻辑
    /// </summary>
    public partial class MainView : Window
    {
        private bool _isMcpServerStarted = false;
        private static McpHttpServer _server;


        public MainView()
        {
            InitializeComponent();
        }

        private void McpServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isMcpServerStarted)
            {
                McpServerButton.Content = "开启MCP服务器";

                if (_server != null)
                {
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

                    _server.Start();
                }
                _isMcpServerStarted = true;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            this.Close();
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            await VegasContext.InvokeAsync<object>(() =>
            {
                var keyframeTools = new KeyframeTools();

                using (var ub = new UndoBlock("aaa"))
                {
                    VegasContext.Current.Project.AddVideoTrack();
                }
                return keyframeTools.ListTrackMotionKeyframes(1);
            });
        }
    }
}
