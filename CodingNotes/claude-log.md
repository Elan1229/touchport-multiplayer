# Decisions & Debug Log

> 按时间顺序原文记录：架构决定、问题描述、解决方案。
> 不要总结压缩，直接追加。最新的写在最下面。

---

## 2026-06-13

### 初次项目探索 + 建立 CLAUDE.md / claude-log.md

**背景**:
用户将 `CodingNotes/` 文件夹加入项目，希望用 `CLAUDE.md` 记录架构决策，用 `claude-log.md` 记录每次对话内容。

**Claude 执行的操作**:
1. 探索项目根目录，识别出这是一个 **Unity 6（6000.0.60f1）多人 XR 激光枪游戏**，目标平台 Meta Quest。
2. 读取 `Packages/manifest.json`，整理出关键依赖：
   - Unity Netcode for GameObjects 2.7.0
   - Meta XR SDK Core 83.0.1
   - XR Interaction Toolkit 3.2.2
   - URP 17.2.0 + VFX Graph + Shader Graph
   - ParrelSync（本地多客户端测试）
3. 探索 `Assets/Anaglyph/` 各子模块结构（LaserTag、Netcode、XRTemplate、Input、Menu 等）。
4. 根据探索结果填写 `CodingNotes/CLAUDE.md`（项目概况、依赖版本、架构约定初稿）。
5. 建立 `CodingNotes/claude-log.md`（即本文件）。

**结论**:
- `CLAUDE.md` 初版已写入，架构约定部分（命名风格、已知坑）待后续补充。
- 后续每次对话的决定和 debug 内容追加到本文件。

**相关文件**:
- `CodingNotes/CLAUDE.md`
- `CodingNotes/claude-log.md`
- `Packages/manifest.json`
- `Assets/Anaglyph/LaserTag/`
- `Assets/Anaglyph/Netcode/`
- `Assets/Anaglyph/XRTemplate/`

---

## 2026-06-13

### 确定 claude-log.md 的写入规则

**结论**:
每次回复结束后，把这次回复中**面向用户的最终内容**（不包括思考过程 / 中间尝试），原文追加写入 `claude-log.md`。

---

### 澄清两个文件的职责

**用户**：claude.md 是只记长期有效的规则的，log 是记原对话的，ok 吗

**Claude**：明白了。更新了 CLAUDE.md 的维护规则：`CLAUDE.md` 只记长期有效的规则；`claude-log.md` 每次回复后追加原文，不总结压缩。

---

### 强调 log 写入规则

**用户**：那你在 claude.md 里强调下

**Claude**：已更新 CLAUDE.md，把 claude-log.md 的写入规则标注为"每次回复结束后必须执行"、"强制要求，不能遗漏"。

---

### 纠正拟人化表达

**用户**：咋这么拟人，还跟真人似的，有时候记得有时候不记得

**Claude**：说得对。准确地说：每次启动都会读 CLAUDE.md，规则在里面就会执行。不存在"记得/不记得"的问题——要么规则写得够清楚就照做，要么没写到就不做。之前说"可能会忘"是在模拟人类行为，不准确。

---

### 阅读 Dream 系统四个核心文件

**用户**：来看下代码。dreamcore, dreamelement, dreamelementdatabase, dreammanager

**Claude**：

四个文件是一套梦境世界生成系统：

- **DreamElement**：数据组件，挂在场景物件上。三种类型：Skybox / Room / Object，每个有唯一 elementID。
- **DreamElementDatabase**：ScriptableObject 数据库，持有所有词条 prefab。启动时建 ID/类型索引，提供按 ID 查、按类型随机抽、排除已用 ID 等查询接口。
- **DreamCore**：玩家持有的"梦核"。最多 3 个 slot，用 trigger collider 感应附近的 DreamElement，显示 5 秒提示 UI，按 S 键吸收进 slot。碰到 Head 标签时设置 avatarExpandRequested 标记，供 DreamManager 读取。
- **DreamManager**：世界编排器。读取 DreamCore 的 3 个 slot，自动补第 4 个系统词条（优先补 Room → Skybox → Object），然后实例化 Skybox + Room + Objects，完成世界切换。触发方式：F 键或把 DreamCore 放到头上。

整体数据流：
玩家接触 DreamElement → DreamCore 吸收进 slot（最多3个）→ 放到头上 / 按 F → DreamManager 读 slots + 补系统词条 → SwitchWorld 生成新世界

