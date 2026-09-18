# 批量操作

默认使用 **一个批次一个撤销记录**（`undoMode: "single"`），遇到错误停止（`onError: "stop"`）。单次工具接口继续可用。

## 接口

| 工具 | 用途 |
| --- | --- |
| `execute_batch` | 等待批次结束，返回逐步骤结果 |
| `start_batch` | 立即返回 `batchId`，适合长批次 |
| `get_batch_status` | 查询排队、运行、完成、失败或取消状态，以及逐步骤结果 |
| `cancel_batch` | 请求在操作之间停止；正在运行的 COM 调用不会被强制中断 |
| `list_batch_capabilities` | 查询所有工具的执行类别与两种模式支持情况 |

`execute_batch` 和 `start_batch` 使用相同参数：

| 参数 | 默认值 / 含义 |
| --- | --- |
| `operations` | 必填，1–1000 个 `{id, tool, arguments, capture?}` |
| `label` | `Cutie batch`，撤销记录名称，1–128 字符 |
| `undoMode` | `single`；显式选择 `staged` 可混合工程、传输与输出操作 |
| `onError` | `stop`；`continue` 继续独立步骤，依赖失败步骤的操作标为 `skipped` |
| `batchId` | 可选，调用方提供的唯一 ID；省略则生成。标识符仅允许 1–64 个字母、数字、下划线或连字符 |
| `completionTimeoutSeconds` | 120，范围 1–3600，仅用于分阶段打开工程或渲染的完成等待 |

运行结果状态为 `queued`、`running`、`completed`、`failed` 或 `cancelled`。逐步骤状态还包括 `succeeded`、`waiting`、`skipped` 和 `not_executed`。`waiting` 表示已启动外部操作，仍在等待完成。

结果保存在进程内，按数量限制清理已完成记录；重启不保留。尚在保留范围的重复 `batchId` 会被拒绝，客户端超时后应查询原 ID，避免重复编辑。`start_batch` 可避免长请求超时。

## 示例：创建轨道、生成媒体并添加效果

以下为 `execute_batch` 的 `arguments`：

```json
{
  "label": "生成动画镜头",
  "operations": [
    {
      "id": "track",
      "tool": "create_track",
      "arguments": { "type": "video", "name": "主画面", "index": 0 },
      "capture": [
        {
          "name": "main",
          "kind": "track",
          "selector": { "trackIndex": { "$ref": "track#/index" } }
        }
      ]
    },
    {
      "id": "clip",
      "tool": "create_generated_event",
      "arguments": {
        "trackIndex": { "$handle": "main", "property": "trackIndex" },
        "pluginId": "{Svfx:com.vegascreativesoftware:solidcolor}",
        "startMs": 0,
        "lengthMs": 3000
      },
      "capture": [
        {
          "name": "clip",
          "kind": "event",
          "selector": {
            "trackIndex": { "$handle": "main", "property": "trackIndex" },
            "eventIndex": { "$ref": "clip#/eventInfo/eventIndex" }
          }
        }
      ]
    },
    {
      "id": "effect",
      "tool": "add_effect",
      "arguments": {
        "targetType": "event",
        "trackIndex": { "$handle": "clip", "property": "trackIndex" },
        "eventIndex": { "$handle": "clip", "property": "eventIndex" },
        "pluginId": "{Svfx:com.vegascreativesoftware:pictureinpicture}"
      }
    }
  ]
}
```

## 结果引用与对象引用

### 由外部传入关键帧属性

批量层不生成音乐规则或动画参数。调用方传入每一步的参数；同一个平移/裁切关键帧可同时接收位移、缩放和插值。例如：

```json
{
  "label": "外部音符编排",
  "operations": [
    {
      "id": "note1",
      "tool": "set_pan_crop_keyframe",
      "arguments": {
        "trackIndex": 5,
        "eventIndex": 0,
        "atMs": 333.667,
        "interpolation": "Hold",
        "moveX": -40,
        "scaleX": -1,
        "scaleY": 1
      }
    }
  ]
}
```

`scaleX` / `scaleY` 默认是 1，直接调用原生 `VideoMotionKeyframe.ScaleBy`。它们与既有 `moveX` / `moveY` 一样是相对操作；再次传入 `scaleX: -1` 会再次翻转。需设置绝对朝向时，外部编排先读回 bounds，仅在当前朝向与期望不符时翻转。无需单独的缩放工具或额外 PanCrop 工具类。

- `{"$ref":"step#/field"}` 读取已经成功完成步骤的结果快照。路径采用 JSON Pointer，数组用 `/0` 等下标，`~0` 和 `~1` 分别表示 `~` 和 `/`。`step#` 表示整个结果。
- `{"$handle":"name","property":"trackIndex"}` 在执行该步骤时重新定位对象，适用于插入、删除或排序引起索引变化的场景。
- `capture` 在步骤成功后捕获对象。其 selector 可以引用本步骤的结果和此前步骤的 handle；同一步中的 capture 相互独立。
- 普通数字索引和 `$ref` 中的数字都是快照，**不会**自动跟踪对象。需要跟踪时使用 handle。

