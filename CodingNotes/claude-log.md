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

---

## Session 3（续上次 TriggerForwarder 中断）

### TriggerForwarder 删除

**背景**：上次对话中断时，TriggerForwarder.cs 已创建，但 GlowNeonController 缺少 ForwardTriggerEnter/ForwardTriggerExit 方法。

**用户**：加了 Rigidbody（Kinematic）到 GlowingCapsule，不要 TriggerForwarder 了。

**操作**：
- 删除 TriggerForwarder.cs 及 .meta
- 删除 GlowNeonController 里的 ForwardTriggerEnter/ForwardTriggerExit
- 原因：有 Rigidbody 后，子物体 trigger 事件自动传到父节点，OrbGlow 也是同理

---

### NeonGlow_Pulse 新材质（ShaderGraph）

**用户**：换了一个新的 ShaderGraph 材质，效果好，但 Game 视图没有光晕。

**问题**：Camera 未勾选 Post Processing。
**修复**：Hierarchy → Camera → Inspector → 勾 Post Processing。

**ShaderGraph 节点链路**：Time → Multiply(PulseSpeed) → Sine → Remap → Emission

---

### 呼吸灯优化

**用户**：呼吸太急促。
**修复**：PulseSpeed 从 2.0 降到 0.5 左右。

**用户**：想要 x^8 的波形（高峰短、暗处长），但太多时间全黑。
**修复**：
- Sine → Remap 后加 Power 节点（A=Remap输出，B=8）
- Power 后加 Lerp 节点（A=0.15打底，B=1，T=Power输出）→ Emission
- 效果：[0.15, 1] 范围呼吸，永不全黑

---

### GlowNeonController 大幅简化

**用户**：去掉 Point Light、Transition、_Activated 整套，碰撞只改 PulseSpeed 和 GlowStrength。

**新逻辑**：
- 碰撞进入 → `_PulseSpeed = 3, _GlowStrength = 3`
- 全部离开后 5s cooldown → 渐变回 `_PulseSpeed = 1, _GlowStrength = 1`
- 子物体 Trigger + 父物体 Rigidbody，OnTriggerEnter/Exit 自动接收

**注意**：ShaderGraph property 的 Reference 名默认加下划线前缀，`PulseSpeed` → `_PulseSpeed`。

---

### 退出时闪烁 bug 修复

**问题**：deactivate 时把 PulseSpeed 也做了 Lerp，导致 sine 波形频率变化、相位跳变，出现狂闪。

**修复**：
- PulseSpeed 直接 snap 回 idlePulseSpeed
- 只对 GlowStrength 做 Lerp 渐变（deactivateDuration 默认 1.5s）

---

## 2026-07-07

### Fake Bloom 方案（替代 Post-processing）

**背景**：Post-processing（HDR Bloom）与 Quest Passthrough 不兼容，改用 VFX Graph 内 Fake Bloom。

**核心原理**：Additive 混合模式下粒子颜色叠加，密集处自然变亮，模拟 bloom 效果，不需要 HDR。

**贴图**：三张软光晕贴图生成到 `_assets/`：
- `glow_halo_only.png` — 纯软晕，无硬核，用于叠加光晕层
- `glow_psf_core_halo.png` — 亮核 + 光晕，用于主光点粒子
- `glow_psf_tight.png` — 更紧实的核

---

### HandshakeFBloom

- 已有 Output Particle Unlit Quad（Additive）只是缺贴图，补上 `glow_psf_core_halo` 即可
- 删除原有的随机强烈闪光节点（Random Float → Compare < 3 → Random Int 5~300 → Multiply Color，会把 3% 粒子颜色乘以最高 300 倍爆白）
- **Twinkle 效果**：`TotalTime(Game) × 2.5` + `Random Float(Per Particle, 0~6.28)` → `Frac` → `Sample Curve(sin 波)` → `Multiply Alpha` + `Multiply Size`
- Orient 改为 Face Camera Position；删掉 Set Angle Z: 90

---

### Portal VFX occlusion FBloom