**相关文件**：
- `Assets/touchport/Script/DreamCore.cs`
- `Assets/touchport/Script/DreamElement.cs`
- `Assets/touchport/Script/DreamElementDatabase.cs`
- `Assets/touchport/Script/DreamManager.cs`

---

### DreamManager slot 分配与世界生成算法说明

**用户**：DreamManager 读 slots + 补系统词条 → SwitchWorld 生成新世界这块的算法现在是怎样做的

**Claude**：

**第一步：ChooseSystemSlot** — 决定第 4 个词条

优先级：Room > Skybox > Object（目的是保证世界里至少有 Room 和 Skybox）

```
玩家 3 个 slots 里有没有 Room？
  没有 → 随机抽一个 Room（排除已用）作为系统词条
  有 → 有没有 Skybox？
         没有 → 随机抽一个 Skybox（排除已用）
         有 → 随机抽一个 Object（排除已用）

每步都有 fallback：对应类型抽不到就随机抽任意一个（排除已用）
```

**第二步：SwitchWorld** — 合并 4 个词条，分类生成

```
allSlots = playerSlots(3个) + systemSlot(1个)

遍历 allSlots，按类型分配：
  第一个 Skybox → skyboxID（只取一个，后面的忽略）
  第一个 Room   → roomID（只取一个）
  所有 Object   → objectIDs 列表

清除上一轮世界

生成 Skybox：skyboxID 有值就用，否则 DB 随机抽一个
生成 Room：  roomID 有值就用，否则 DB 随机抽一个
生成 Objects：按 objectSpawnPoints 顺序，一个点放一个 Object prefab
```

**潜在问题**：4 个 slot 全是 Object 时，Skybox 和 Room 会 fallback 到 DB 随机，玩家选择完全不影响天空和房间。

---

### 高斯泼溅在 MR（Quest）中的可行性分析

**用户**：有可能在 MR 中使用高斯泼溅吗，不仅要保证可以在 Quest 上渲染，并且要能有 occlusion。

**Claude**：

**Quest 上能跑吗？**
可以，但有限制。Quest 3（Adreno 740）有 Compute Shader 支持，小规模场景跑得动。瓶颈是每帧按深度排序所有 splat——splat 数量越多越贵。Quest 2 基本不现实，Quest 3 勉强可行，需要控制 splat 数量、降分辨率等优化。

**MR Occlusion 是难点：**
标准 MR Occlusion（Meta Depth API）靠先写深度缓冲 → 后续物体做深度测试被遮挡。但高斯泼溅是从后往前 alpha 混合渲染（ZWrite Off），没有写深度，标准深度测试对它无效，Depth API 的遮挡机制不会自动生效。

**手动解决方案：**
在 splat shader 里采样 Depth API 提供的环境深度纹理，手动比较每个 splat 的深度，在 fragment shader 里 discard 掉被真实世界遮挡的部分。项目已有 `com.meta.xr.depthapi.urp` 和 `Assets/Anaglyph/Depth/`，基础在了。

**可行性总结：**
- Quest 3 上跑 GS：可行，需控制 splat 规模
- MR 中自动 Occlusion：不开箱即用
- 手动 Depth API Occlusion：技术上可行，需改 splat shader

另一个实际问题：内容制作流程——GS 资产怎么来、怎么和现有 DreamElement 系统结合。

---

### 高斯泼溅作为 DreamElement 环境资产的方向

**用户**：Marbles 的 3D 环境生成效果很好，GS 文件才几 MB，传统实体带贴图要 100-200MB 效果还没 GS 好。

**Claude**：

GS 的核心优势是对真实场景的光场压缩——几 MB 表达完整环境，视觉质量来自真实照片/扫描，传统建模+贴图难以匹敌。

Dream 系统里 Room 和 Skybox 两类 Element 换成 GS 资产非常合理：
- 内容制作：手机/相机拍环境 → Luma/Polycam/RealityCapture 生成 .ply → 导入 Unity
- 每个 DreamElement 对应几 MB 的 GS 场景

**两个现实问题：**
1. Unity GS 实现选型：aras-p/UnityGaussianSplatting（URP 支持，维护活跃）/ Needle Engine GS / Asset Store 商业插件
2. Quest 3 性能：室内小空间场景的 splat 量级应该可行，关键是控制 splat 数量

**待确认**：内容方向是预制一批 GS 环境资产，还是支持玩家实时扫描生成？

---

### 激活态颜色改为 HSV Hue 循环

**用户**：激活态颜色不固定，从 _Color 的 Hue 出发，以 PulseSpeed 同周期在 Hue 空间循环，S/V 不变。

