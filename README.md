由于我图方便直接在脚本层面实现mcp，写完工具调用后去拉mcp aspnet的包才发现不支持.net standard，于是让gpt大人帮我写了一个httpserver。目前端口号是固定的，请在启动host的代码里看，之后我会写个配置的功能

批量操作使用 `execute_batch` / `start_batch`，默认整批一次撤销。接口、对象引用、执行边界与测试方法见 [批量操作说明](docs/batch-operations.md)。