- 两个 Output Particle Shader Graph 共用同一套 Initialize → Update，都走 VFXParticleOccluded（保留 occlusion）
- 主 Output：Default-Particle 贴图，Orient Along Velocity
- 光晕 Output：`glow_psf_core_halo`，Orient Face Camera Plane
- **筛掉 90% 粒子**（只在光晕 Output 里）：`Random Float(Per Particle, 0~1)` → `Compare < 0.1` → `Branch(True=原alpha, False=0)` → Output alpha
- Shader Graph block 后面的 block 有效（可加 Multiply Color、Multiply Alpha 等）
- Multiply Color 加轻微亮度变化


---

## 2026-11-11

### 待办 7 条的分诊与估时

**用户**：1 speculative view；2 礼物改变世界的时间留久一点 + 礼物加极省的发光；3 世界之间跳不过去；4 礼物不随世界消失；5 手部识别不利落、用过 controller 后手消失；6 手部 vfx 不出现；7 帧率低。

**Claude（估时）**：2 约 1–1.5h；3 约 2–3h（做模拟器调参）；4 约 0.5 天（已有 [GiftDiag] 日志，复现一次就能定位）；6 约 0.5 天（疑 VFX bounds 裁剪 / 层 / handshakeDistance，先看 [HsDiag]）；5 约 0.5–1 天（OVRManager 手+手柄设置；pinch 无迟滞、30Hz、用手腕根位置抓取）；7 约 1 天（先看 [PerfDiag]；已知 `IObjectXR.ApplyVisibility` 每帧 `GetComponentsInChildren<Renderer>()`）；speculative view（第三人观众）约 1.5–2 天，"两人"假设散在 PlayerWorld / HandshakeEffectInputNetwork / IObjectXR 可见性 / 礼物 spawn 等 ack 等处。

### 换梦模拟器 Unity 复刻版

新文件：`_DreamTouchForAI/Worlds/simulator-unity.html`（旧 simulator.html 未动）。逐行照搬 PlayerDreamBag（含 bumper、take back）、DreamNetworkManager（T0 结算、changeDelay 缓冲锁、commit 时按 owner 清场 + 收 portal）、GiftDeliveryTrigger（WorldShowing 按 bag.Current）。数据取自工程 .asset 和房间 prefab 的实际礼物实例数；带批量随机模拟。

**结论**：push=4 / lead=2 下纯 bag 逻辑 98% 一件礼物就跳——"跳不过去"不是权重问题，是时序：NoMR 随机玩家（6s 一件）37% 的送礼落在缓冲锁里被跳过，29% 因 stale currentDream（bag 已在 T0 换了，旧房间礼物的 currentDream 对不上任何一边，WorldShowing=-1）结算丢失，而 PortalDirectionTrigger 仍翻转 owner——这也可能是"礼物不随世界消失"的成因。

**数据问题**：6 Asian Alley 里 `Game` ×1、`Game Mario` ×2 的 gift 填的是 FlowerInterior，不是 GameCartridges（未改）。

### 关于重做底座

**Claude 建议**：新开 Unity 项目重做底座（N 人 + spectator 从第一天起、玩家与世界解耦、Shared Spatial Anchors + Colocation Discovery、Meta Interaction SDK 手部 + 抓取时转移网络所有权），限时约一周原型验证流畅度和帧率基线；PlayerDreamBag / Definition* / 房间 prefab / dissolve / ChangeDreamByGift / 模拟器搬过去。若近期有演示截止，截止前不动底座。

### 第 2 条：changeDelay 10s + 礼物闪光点

- `DemoMultiplayer_MR_ChangeDreams`：changeDelay 4 → 10；两个 ChangeDreams 场景的 `giftHeraldPrefab` 都指向新 prefab。
- 新增 `Prefab/GiftHerald.prefab`：一个 Quad + `Material/Particles/HeraldGlow.mat`（新 shader `DreamTouch/HeraldGlow`：Additive unlit、透明队列、支持 stereo instancing，贴 glow_psf_core_halo）+ `GiftHeraldBlink.cs`。零粒子。
- `GiftHeraldBlink`：定位到礼物包围盒顶上方、朝向相机、固定世界尺寸、尖峰式闪烁。闪烁用缩放不用 renderer.enabled——`IObjectXR.ApplyVisibility` 每帧覆盖子 Renderer 的 enabled。
- 销毁：fx 是礼物子物体，换梦 commit 时随礼物 despawn 一起没；兜底计时器改为 changeDelay + `heraldExtraLifetime`(2s)，因为 despawn 是摊帧的。
- Unity 当时没开，这些资产是直接写 YAML 的，打开工程后需确认编译和 prefab 显示正常。

