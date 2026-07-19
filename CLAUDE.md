# touchport-multiplayer

## unityMCP 使用规则：禁止 execute_code / 仅使用"菜单等价"操作

核心原则一句话：**只做 Unity Editor 菜单/Inspector 里本来就有对应按钮的操作；任意 C# 代码执行一律禁止。** 判断标准就一个问题——"这个操作在 Unity 菜单/Inspector 里有没有对应的手动按钮？"有 → 可以用对应工具做；没有 → 不用工具，改为自己想办法或让用户手动操作。

**允许使用**（等价于人在 Editor 里点鼠标，走 Unity 自己的 Undo 和序列化系统，出错可 Ctrl+Z）：

- 结构化编辑类：`manage_gameobject`、`manage_components`、`manage_material`、`manage_prefabs`、`manage_scene`、`manage_asset`、`manage_animation`、`manage_camera`、`manage_physics`、`manage_vfx`、`manage_ui`
- `execute_menu_item` —— 字面意思就是"点一个菜单项"，完全等价于人点
- 只读类：`read_console`、`find_gameobjects`、`unity_reflect`、`get_sha`、`manage_profiler`（读）

**永远禁止**：

- `execute_code` —— 唯一一个不对应任何 Editor 手动操作的工具，纯代码注入，没有"撤销"的概念，出问题无法回退
- 任何工具的任何参数如果是"传一段代码/表达式进去执行"，该用法同样禁止，哪怕该工具本身平时是安全的

这条线不需要按"诊断/修复阶段"来切换判断，任何时候都适用。

## Code style

- Runtime-visible strings — `Debug.Log`/`LogWarning`/`LogError` output, Console messages, UI labels/text — must be written in **English** by default, unless explicitly told otherwise for that specific case.
- Code **comments** have no such restriction — write in whichever language is natural or already established in the surrounding file (this codebase's comments are predominantly Chinese, and that's fine).
