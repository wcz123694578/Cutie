# VEGAS 调试启动器

1. 在 Visual Studio 中打开 `Cutie.slnx`，安装“.NET 桌面开发”工作负载及 .NET Framework 4.8.1 开发工具。
2. 在解决方案资源管理器中右键 `Cutie.Launcher`，选择“设为启动项目”。在“工具 → 选项 → 项目和解决方案 → 生成并运行”中，保持“运行时仅生成启动项目和依赖项”开启。启动器没有 Cutie 项目依赖，首次配置不需要 VEGAS 引用已就绪。
3. 按 F5，在窗口中选择“调试版本”和“编译 SDK”，点击“生成并启动 VEGAS”。扫描不到时浏览选择 `vegas*.exe` 或 SDK 目录。
4. 等窗口显示“调试器已附加”，在 VEGAS 的“工具 → 扩展”中打开 `CutieCommand`，即可命中插件断点。

每次 F5 都显示版本选择窗口，并自动填入上次配置。直接在“调试版本”列表中切换，然后点击“生成并启动 VEGAS”；打开窗口不会自行启动或附加。切换前先停止之前的调试并退出占用插件 DLL 的 VEGAS。

## 配置和生成

- `Vegas.local.props` 仅保存本机 SDK 目录与启动程序路径，已加入 Git 忽略。SDK 默认使用扫描到的最旧版本，也可选择其他安装版本或单独的 SDK 目录；调试版本可独立选择。
- Cutie 和 Cutie.Shared 通过公共 MSBuild 属性 `VegasSdkDirectory` 引用同一 SDK；不会把 VEGAS 自带的 DLL 复制或嵌入插件。命令行生成可指定 `/p:VegasSdkDirectory="实际 SDK 目录"`。
- 启动器通过父进程链及 COM 运行对象表匹配启动它的 VS，同时核对解决方案位置。不会随便连接另一个 VS 实例。
- F5 调试启动器期间无法使用 VS 解决方案生成接口，因此启动器使用**所属 VS 自带的 MSBuild**生成 Cutie，沿用 Cutie 当前配置与平台，保存已打开文件后执行还原和生成。输出显示在启动器窗口。
- 构建目标返回实际 `TargetPath`，启动器据此传入 `-CMDMODULE`，不写死 Debug、框架输出路径或安装位置。随后仅按新启动的进程 ID 附加 .NET Framework 调试器。多个进程调试时 VS 会同时显示启动器和 VEGAS。
- 启动器无需引用 VEGAS、Cutie、EnvDTE 或增加 VS 扩展。使用安装的 VS 的 COM 自动化接口；VS 与启动器须具有相同权限。

## 失败处理和限制

- 取消向导可直接关闭窗口，不生成插件、不启动 VEGAS。
- 路径失效时回到配置界面。生成失败不会启动 VEGAS；DLL 被占用时需手动停止对应调试并退出占用进程。
- 自动附加失败会显示 VEGAS 的 PID 并保留进程，便于手动附加；再次启动前请自行退出该进程。启动器不自动关闭已有 VEGAS。
- 关闭启动器窗口不会主动关闭 VEGAS；在 VS 停止调试时按 VS 的进程管理规则处理。
- 自动附加前执行的初始化代码不保证命中断点。使用新版 SDK 编译也不代表保证兼容旧 VEGAS，仍需在目标版本测试。
- 首次配置会新增本机 MSBuild 导入文件，已加载的编辑器项目可能提示重新加载；接受重新加载即可。启动器的实际生成通过新 MSBuild 进程读取最新配置。

## 窗体编辑与高 DPI

窗体采用标准 WinForms 分部类结构：`LauncherForm.cs` 保存行为，`LauncherForm.Designer.cs` 保存 `InitializeComponent` 和控件声明，`LauncherForm.resx` 保存资源。可直接在 VS 中打开窗体设计器拖放编辑。`App.config` 启用 PerMonitorV2，清单声明 Windows 10/11 兼容，窗体使用 96 DPI 基线及 DPI 自动缩放。