## 2026-11-12 新房间 GLB 压缩（Gradle Java heap space）

- Build 失败：`:launcher:compressReleaseAssets` Java heap space。Addressables 产出单个 1.8GB bundle（Pack Together），主因 `Dreams/Models/202611` 10 个 GLB 各 ~145MB：约 60 万三角面、178 万顶点（逐面法线导致零共享）、内嵌 8192² PNG。
- glTFast 导入器无贴图 Max Size，定制材质（DreamsMaterial/202611）直接引用 GLB 内嵌贴图 → 选择原地覆盖 GLB（GUID、名字不变 → prefab 摆放与材质引用保留）。
- 处理（gltf-transform 4.1.1 + meshoptimizer，脚本在会话 scratchpad）：去逐面法线 → weld → simplify(ratio 0.25, error 0.005) → 面积加权平滑法线（仅 ShadowCaster bias 用）→ 贴图 2048 PNG → 恢复原 JSON 名字。
- 结果：1.4GB → 181MB；每个约 15–18 万面、17–27 万顶点、2K 贴图；10 个文件 names_same=True，.meta 未动。
- 原文件备份：项目根 `_glb_backup/202611/`（未加 .gitignore）。
- 使用情况：6 个被房间 prefab 引用；curtain / asianroom-game / asianroom-moon / booth-cloud 及其材质未被引用。
- 待 Unity 验证：重新导入后 prefab 无 Missing、材质贴图仍在、外观可接受；再 Clear Build Cache → build。

## 2026-11-14 梦境重整（3 儿童房 / 3 电话亭 / 3 亚洲房）——只做文件管理 + 权重，模型由 Elan 自己换

- 原地改名（GUID 不变 → 下标、场景注册、Addressables 引用都不变）：
  - `0 Bear Greenhouse` → `0 Cloud Phone Booth`，dream `0Greenhouse` → `0CloudPhoneBooth`（dreamId CloudPhoneBooth）
  - `1 Child Room` → `1 Child Room Cloud`，dream `1ChildRoomCloud`（权重加 Cloud 3）
  - `5 Cloud Room` 弃用 → 文件内容换成 1 的拷贝，改名 `5 Child Room Curtain` / `5ChildRoomCurtain`（沿用原 5 的 GUID）
  - `6 Asian Alley` → `6 Asian Shrine` / `6AsianShrine`
- 新建（拷贝 6，新 GUID）：`7 Asian Game Room` / `7AsianGameRoom`，`8 Asian Moon Bunny` / `8AsianMoonBunny`；两个 prefab 已加进 Default Local Group。
- allDreams 实际顺序（联网按下标）：0 AsianShrine, 1 ChildRoomCloud, 2 ChildRoomCurtain, 3 CloudPhoneBooth, 4 PhoneBooth, 5 Submarine, 6 SunsetBooth, **7 AsianGameRoom, 8 AsianMoonBunny**（追加在末尾）。MR / NoMR 各 World0、World1 共 4 处 + DemoSinglePlayer 1 处已加。
- 权重：CloudPhoneBooth{Booth10,Glass5,Cloud3,Sunlight2,Computer1}，ChildRoomCloud{Toy3,Cloud3,Computer2,Animal1,Balloon1}，ChildRoomCurtain{Toy3,Computer2,Animal2,Balloon1,Dark1}，AsianShrine{Halo3,Dark3,Flower2,Animal1}，AsianGameRoom{Computer3,Toy2,Flower2,Halo2,Dark1}，AsianMoonBunny{Halo3,Animal3,Dark2,Toy1}。原 Greenhouse 重复的 Glass 条目随之消失。
- 5、7、8 的礼物摆放是拷贝来的（5=1 的，7/8=6 的），需要在 Unity 里手摆；礼物 originDream 运行时由 DreamNetworkManager 重写，不用管。
- simulator-unity.html 数据同步为 9 个 dream；MR 预设 delay 改 10；fixGame 开关改作用于 AsianGameRoom。
- 单件礼物跳转表观察：push 4 / lead 2 下几乎每件礼物都能触发跳转；SunnyCloud 总是去 CloudPhoneBooth，ChildRoomCloud 很难被跳到；AsianGameRoom 只在平局时出现 → 待在 simulator 里调。
- 文件备份：会话 scratchpad `dreams/backup/`。