| capture.kind | selector | 可读取属性 |
| --- | --- | --- |
| `track` | `trackIndex` | `trackIndex` |
| `event` | `trackIndex, eventIndex` | `trackIndex, eventIndex` |
| `effect` | `targetType, trackIndex, effectIndex`；事件效果另需 `eventIndex` | `targetType, trackIndex, eventIndex`（事件效果）, `effectIndex` |
| `marker` / `region` | `index` | `index` |

对象被删除、所属工程被替换时引用失败，不会改为操作占用旧索引的新对象。事件被移至其他轨道会使原 handle 失效。Handle 只在当前批次有效，不跨请求保存。媒体使用现有 `mediaId`；take 和关键帧的索引仍为结果快照。

## 撤销、失败与分阶段执行

`single` 在一个 VEGAS 同步上下文回调内执行所有步骤，编辑工具复用一个外层 UndoBlock。TrackMotion 读取仍在 UndoBlock 保护下取得对象，独立调用时保留原有作用域。

**失败与取消不自动回滚。** 已完成的修改、以及失败操作抛错前可能做出的修改，保留在该批次的撤销记录中。`rollbackPerformed` 明确返回 `false`。返回的成功步骤是执行时的快照，后续步骤仍可能修改相同对象。捕获对象失败也算该步骤失败，即使工具本身已经完成编辑。

以下操作不能由工程 UndoBlock 恢复：保存、打开/新建工程、渲染、光标/选择/循环设置和播放控制。默认模式在**执行任何步骤之前**拒绝含有这些操作的批次。

选择 `staged` 后，连续的编辑/读取操作共享一个撤销块，外部操作在块外执行；后台工具也作为阶段边界。打开工程等待对应 `ProjectOpened` 事件及目标路径，渲染等待完成状态。完成等待失败会停止批次，即使设置 `onError: continue`，避免在未知工程状态下继续编辑。

取消等待不会关闭已启动的渲染，也不会撤销已经完成的文件写入。超时同样不代表外部操作已停止。

## 调度与限制

- 单次 VEGAS 调用和批次共用串行队列，批次之间不交错编辑。批次状态和取消接口绕过编辑队列。
- 最多 8 个排队/运行批次，最多 1000 步，输入 JSON 字符数限制为 4 MiB。
- 整批撤销模式不跨异步等待，也不在 UI 回调中并行访问 VEGAS；运行期间 UI 可能短时无响应。取消请求在服务线程接收，操作边界检查。
- `analyze_midi` 等纯后台工具单独调用时不占 VEGAS UI 线程；批次内为了支持对象捕获和引用仍在相应的 VEGAS 回调执行。
- 新工具必须显式声明 `ToolExecution`；缺失声明会使注册失败。批次控制工具不允许嵌套。
- 静态参数、引用关系和模式兼容性先检查；依赖实际结果或 VEGAS 状态的检查在执行时完成。预检不是对工程修改的模拟。

## 测试

离线测试没有额外测试框架依赖，构建并运行：

```powershell
$buildOutput = Join-Path $PWD 'mg_tests/batch_test_bin/'
dotnet build tests/Cutie.BatchTests/Cutie.BatchTests.csproj "-p:OutputPath=$buildOutput"
& (Join-Path $buildOutput 'Cutie.BatchTests.exe')
```

实机集成测试（先保存当前工程、加载最新 Cutie 并开启 MCP）：

```powershell
pwsh -File tests/integration/Test-BatchVegas.ps1
```

该脚本在独立工程中覆盖全部 60 个业务工具，通过批量接口写入并读回三类关键帧，验证工程保存/打开和 WAV 渲染；保存报告后恢复原工程。HTTP 调用使用 `-NoProxy` 直连本机。

撤销及控制接口验证分阶段执行：

```powershell
pwsh -File tests/integration/Test-BatchControl.ps1 -Phase Prepare
# 在 VEGAS 中按一次 Ctrl+Z
pwsh -File tests/integration/Test-BatchControl.ps1 -Phase VerifyUndo
# 在 VEGAS 中按一次 Ctrl+Y（重做）
pwsh -File tests/integration/Test-BatchControl.ps1 -Phase VerifyRedo
pwsh -File tests/integration/Test-BatchControl.ps1 -Phase Restore
```

`Prepare` 会验证异步取消，再留下单次撤销用的测试工程。可显式添加 `-IncludeFailureInjection` 验证运行时失败后继续；该选项故意访问不存在的轨道索引 `999`。如果 Visual Studio 设置为在异常引发时中断，需按 F5 让批次的异常处理继续执行，不应删除正确的索引检查。离线测试默认已覆盖此类失败路径。

`PrepareUndo` 用于调试暂停后直接准备撤销验证，不重复无效索引测试。测试代码在 `tests/` 跟踪；生成的媒体、工程和报告统一写入被忽略的 `mg_tests/`。
