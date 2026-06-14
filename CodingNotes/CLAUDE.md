# CLAUDE.md

> 这个文件会被 Claude Code 在每次启动时自动读取。
> 用来记录**当前有效**的架构规则、约定、已知坑。
> 历史讨论/debug记录原文放在 `CodingNotes/claude-log.md`，不放这里。

## 项目概况

- **项目名**: touchport-multiplayer（激光枪多人对战 XR 游戏）
- **Unity 版本**: 6000.0.60f1（Unity 6）
- **渲染管线**: URP 17.2.0
- **平台**: Meta Quest（Android XR），同时支持桌面 Editor 模式
- **主要模块**:
  - `Assets/Anaglyph/LaserTag/` — 核心游戏逻辑（武器、玩家、对局管理、空间定位）
  - `Assets/Anaglyph/Netcode/` — 网络层封装（基于 Unity Netcode for GameObjects）
  - `Assets/Anaglyph/XRTemplate/` — XR 基础设施（AprilTag 定位、Passthrough、SharedSpaces、设备摄像头）
  - `Assets/Anaglyph/Input/` — 输入系统（Unity Input System 1.14.2）
  - `Assets/Anaglyph/Menu/` — 菜单 / UI
  - `Assets/Anaglyph/VariableObjects/` — ScriptableObject 变量系统
  - `Assets/Anaglyph/StrikerSupport/` — Striker VR 控制器支持
  - `Assets/touchport/` — 游戏内容（场景、Prefab、材质）

## 关键依赖版本

| 包 | 版本 |
|---|---|
| Unity Netcode for GameObjects | 2.7.0 |
| Unity Transport | 2.6.0 |
| Unity Services Multiplayer | 1.1.8 |
| XR Interaction Toolkit | 3.2.2 |
| Meta XR SDK Core | 83.0.1 |
| Meta XR Depth API (URP) | git |
| VFX Graph | 17.2.0 |
| Shader Graph | 17.2.0 |
| ParrelSync | git（多客户端本地测试）|
| Striker SDK | 0.9.0 |

## 架构约定

### 网络层（Netcode）
- 使用 **Unity Netcode for GameObjects (NGO) 2.7.0**，不用 Mirror / Fishnet
- 网络逻辑封装在 `Anaglyph/Netcode/`，游戏逻辑不直接依赖 NGO API
- NetworkObject 池化：`NetworkObjectPool.cs`

### 空间定位 / 共享空间
- 使用 **AprilTag** 做多人空间对齐（`XRTemplate/AprilTags/`）
- 共享空间管理：`LaserTag/ColocationManager.cs`
- Editor 调试用 `EditorSimulatedAprilTags.cs` 模拟

### 玩家 / Rig
- XR Rig Prefab：`LaserTag/XR Rig.prefab`，桌面调试 Rig：`LaserTag/DesktopRig.prefab`
- Rig 动态生成：`RigSpawner.cs`

### 场景结构
- 主场景：`Assets/Anaglyph/LaserTag/MainScene.unity`
- touchport 内容场景：`Demo`, `DemoMultiplayerNoMR`, `DemoSinglePlayerNoMR`, `MultiTest`

### VFX / 渲染
- VFX Graph（`Assets/VFX/`）
- URP + Meta Depth API（深度遮挡）
- Passthrough 由 `PassthroughManager.cs` 管理

### 命名 / 代码风格约定
-（待补充）

## 已知坑 (Known Issues)

>（待记录）

## 维护规则

- `CLAUDE.md`：只记**长期有效**的架构规则、约定、已知坑。过时的内容删掉或标注废弃。
- `claude-log.md`：**每次回复结束后必须执行**——把面向用户的最终内容原文追加进去（加日期标题）。不总结压缩，直接写原文。**这是强制要求，不能遗漏。**