## 2026-11-14 调权重：让 ChildRoomCloud / AsianGameRoom 可达

- 方法：纯 bag 逻辑穷举（当前梦 × 伙伴梦 × 单件礼物，push 4 / lead 2），统计每个梦被跳到的比例。脚本在会话 scratchpad `dreams/tune.js`。
- 改动：CloudPhoneBooth {Booth10,Glass5,Cloud3,Sunlight2,Computer2}；ChildRoomCloud {Cloud4,Balloon3,Toy2,Sunlight2,Computer1,Animal1}；AsianGameRoom {Computer4,Toy3,Halo2,Flower1,Dark1}。
- 结果：被跳到比例从 2.3%–16.7% 收敛到 7.9%–14.1%（儿童房·云 2.3→8.7%，游戏房 3.5→14.1%，云端电话亭 8.8→10.0%）；不跳的组合约 6%。
- SunnyCloud：从儿童房送 → 儿童房·云；从电话亭送 → 云端电话亭（电话亭种子带 Booth/Glass）。TV/电话听筒/游戏卡带从非电话亭送 → 游戏房。
- simulator-unity.html 已同步。礼物摆放改完后要重新统计 ROOM_GIFTS_RAW 再验证。

## 2026-11-14 编号 = 下标：统一命名 + 重排 allDreams

| 下标 | prefab | dream 资产 / dreamId | 原名 |
|---|---|---|---|
| 0 | 0 ChildRoom-Cloud | 0ChildRoomCloud | 1 Child Room Cloud |
| 1 | 1 ChildRoom-Curtain | 1ChildRoomCurtain | 5 Child Room Curtain |
| 2 | 2 ChildRoom-Submarine | 2ChildRoomSubmarine | 2 Submarine Child Room |
| 3 | 3 Booth-Greenhouse | 3BoothGreenhouse | 3 Phone Booth |
| 4 | 4 Booth-Sunset | 4BoothSunset | 4 Phone Booth at Sunset |
| 5 | 5 Booth-Cloud | 5BoothCloud | 0 Cloud Phone Booth |
| 6 | 6 Asian-Shrine | 6AsianShrine | 6 Asian Shrine |
| 7 | 7 Asian-GameRoom | 7AsianGameRoom | 7 Asian Game Room |
| 8 | 8 Asian-MoonBunny | 8AsianMoonBunny | 8 Asian Moon Bunny |

- GUID 全部不变；prefab 根名、m_Name、dreamId、Addressables 地址同步。
- allDreams 在 MR(2) / NoMR(2) / DemoSinglePlayer(1) 共 5 份列表按 0..8 重排（脚本校验每份恰好这 9 个）。startDream 是引用不是下标，不受影响：MR W0=0ChildRoomCloud W1=4BoothSunset；NoMR W0=1ChildRoomCurtain W1=0ChildRoomCloud；SinglePlayer=0ChildRoomCloud。
- **联网下标变了 → 两台头显必须装同一个新 build。**
- simulator-unity.html 键名与顺序同步；跳转比例不变（7.9%–14.1%，不跳 6.4%）。
- 备份：会话 scratchpad `dreams/backup2/`。

## 2026-11-14 分组权重（012 儿童房 / 345 电话亭 / 678 亚洲）

- 设计：每组共享一个“底”（组内相似），再各加 1–2 个特色 keyword（组内区分）。
  - 儿童房：底 Toy3 + Balloon2~3；Cloud {Cloud4,Sunlight2,Balloon3}，Curtain {Animal3,Computer2}，Submarine {Dark3,Computer2}
  - 电话亭：底 Booth4 + Glass3 + Computer3；Greenhouse {Flower3,Sunlight2,Animal1}，Sunset {Dark4,Cloud3,Halo1}，Cloud {Cloud3,Sunlight3,Balloon1}
  - 亚洲：底 Halo2 + Flower；Shrine {Flower3,Dark3,Animal1}，GameRoom {Computer4,Toy2,Flower1}，MoonBunny {Animal3,Sunlight2,Flower1}
