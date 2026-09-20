using Cutie.Tools;
using ScriptPortal.Vegas;
using System;
using System.Windows;

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
