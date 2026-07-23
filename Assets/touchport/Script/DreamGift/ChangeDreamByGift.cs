using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DreamTouch
{
    // The presenter. The ONLY place that touches how the dream looks.
    // Loads dreams ASYNC via AssetReference (dreamAsset) and releases the previous one, so
    // only the CURRENT dream's assets stay in memory (Quest-friendly) — plus, at most, ONE
    // preloaded dream waiting inactive during the buffer window before a switch.
    //
    // 过渡时序（Switch 状态机，顺序溶解）：
    //   等新梦就绪（已预载则立即；兜底路径先加载，期间旧梦原样保留）
    //   → 先让旧梦溶出并停用，再激活新梦溶入
    //   → Release 延迟 releaseDelay 秒错开卸载尖峰。
    // 房间/装饰材质需用 DreamTouch/DissolveOcclusion* shader；_DissolveProgress=0 时视觉等同
    // 普通 occlusion 材质，平时不换材质不换 shader。礼物材质不带该属性，自动不参与溶解。
    public class ChangeDreamByGift : MonoBehaviour
    {
        // Fired when a dream instance finishes loading (preload done, or fallback load done).
        // The instance may still be INACTIVE at this point — "ready", not necessarily visible.
        // DreamNetworkManager listens to this to handle the ObjectGift markers baked into the
        // dream prefab (server pre-instantiates real gifts, clients delete the local ghosts).
        public event Action<GameObject, DefinitionDream> OnInstanceReady;

        // Fired when a Switch() transition fully finishes (dissolve-in done, dream visible).
        // On load FAILURE this still fires with instance=null so orchestrators can unlock.
        public event Action<GameObject, DefinitionDream> OnTransitionComplete;

        [Tooltip("Where the dream is parented. Defaults to this object.")]
        [UnityEngine.Serialization.FormerlySerializedAs("worldRoot")] public Transform dreamRoot;

        [Tooltip("Layer to put the whole spawned dream on, for the portal stencil " +
                 "(World0 = layer0, World1 = layer1). -1 = use this GameObject's layer.")]
        public int worldLayer = -1;

        [Header("Dissolve transition")]
        [Tooltip("Seconds for the old dream to dissolve out (progress 0 -> 1).")]
        public float dissolveOutDuration = 3f;
        [Tooltip("Seconds for the new dream to dissolve in (progress 1 -> 0).")]
        public float dissolveInDuration = 3f;
        [Tooltip("Easing for both dissolve directions (x = normalized time, y = blend 0->1).")]
        public AnimationCurve dissolveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("Seconds after the crossfade before the old dream's assets are actually " +
                 "released — spreads the unload hitch away from the transition moment.")]
        public float releaseDelay = 2f;

        static readonly int DissolveProgressId = Shader.PropertyToID("_DissolveProgress");
        static readonly int CullId = Shader.PropertyToID("_Cull");
        static readonly int UseEmissionId = Shader.PropertyToID("_UseEmission");
        const string EmissionKeyword = "_DREAM_EMISSION";

        public static bool ShaderOptimizationEnabled { get; private set; } = true;
        public static string ShaderABModeName => ShaderOptimizationEnabled ? "Optimized" : "Baseline";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeState()
        {
            ShaderOptimizationEnabled = true;
            dissolveRenderingCount = 0;
            Shader.DisableKeyword(DissolveKeyword);
        }

        // 全局 dissolve keyword 引用计数：只有过渡期间 shader 才编译 clip 分支。
        // discard 指令的存在（哪怕永不触发）会让 GPU 关掉 early-Z/隐面剔除，全屏大 mesh
        // 每个像素都得跑完整 fragment——平时必须走无 clip 变体，这是性能命门。
        const string DissolveKeyword = "_DREAM_DISSOLVING";
        static int dissolveRenderingCount;
        bool holdsDissolveKeyword;   // 本 presenter 是否占着一份计数（OnDestroy 兜底归还用）

        static void BeginDissolveRendering()
        {
            if (++dissolveRenderingCount == 1) Shader.EnableKeyword(DissolveKeyword);
        }

        static void EndDissolveRendering()
        {
            dissolveRenderingCount = Mathf.Max(0, dissolveRenderingCount - 1);
            if (dissolveRenderingCount == 0) Shader.DisableKeyword(DissolveKeyword);
        }

        // ── current 槽：正在展示的梦 ──
        GameObject currentInstance;
        AsyncOperationHandle<GameObject> currentHandle;
        bool hasCurrentHandle;
        DefinitionDream currentDream;
        List<Renderer> currentRenderers = new List<Renderer>();   // renderers on the dissolve shader

        // ── preload 槽：缓冲期里待命的下一个梦（inactive、progress=1）──
        GameObject preloadedInstance;
        AsyncOperationHandle<GameObject> preloadedHandle;
        bool hasPreloadedHandle;
        DefinitionDream preloadedDream;                            // set only when the load COMPLETES
        List<Renderer> preloadedRenderers = new List<Renderer>();
        string preloadingId;                                       // dreamId in flight; stale results self-discard

        // ── 过渡状态 ──
        Coroutine transitionCo;
        DefinitionDream queuedDream;   // 过渡中收到的新 Switch 排队（容量 1，后来者覆盖）。
                                       // 正常流程被 server 的缓冲锁挡住，这是兜底。

        MaterialPropertyBlock mpb;

        public static void SetShaderOptimizationEnabled(bool enabled)
        {
            ShaderOptimizationEnabled = enabled;

            foreach (var presenter in FindObjectsByType<ChangeDreamByGift>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                ApplyShaderABMode(presenter.currentRenderers);
                ApplyShaderABMode(presenter.preloadedRenderers);
            }

            Debug.LogWarning(
                $"[ShaderAB] ACTIVE={ShaderABModeName} " +
                $"cull={(enabled ? "Back" : "Off")} emissionSample={(enabled ? "Off" : "On")}");
        }

        // 预载目标梦：异步加载 + Instantiate 后保持 inactive、_DissolveProgress=1，等 Switch 消费。
        // 幂等：同一个梦重复调用 no-op；换了目标则丢弃旧预载（在途的靠 preloadingId 过期自弃）。
        public void Preload(DefinitionDream to)
        {
            if (to == null || to == currentDream) return;
            if (preloadedDream == to || preloadingId == to.dreamId) return;

            ReleasePreloaded();

            if (to.dreamAsset == null || !to.dreamAsset.RuntimeKeyIsValid())
            {
                Debug.LogWarning($"[ChangeDreamByGift] '{to.dreamId}' has no valid dreamAsset assigned.", this);
                return;
            }

            preloadingId = to.dreamId;
            var parent = dreamRoot != null ? dreamRoot : transform;
            var handle = to.dreamAsset.InstantiateAsync(parent);
            handle.Completed += op =>
            {
                // A newer Preload()/teardown happened meanwhile -> discard this stale result.
                if (preloadingId != to.dreamId)
                {
                    if (op.Status == AsyncOperationStatus.Succeeded) Addressables.ReleaseInstance(op);
                    else Addressables.Release(op);
                    return;
                }
                preloadingId = null;

                if (op.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogWarning($"[ChangeDreamByGift] load failed for '{to.dreamId}' ({op.Status}).", this);
                    Addressables.Release(op);
                    return;
                }

                var root = op.Result;
                // 回调与 Instantiate 同帧、发生在渲染前——先藏起来再把 progress 设满，
                // 房间激活前绝不允许任何一帧全貌闪现。
                root.SetActive(false);
                ApplyLayer(root);
                CollectDissolveRenderers(root, preloadedRenderers);
                SetDissolveProgress(preloadedRenderers, 1f);

                preloadedInstance = root;
                preloadedHandle = op;
                hasPreloadedHandle = true;
                preloadedDream = to;
                Debug.Log($"[ChangeDream] '{name}' preloaded '{root.name}' ({to.dreamId}), waiting inactive.", this);
                OnInstanceReady?.Invoke(root, to);
            };
        }

        public void Switch(DefinitionDream to)
        {
            if (to == null || to == currentDream) return;
            if (transitionCo != null) { queuedDream = to; return; }   // 过渡中：排队到结束后执行
            transitionCo = StartCoroutine(SwitchRoutine(to));
        }

        IEnumerator SwitchRoutine(DefinitionDream to)
        {
            // 兜底/单机路径：没预载就现在开载。加载期间旧梦原样保留——portal 里不能出现空档。
            if (preloadedDream != to && preloadingId != to.dreamId) Preload(to);
            while (preloadingId == to.dreamId) yield return null;   // 等预载落地（已就绪则直接通过）

            if (preloadedDream != to || preloadedInstance == null)
            {
                // 加载失败（或资产无效）：旧梦保持原样，结束过渡并照常广播完成，免得上层缓冲锁卡死。
                Debug.LogWarning($"[ChangeDreamByGift] switch to '{to.dreamId}' aborted — no instance available.", this);
                transitionCo = null;
                OnTransitionComplete?.Invoke(null, to);
                DequeueNext();
                yield break;
            }

            // 旧梦交给延迟释放，current 槽腾给新梦（首次加载没有旧梦）。
            var oldInstance = currentInstance;
            var oldHandle = currentHandle;
            bool oldHasHandle = hasCurrentHandle;
            var oldRenderers = currentRenderers;

            currentInstance = preloadedInstance;
            currentHandle = preloadedHandle;
            hasCurrentHandle = hasPreloadedHandle;
            currentDream = to;
            currentRenderers = preloadedRenderers;
            preloadedInstance = null;
            hasPreloadedHandle = false;
            preloadedDream = null;
            preloadedRenderers = new List<Renderer>();

            // keyword 必须先开再激活：progress=1 的"隐身"就是靠 clip 变体实现的。
            BeginDissolveRendering();
            holdsDissolveKeyword = true;

            bool oldAlreadyHidden = false;
            if (oldInstance != null)
            {
                // Keep the old crossfade's total duration: two 5 s phases become 2.5 s + 2.5 s.
                float originalTotal = Mathf.Max(dissolveOutDuration, dissolveInDuration);
                float requestedTotal = Mathf.Max(0f, dissolveOutDuration) + Mathf.Max(0f, dissolveInDuration);
                float outDur = requestedTotal > 0f
                    ? originalTotal * Mathf.Max(0f, dissolveOutDuration) / requestedTotal
                    : 0f;
                float inDur = Mathf.Max(0f, originalTotal - outDur);

                for (float t = 0f; t < outDur; t += Time.deltaTime)
                {
                    SetDissolveProgress(oldRenderers,
                        dissolveCurve.Evaluate(Mathf.Clamp01(t / outDur)));
                    yield return null;
                }

                SetDissolveProgress(oldRenderers, 1f);
                oldInstance.SetActive(false);
                oldAlreadyHidden = true;

                currentInstance.SetActive(true);
                for (float t = 0f; t < inDur; t += Time.deltaTime)
                {
                    SetDissolveProgress(currentRenderers,
                        1f - dissolveCurve.Evaluate(Mathf.Clamp01(t / inDur)));
                    yield return null;
                }
            }
            else
            {
                // Initial load has no old dream, so only fade the new dream in.
                currentInstance.SetActive(true);
                float outDur = oldInstance != null ? dissolveOutDuration : 0f;
                float total = Mathf.Max(outDur, dissolveInDuration);
                for (float t = 0f; t < total; t += Time.deltaTime)
                {
                    if (oldInstance != null && outDur > 0f)
                        SetDissolveProgress(oldRenderers,
                            dissolveCurve.Evaluate(Mathf.Clamp01(t / outDur)));
                    if (dissolveInDuration > 0f)
                        SetDissolveProgress(currentRenderers,
                            1f - dissolveCurve.Evaluate(Mathf.Clamp01(t / dissolveInDuration)));
                    yield return null;
                }
            }
            SetDissolveProgress(currentRenderers, 0f);

            // 旧梦已全溶（全被 clip），立即关掉；真正的 Release 延后错开，藏起卸载尖峰。
            if (oldInstance != null)
            {
                if (!oldAlreadyHidden)
                {
                    SetDissolveProgress(oldRenderers, 1f);
                    oldInstance.SetActive(false);
                }
                ScheduleRelease(oldHandle, oldHasHandle, oldInstance);
            }

            // 新梦已到 progress=0、旧梦已隐藏——关回无 clip 变体，early-Z 恢复。
            holdsDissolveKeyword = false;
            EndDissolveRendering();

            transitionCo = null;
            OnTransitionComplete?.Invoke(currentInstance, to);
            DequeueNext();
        }

        void DequeueNext()
        {
            if (queuedDream == null) return;
            var next = queuedDream;
            queuedDream = null;
            Switch(next);
        }

        // 延迟释放旧梦：登记 + 定时。挂在列表上是为了 OnDestroy 时能兜底释放（协程会随组件销毁停掉）。
        struct PendingRelease
        {
            public AsyncOperationHandle<GameObject> handle;
            public bool hasHandle;
            public GameObject instance;
        }
        readonly List<PendingRelease> pendingReleases = new List<PendingRelease>();

        void ScheduleRelease(AsyncOperationHandle<GameObject> handle, bool hasHandle, GameObject instance)
        {
            var entry = new PendingRelease { handle = handle, hasHandle = hasHandle, instance = instance };
            pendingReleases.Add(entry);
            StartCoroutine(ReleaseAfterDelay(entry));
        }

        IEnumerator ReleaseAfterDelay(PendingRelease entry)
        {
            if (releaseDelay > 0f) yield return new WaitForSeconds(releaseDelay);
            pendingReleases.Remove(entry);
            DoRelease(entry);
        }

        static void DoRelease(PendingRelease entry)
        {
            if (entry.hasHandle && entry.handle.IsValid())
                Addressables.ReleaseInstance(entry.handle);   // destroys instance + unloads its assets
            else if (entry.instance != null)
                Destroy(entry.instance);
        }

        void SetDissolveProgress(List<Renderer> renderers, float value)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.SetFloat(DissolveProgressId, value);
            foreach (var r in renderers)
                if (r != null) r.SetPropertyBlock(mpb);
        }

        // 只挑用 dissolve shader 的 renderer（按材质是否有 _DissolveProgress 判断）——
        // 礼物 marker 等其它材质自动排除，不参与溶解。
        static void CollectDissolveRenderers(GameObject root, List<Renderer> into)
        {
            into.Clear();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial;
                if (m != null && m.HasProperty(DissolveProgressId)) into.Add(r);
            }

            ApplyShaderABMode(into);
        }

        static void ApplyShaderABMode(List<Renderer> renderers)
        {
            var visited = new HashSet<Material>();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !material.HasProperty(DissolveProgressId) ||
                        !visited.Add(material)) continue;

                    if (material.HasProperty(CullId))
                        material.SetFloat(CullId, ShaderOptimizationEnabled
                            ? (float)CullMode.Back
                            : (float)CullMode.Off);

                    if (!material.HasProperty(UseEmissionId)) continue;
                    material.SetFloat(UseEmissionId, ShaderOptimizationEnabled ? 0f : 1f);
                    if (ShaderOptimizationEnabled) material.DisableKeyword(EmissionKeyword);
                    else material.EnableKeyword(EmissionKeyword);
                }
            }
        }

        void ClearCurrent()
        {
            if (hasCurrentHandle && currentHandle.IsValid())
                Addressables.ReleaseInstance(currentHandle);   // destroys instance + unloads its assets
            else if (currentInstance != null)
                Destroy(currentInstance);

            currentInstance = null;
            hasCurrentHandle = false;
            currentDream = null;
            currentRenderers.Clear();
        }

        void ReleasePreloaded()
        {
            preloadingId = null;   // 在途加载完成时发现 id 不匹配会自弃
            if (hasPreloadedHandle && preloadedHandle.IsValid())
                Addressables.ReleaseInstance(preloadedHandle);
            else if (preloadedInstance != null)
                Destroy(preloadedInstance);

            preloadedInstance = null;
            hasPreloadedHandle = false;
            preloadedDream = null;
            preloadedRenderers.Clear();
        }

        // Put the whole dream on the world's stencil layer so the portal renders it correctly.
        void ApplyLayer(GameObject root)
        {
            if (root == null) return;
            int layer = worldLayer >= 0 ? worldLayer : gameObject.layer;
            SetLayerRecursively(root.transform, layer);
            Debug.Log($"[ChangeDream] '{name}' spawned '{root.name}' on layer {layer} ({LayerMask.LayerToName(layer)})", this);
        }

        static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursively(t.GetChild(i), layer);
        }

        void OnDestroy()
        {
            // 过渡中途被销毁：把占着的 keyword 计数还回去，别让全场景永远卡在 clip 变体上。
            if (holdsDissolveKeyword)
            {
                holdsDissolveKeyword = false;
                EndDissolveRendering();
            }
            ClearCurrent();
            ReleasePreloaded();
            // 还挂在延迟释放队列里的旧梦（协程随组件销毁停掉了）在这里兜底释放。
            foreach (var entry in pendingReleases) DoRelease(entry);
            pendingReleases.Clear();
        }
    }
}