- Booth/Glass 没有礼物带，只来自初始包（种子）→ 只在“已经在电话亭”时给同组加分，所以电话亭组内跳转偏多（52%），另两组组内约 1/3。
- 纯 bag 穷举结果：每个梦 7.5%–13.5%，不跳 5.2%。脚本 scratchpad `dreams/group.js`。
- 9 个 dream 资产 + simulator-unity.html 已同步；改前备份 scratchpad `dreams/backup3/`。

## 2026-11-14 Bug：旧梦礼物不随梦消失 + 新梦里抓不动

- 读完整条 生成→穿门→清场 路径：纸面逻辑正确，问题在它覆盖不到的情况。头显未连，拿不到日志，先上诊断 + 防御。
- 嫌疑（按吻合度）：① 客户端本地「幽灵」ObjectGift（没上网 → 清场管不到、server 上不存在 → 抓不动）；② portal「一碰就翻 owner」，伸进去抽回来 owner 就错了 → 按 owner 清场漏掉；③ 没进账本的礼物。
- 诊断：`DreamNetworkManager.LogGiftSnapshotLater`——每台机器在换梦凝入后 ~8s 打 `[GiftGhost]` 快照（live/GHOST、owner、layer、origin/current、root、pos）。
- 防御 1：PortalDirectionTrigger 与 GiftDeliveryTrigger（多人路径）都改成「真正穿过才算」：进 trigger 记 bounds 中心在门面哪侧，出 trigger 侧变了才翻 owner / 结算；同侧出来只打日志。单人 DreamSingleManager 仍是进入即结算。
- 防御 2：换梦清场除了 owner 匹配，再清 `currentDream == 该 world 旧梦` 的礼物（ApplyVisual 现在拿到 oldIndex）。已知边角：缓冲期里带过门且走了 stale 分支的礼物也会被当旧梦清掉。
- 防御 3：快照之后删除本机「可见的」幽灵（activeInHierarchy 且未 Spawn；跳过 server 的 pendingSpawn）。只删礼物本身不删上级。
- 未编译验证（Unity MCP 未连接）。查日志：`adb logcat -s Unity | grep -E "GiftGhost|GiftDiag|Portal\]|GiftDelivery"`。

## 2026-11-15 simulator 礼物数量同步
- 核对：simulator-unity.html 的 UNITY_SIG / GIFT_KW 与 9 个 dream asset、16 个 gift asset 完全一致。
- 按房间 prefab 重新统计 ObjectGift（含嵌套 prefab），更新 ROOM_GIFTS_RAW：
  - ChildRoomCloud：去掉 GoldenFish 5（实际是 SunnyCloud 5 = Cloud prefab）
  - ChildRoomCurtain：去掉 SunnyCloud 5（实际是 GoldenFish 5）
  - BoothSunset：FlowerAtNight 11 → 9
  - BoothCloud 的 SunnyCloud 3 = CloudPink（gift 指向 SunnyCloud）
- 7/8 Asian 礼物仍与 6 相同，改完后需再统计。备份：scratchpad dreams/simulator-unity.before-counts.html
- 发现：0、1 号房间的 Animal_TeddyBear_bluetie 实例上多加了一个 ObjectGift（gift 为空、networkPrefab=TeddyBear），与 prefab 自带的重复；可能产生无 gift 的幽灵礼物，待用户在 prefab 里 Remove。
- CloudPink 已加入 Assets/Settings/DefaultNetworkPrefabs.asset（场景实际使用的那份）；DefaultNetworkPrefabs2.asset 无任何引用，是 7/17 的副本。

## 2026-11-15 simulator：portal 常开
- simulator-unity.html 交互区加「portal 常开」勾选（默认勾上）：render() 里直接把 S.portal 设为 open，换梦后也立刻重开，「带过门」随时可点。批量模拟仍走握手逻辑。
- 备份：scratchpad dreams/simulator-unity.before-alwaysopen.html

