using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DreamTouch
{
    // The presenter. The ONLY place that touches how the dream looks.
    // Loads the target dream ASYNC via its AssetReference (dreamAsset) and releases the
    // previous one, so only the CURRENT dream's assets stay in memory (Quest-friendly).
    public class ChangeDreamByGift : MonoBehaviour
    {
        [Tooltip("Where the dream is parented. Defaults to this object.")]
        [UnityEngine.Serialization.FormerlySerializedAs("worldRoot")] public Transform dreamRoot;

        [Tooltip("Layer to put the whole spawned dream on, for the portal stencil " +
                 "(World0 = layer0, World1 = layer1). -1 = use this GameObject's layer.")]
        public int worldLayer = -1;

        GameObject currentInstance;
        AsyncOperationHandle<GameObject> currentHandle;
        bool hasHandle;
        string loadingId;                            // guards against overlapping switches (diff dreams)
        DefinitionDream pendingOrCurrent;            // idempotency: skip re-Switch of the SAME dream

        public void Switch(DefinitionDream to)
        {
            if (to == null || to == pendingOrCurrent) return;
            pendingOrCurrent = to;
            var parent = dreamRoot != null ? dreamRoot : transform;

            ClearCurrent();                          // unload the previous dream first
            loadingId = to.dreamId;

            if (to.dreamAsset == null || !to.dreamAsset.RuntimeKeyIsValid())
            {
                Debug.LogWarning($"[ChangeDreamByGift] '{to.dreamId}' has no valid dreamAsset assigned.", this);
                return;
            }

            // Lazy async load: only THIS dream's assets get pulled into memory.
            var handle = to.dreamAsset.InstantiateAsync(parent);
            handle.Completed += op =>
            {
                // A newer Switch() started meanwhile -> discard this stale result.
                if (loadingId != to.dreamId)
                {
                    if (op.Status == AsyncOperationStatus.Succeeded) Addressables.ReleaseInstance(op);
                    else Addressables.Release(op);
                    return;
                }

                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    currentInstance = op.Result;
                    currentHandle = op;
                    hasHandle = true;
                    ApplyLayer(currentInstance);
                }
                else
                {
                    Debug.LogWarning($"[ChangeDreamByGift] load failed for '{to.dreamId}' ({op.Status}).", this);
                    Addressables.Release(op);
                }

                // Hook portal FX / crossfade / audio for `to` here.
            };
        }

        void ClearCurrent()
        {
            if (hasHandle && currentHandle.IsValid())
                Addressables.ReleaseInstance(currentHandle);   // destroys instance + unloads its assets
            else if (currentInstance != null)
                Destroy(currentInstance);

            currentInstance = null;
            hasHandle = false;
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

        void OnDestroy() => ClearCurrent();
    }
}