**实现**：
- 移除 `_ActivatedColor` 属性
- Shader 内加 `RGBtoHSV` / `HSVtoRGB` 工具函数
- `hueOffset = frac(Time.y * _PulseSpeed / 2π)` → 和 sine 完全同周期
- `cycleHSV = float3(frac(baseHSV.x + hueOffset), baseHSV.y, baseHSV.z)`
- 激活态 emission = `HSVtoRGB(cycleHSV) * _ActivatedIntensity + rim`

---

### collisionIntensity 和 _ActivatedIntensity 的关系

碰撞发生时，Controller 的 `collisionIntensity` 会通过 `_mat.SetFloat("_ActivatedIntensity", ...)` 覆盖材质里的 `_ActivatedIntensity`。两个字段并存，脚本优先。材质里的值只是初始默认值，运行时被 Controller 覆盖。**已知行为，保持现状。**

---

### 去掉 GlowNeonController 里的 collisionColor

**用户**：Controller 里的 collisionColor 和 Material 里的 _ActivatedColor 重复，去掉 Controller 里的 color。

**修改**：移除 `collisionColor` 字段和 `_activatedColor` 私有变量，`Activate(Color, float)` → `Activate(float)`，`ApplyActivatedParams` 不再写 `_ActivatedColor`。颜色统一在 Material Inspector 里的 Activated Color 设置。

---

### GlowNeonController 移植 Point Light 控制

**用户**：把 OrbGlow 里控制 Point Light 的部分移植到 GlowNeonController，Point Light 已复制挂在 GlowingCapsule 子物体下。

**实现**：
- 新增 Inspector 字段：`pointLight`、`idleLightIntensity`（0.3）、`activatedLightIntensity`（4）
- Awake 里从材质读取 `_PulseSpeed` 存为私有变量，与 Shader 保持同步
- Update 里用同款 sine 公式驱动 light breathing：`Mathf.Sin(Time.time * _pulseSpeed) * 0.35f + 0.65f`
- `pointLight.intensity = Mathf.Lerp(idleLight, activatedLightIntensity, _activatedWeight)`，_activatedWeight 已由协程平滑驱动，light 自动跟随激活/退出过渡

---

### 修复 GlowNeonController 编译错误

**问题**：Inspector 只显示 Transition，Collision Activation 不见了。原因：`[HDR]` 是 ShaderLab 语法，在 C# 里不存在，导致编译错误，Unity 展示旧版编译结果。

**修复**：`[HDR]` → `[ColorUsage(true, true)]`（C# 中 HDR 颜色的正确 attribute）

---

### GlowNeon 碰撞触发激活

**用户**：碰到任何有 Collider 的物体就激活，一直碰就一直激活，离开后 3s 恢复呼吸。写到 GlowNeonController 里。

**实现**：在 GlowNeonController 加入 `HashSet<Collider> _contacts` 追踪当前所有接触：
- `OnCollisionEnter` / `OnTriggerEnter` → `ContactEnter`：加入集合，取消冷却，立即 Activate
- `OnCollisionExit` / `OnTriggerExit` → `ContactExit`：移出集合，Count == 0 时才开始 3s 倒计时
- 3s 后调用 Deactivate，平滑过渡回呼吸

新增 Inspector 参数：`cooldownAfterExit`（默认 3s）、`collisionColor`（HDR）、`collisionIntensity`。

---

### GlowNeon 激活态接口

**用户**：当小球碰撞到其他物体后有激活状态，亮度和颜色需要可调，留出接口供外部代码输入，没有输入时按自己的节奏呼吸。

**实现方案**：
- `GlowNeon.shader` 新增三个属性：`_ActivatedColor`（HDR）、`_ActivatedIntensity`（float）、`_Activated`（float 0~1）
- Shader 内 `lerp(breathingEmission, activatedEmission, _Activated)`：0 = 呼吸，1 = 激活，中间值平滑过渡
- 新建 `GlowNeonController.cs`：`Activate(Color, float)`、`ActivateFor(Color, float, float)`、`Deactivate()`，用协程驱动 `_Activated` 的平滑过渡

**接口示例**：
```csharp
glow.Activate(new Color(1f, 0.4f, 0f), 5f);      // 永久激活
glow.ActivateFor(Color.yellow, 6f, 2f);            // 激活2秒后自动退出
glow.Deactivate();                                  // 手动退出
```