## 2026-11-15 simulator：礼物数量标签
- 礼物行「Name ×N」改成「Name（这里还有 N 个）」，避免误读成「要送 N 个」。备份 scratchpad dreams/simulator-unity.before-countlabel.html

## 2026-11-15 保真度核对 + 电话亭陷阱
- 核对：PlayerDreamBag（seed=1、score、lead/bumper、排除自己+对方）与 simulator-unity.html 逐行一致；MR 场景两个 bag 都是 push 4 / lead 2 / 9 个梦 0..8；今天 11:02 的 build 晚于权重修改（11-14 10:02），所以 build 里是新权重。
- 「两边落到同一个梦」的保护还在：DeliverGift 传 partner = 对方 bag.Current，EvaluateJump 排除。模拟器里看起来一样是因为 BoothCloud 和 ChildRoomCloud 共用同一张图（3 个亚洲房也共用一张）。
- 用户实测：都跳去 BoothCloud、在电话亭组里绕圈。新写 scratchpad dreams/markov.js（MR 初始 + 房间实际礼物数的连续游玩模拟）复现：电话亭 54%，GameRoom / Submarine 各 2.7%。原因：上次调权重的指标是「所有 当前×对方×礼物种类」均匀平均，没按实际起点、礼物数量和连续跳转来算。
- retune.js 以连续游玩为目标重新搜权重（结果在 scratchpad dreams/retuned.json，尚未写入 Unity / HTML，等用户确认）：三组 30~36%，每个梦 9~13%，93% 的送礼会触发换梦。
- 注意：若把 Game / Game Mario 改成 GameCartridges，同一组权重会变成 儿童房 42% / 电话亭 24%，两件事要一起定。
- MR 里「FlowerAtNight BoothSunset→ChildRoomCloud 跳去 BoothCloud」模拟器预测是 AsianShrine，没有 Quest 日志无法解释（adb 无设备）。SunsetCloud / TelephoneHandset 才会把 ChildRoomCloud 推去 BoothCloud。
- simulator：「会触发换梦」改成「会触发对方换梦」。
- 旧礼物不消失的 bug：今天的 build 已含 [GiftGhost] 诊断和三处防御，用户反馈仍然严重 → 防御没解决，需要 Quest logcat。

## 2026-11-15 simulator：权重方案切换
- 顶部加「权重方案」下拉：工程现状 / 新方案（PROPOSED_SIG = retuned.json，未写入 Unity）。切换后编辑器、跳转表、批量模拟都跟着变。备份 scratchpad dreams/simulator-unity.before-proposed.html

## 2026-11-15 simulator：换成 9 张新房间图
- IMG 映射改为每个梦各一张（0playroomcloud / 1 playroomcurtain / 2 playroomsubmarine / 3 Phone booth / 4 Phone Booth at Sunset / 5 Phone Booth In Clouds / 6 Asianshrine / 7 Asian Game Room / 8 Asian MoonBunny），文件都存在。

## 2026-11-15 黏性版权重（模拟器预览，未写入 Unity）
- 用户要求 playroom 留在 playroom 系列。扫了一遍：只靠种子里的组名关键词，权重 4 时留组约 67%，但会抢电话亭的时间；需要整体重调。
- retune3.js：每组一个组名关键词（Playroom 8 / Booth 10 / Asian 9），礼物按 Game 已修成 GameCartridges 计算。结果 retuned3.json：留在本组 儿童房 66% / 电话亭 69% / 亚洲 58%，时间占比 32/37/30，每个梦 9~13%，触发率 99%。
- simulator：PROPOSED_SIG 换成这一版；KEYWORDS 加 Playroom、Asian；「修正 Game」勾选改为对 3 个亚洲房都生效，选新方案时自动勾上。备份 scratchpad dreams/simulator-unity.before-sticky.html
- Unity 里 Game.prefab / Game Mario.prefab 的 gift 仍是 FlowerInterior（未改，等用户确认）。写入 Unity 还需要新建 Playroom、Asian 两个 Keyword asset。

## 2026-11-15 simulator：纯权重模式
- 「portal 常开」勾选改名为「纯权重模式」（默认勾上）：portal 常开 + doCarry 后把排队事件（缓冲/过渡/spawn）一次跑完，送礼立即换梦。取消勾选回到真实时序。备份 scratchpad dreams/simulator-unity.before-instant.html

