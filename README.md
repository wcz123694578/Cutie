# Vegas Cutie 插件
由于我图方便直接在脚本层面实现mcp，写完工具调用后去拉mcp aspnet的包才发现不支持.net standard，于是让gpt大人帮我写了一个httpserver。

## 使用方法
1. 把下载的文件夹解压到用户文件夹下的`Vegas Application Extensions`目录里，重启Vegas Pro。
2. 在Vegas Pro的`工具>扩展`菜单项中点击 `CutieCommand`，打开插件界面。
3. 在设置界面中设置好MCP Server的端口号，在主界面点击开启MCP按钮。

## 常见问题
1. AI调用截取预览画面有时候会使服务阻塞，通常AI发现后会告诉你，这时候需要重启VEGAS打开插件才能恢复正常。排查中。

批量操作使用 `execute_batch` / `start_batch`，默认整批一次撤销。接口、对象引用、执行边界与测试方法见 [批量操作说明](docs/batch-operations.md)。