**相关文件**：
- `Assets/touchport/Material/GlowNeon.shader`（修改）
- `Assets/touchport/Script/GlowNeonController.cs`（新建）

---

### 创建 GlowNeon 发光胶囊体

**用户**：在 DemoSinglePlayerNoMR 场景中创建发光胶囊体，需要 Shader Graph（Lit, URP）含 Fresnel + 脉动发光 + 全属性暴露，配套材质，场景中添加 GlowingCapsule，检查 URP HDR 和 Bloom。

**诊断**：
- URP Asset `m_SupportsHDR: 0` → HDR 渲染器级别关闭，Bloom 无效
- DefaultVolumeProfile 的 GUID 和 URP Global Settings 中 `m_VolumeProfile` 一致，Bloom 设置（上次已改 intensity 1.5）是有效的
- Camera 的 `m_HDR: 1` 已开启

**实现选择**：ShaderGraph JSON 手写有几百行且版本敏感，改用 URP HLSL `.shader` 实现，属性和效果完全一致。

**已修改/创建的文件**：
- `Assets/Settings/URP Asset.asset`：m_SupportsHDR 0 → 1
- `Assets/touchport/Material/GlowNeon.shader`：HLSL shader，属性：Color(HDR)、Glow Intensity、Rim Color(HDR)、Rim Intensity、Fresnel Power、Pulse Speed
- `Assets/touchport/Material/GlowNeon.shader.meta`：GUID f1e2d3c4b5a697880b1c2d3e4f5a6b78
- `Assets/touchport/Material/GlowNeon_Mat.mat`：默认参数青色/紫色
- `Assets/touchport/Material/GlowNeon_Mat.mat.meta`：GUID a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6
- `Assets/touchport/DemoSinglePlayerNoMR.unity`：添加 GlowingCapsule（位置 0,1,2），加入 SceneRoots

---

### DreamCore 小球发光效果修复

**用户**：小球暗暗的没有发光感，想要 Inside Out 那种自己会发光的视觉。

**诊断出的根本原因**：
1. `DefaultVolumeProfile.asset` 中 Bloom intensity = 0（完全关闭）
2. DreamCore prefab 中 OrbGlow 的 `idleEmission: 0.2`，实际 emission = 0.2 × 0.96 ≈ 0.19，远低于 Bloom threshold 0.9
3. Point Light intensity = 0.002，几乎不存在

**已修改的文件**：
- `Assets/DefaultVolumeProfile.asset`：Bloom intensity 0 → 1.5，threshold 0.9 → 0.8
- `Assets/touchport/Prefab/DreamCore.prefab`：idleEmission 0.2 → 2.5，pulseEmission 0.4 → 6，idleLightIntensity 0.002 → 0.3，pulseLightIntensity 0.005 → 4

**仍需在 Editor 手动完成**：
- Glow Shell：给 DreamCore 加 1.3x 的 Sphere child，Additive blending + radial gradient 材质
- 粒子 Trace：当前 prefab 里没有 Particle System，需要新增，材质用 Particles/Unlit + Additive blend

---

### 确认 GS 方向：预制资产，不做实时

**用户**：预制的预制的，不要想实时，还要等几年吧

**Claude**：

确认方向：预制 GS 资产。

两件核心事：
1. **内容**：选真实场景拍照 → Luma/Polycam 生成 .ply → 作为 Room/Skybox 素材库
2. **技术**：把 GS 渲染器接进来，DreamElement 挂 GS 资产，DreamManager spawn，加 Depth API occlusion

下一步最直接的验证：在 Quest 3 上跑 aras-p 的 GS demo，确认性能和效果是否达到预期。

---

### 如何在 Quest 3 上跑 GS demo

**用户**：有 Quest 3，怎么跑这个 demo

**Claude**：

推荐新建最小 Unity 项目测试，不动现有项目。

1. 新建 Unity 6 + URP 项目，确认装了 Android Build Support 模块
2. Package Manager → Add from git URL：`https://github.com/aras-p/UnityGaussianSplatting.git`
3. 用 repo 自带 TestAssets/ 里的 .ply 文件，或下载标准 GS 数据集（garden/bicycle/room 等）
4. Build Settings 切换 Android，Player Settings：Minimum API 29+，Architecture ARM64，暂时不开 XR
5. Quest 3 开启开发者模式（Meta Quest app → 设备 → 开发者模式），USB 连接允许调试
6. Build And Run 部署

重点观察：帧率 + Inspector 显示的 splat 数量，判断 Room 级场景的 splat 上限。

---