## 2026-11-15 simulator：点击丢失
- 原因：loop 每 0.2s 整页 innerHTML 重画，按钮 DOM 被替换，mousedown / mouseup 落在不同元素上，click 不触发。改成只有状态变化才重画，平时只更新时钟。

## 2026-11-15 simulator：默认值
- 打开即 mr 预设（World0 ChildRoomCloud / World1 BoothSunset）+ 新方案权重（自动勾 Game 修正）。

## 2026-11-15 simulator：隐藏时序控件
- 时钟/流速/握手/纯权重开关整块 display:none，固定纯权重模式。

## 2026-11-15 simulator：删除时序控件
- 时钟/流速/跳事件/握手/纯权重开关整块删除，render 固定 portal 常开，doCarry 送礼后立即跑完事件，去掉 requestAnimationFrame 循环。

## 2026-11-15 留组 vs 能去别的系列：结构性矛盾
- 用户截图：亚洲玩家收到电话亭的礼物（SunnyCloud/Flower/TeddyBear）全部跳出亚洲。原因：外组礼物 push 4 × 权重（Cloud 7 等）≈ 28 分，远大于种子里组名关键词的 1 × 9。
- seedtest.js：把种子里的组名数量从 1 提高到 2/3/4 → 玩家困死在起始组，MR 开局（儿童房 + 电话亭）下亚洲变 0%。权重无法同时做到「很黏」和「三组都去得了」。
- famrule.js：试「本组优先」规则（组外第 1 名要比组内第 1 名多 M 分才跳出组）。M 是平滑旋钮：M=8 时留组 73/69/54%，M=12 时 84/79/67%（温室 1%，需配合调权重），M=20 时亚洲 0%。需要改 PlayerDreamBag.EvaluateJump，未动。

## 2026-11-15 方案 v3：权重 + 礼物改动联合搜索（模拟器预览，未写入 Unity）
- 用户要求：同系列留组 ≥ 40%，不能锁死在起始两个系列；可以加礼物 / 改礼物 keyword。
- pairs.js：逐对（收礼房间 × 对方房间）看留组率。上一版 retuned3 有 3 对 0%、16 对 100%，这就是「亚洲房一收礼就全跑了」的来源——整体留组率达标，但具体某一对是死角。
- opt.js / opt2.js：目标 = 每一对留组率落在 30%~85% + 连续游玩三系列均匀 + 每系列留组 ≥ 50%；变量 = dream 权重、组名关键词、21 个候选礼物改动（每个改动有惩罚，尽量少改）。
- 结果 opt_result.json（800 局复核）：留组 儿童房 49% / 电话亭 62% / 亚洲 62%；时间 37/31/32；每个梦 9~13%；触发率 98%；72 对里 0% 和 100% 的都是 0 对。
- 礼物改动 4 处：DirtToy 礼物加 Playroom；BoothCloud 加 2 个 TelephoneHandset、3 个 Balloon；BoothGreenhouse 再加 2 个 Raccoon。另含 Game / Game Mario → GameCartridges。
- 权重：ChildRoomCloud Toy4 Balloon7 Cloud8 Sunlight1 Playroom6｜ChildRoomCurtain Toy3 Balloon5 Animal7 Computer3 Playroom6｜ChildRoomSubmarine Toy8 Balloon2 Dark1 Computer1 Playroom6｜BoothGreenhouse Booth9 Glass4 Computer3 Flower4 Sunlight1 Animal4｜BoothSunset Booth9 Glass6 Computer3 Dark2 Cloud5 Halo5｜BoothCloud Booth9 Glass5 Computer3 Cloud8 Sunlight4 Balloon2｜AsianShrine Halo6 Flower2 Dark3 Animal3 Asian10｜AsianGameRoom Halo5 Flower1 Computer5 Toy1 Asian10｜AsianMoonBunny Halo5 Flower2 Animal1 Sunlight2 Asian10
- simulator：PROPOSED_SIG 换成 v3，加 PROPOSED_GIFT_KW / PROPOSED_ROOM_ADD / applyGiftSet()，选新方案时礼物改动一起生效。headless.js 用假 DOM 跑通了初始化和连续送礼。备份 scratchpad dreams/simulator-unity.before-opt.html

## 2026-11-15 v3 存档 + 规则 B（固定系列 + 隐藏亚洲房）原型
- 用户反馈 v3「随机乱跳，我的变你的」，不要。v3 存档：Worlds/saved-schemes/v3-free-roam.json（权重、礼物改动、结果）+ Worlds/simulator-unity.v3-free-roam.html（当时的模拟器整页）。
- 用户的新方案：玩家 0 固定在儿童房系列、玩家 1 固定在电话亭系列；连续两件礼物带 Animal → 月兔，带 Computer/Toy → 游戏房，带 Dark → 夜神社（计数不随换梦清零）；到了亚洲房，下一件礼物一定回自家系列，但这件算进计数。
- simulator：加「跳转规则」下拉（B 固定系列 + 隐藏亚洲房 / A 自由跳），默认 B。evaluateFixed()、HIDDEN、SERIES，state 加 last:[[],[]]。备份 scratchpad dreams/simulator-unity.before-fixedrule.html
- 我补的三条默认规则（待用户确认）：① 一件礼物同时中多类时按 Animal → Game → Dark 取第一个；② 对方已经在那个亚洲房时不去，照常在自家系列里换；③ 触发去亚洲房后计数清零。
- headless2.js 跑 6000 次随机送礼（v3 权重 + 现有礼物）：World0（儿童房玩家）夜神社 10%、游戏房 0.5%、月兔 0.2%；World1（电话亭玩家）游戏房 25%、月兔 0.9%、夜神社 0.2%，温室只有 9%。原因是礼物构成：儿童房里 TV 13 + Toy 8 占大多数 → 电话亭玩家不停进游戏房；Animal 礼物太少 → 月兔几乎去不了。需要调礼物数量或触发条件。

## 2026-11-15 规则 B 方案（模拟器预览，未写入 Unity）
- 用户同意方向：电话亭多放电话；每个房间至少 2 个 Animal（多加金鱼和熊），否则没法连续送两个。
- ruleB.js：规则 B 的快速模拟 + 搜索。让优化器随便加礼物时要加 47 个才勉强均匀——「连续两件」的概率是占比的平方，随机送礼下隐藏房间天生稀少；真实玩家是有意去凑的，所以改成保证「凑得出来」而不是追随机频率。
- ruleB_min.js（最小加法）：ChildRoomCloud +1 TeddyBear；ChildRoomSubmarine +2 TeddyBear；BoothGreenhouse +1 Raccoon、+2 TelephoneHandset；BoothSunset +2 Raccoon、+2 TelephoneHandset；BoothCloud +1 TeddyBear、+3 TelephoneHandset。共 14 个。
- 权重（只有自家系列 6 间参与计分）：ChildRoomCloud Toy5 Balloon8 Cloud8 Sunlight1 Playroom6｜ChildRoomCurtain Toy3 Balloon5 Animal7 Computer6 Playroom6｜ChildRoomSubmarine Toy8 Balloon1 Dark1 Computer2 Playroom6｜BoothGreenhouse Booth9 Glass5 Computer3 Flower4 Sunlight2 Animal2｜BoothSunset Booth9 Glass6 Computer2 Dark3 Cloud5 Halo7｜BoothCloud Booth9 Glass4 Computer2 Cloud8 Sunlight6 Balloon2
- 随机送礼 500 局：World0 儿童房三间 31/28/30%，夜神社 6%、游戏房 4%、月兔 1%；World1 电话亭三间 24/24/25%，游戏房 22%、夜神社 3%、月兔 2%。
- 多类礼物优先级改成 Animal → Dark → 游戏（原来游戏在 Dark 前，两个 DirtToy 永远进游戏房、电话亭玩家去不了夜神社）。
- simulator：加 RULEB_SIG / RULEB_ROOM_ADD / SCHEMES，权重方案下拉多一项「规则 B 方案」并设为默认；applyGiftSet(key) 通用化。备份 scratchpad dreams/simulator-unity.before-ruleBset.html。结果存 scratchpad dreams/ruleB_min.json
