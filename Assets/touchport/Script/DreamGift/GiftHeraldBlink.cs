using UnityEngine;

namespace DreamTouch
{
    // 换梦预告的小闪光点：挂在 giftHeraldPrefab 根上（prefab = 一个 Quad + Additive 发光贴图材质）。
    // 由 DreamNetworkManager.HeraldGiftClientRpc 实例化为礼物的子物体，所以礼物 despawn
    // （随旧梦清场）时它自动一起销毁；另有兜底寿命，见 HeraldGiftClientRpc。
    //
    // 性能：一个 quad、零粒子、零 GC，每帧只写一次 transform。
    // 闪烁用缩放而【不】用 renderer.enabled——IObjectXR.ApplyVisibility 每帧都会把礼物所有子
    // Renderer 的 enabled 覆盖成礼物自己的可见性，这里改 enabled 会被它冲掉。
    public class GiftHeraldBlink : MonoBehaviour
    {
        [Tooltip("光点的世界尺寸（米），不受礼物缩放影响。")]
        public float worldSize = 0.06f;

        [Tooltip("光点在礼物包围盒顶部之上多高（米）。")]
        public float heightAboveGift = 0.06f;

        [Tooltip("一次完整呼吸（亮→暗→亮）的秒数。")]
        public float breathSeconds = 3f;

        [Tooltip("亮态停留：越大亮的时间越长、暗的间隔越短（1 = 纯正弦，亮暗各半）。")]
        [Range(1f, 6f)] public float brightHold = 2.5f;

        [Tooltip("最暗时保留的尺寸比例（0 = 完全消失）。")]
        [Range(0f, 1f)] public float minScale = 0.2f;

        Transform cam;
        float phase;

        void Start()
        {
            phase = Random.value;   // 多个礼物同时闪时别完全同步
            PlaceAboveGift();
        }

        // 用礼物（父物体）所有 Renderer 的包围盒顶部定位，世界坐标；排除自己的 renderer。
        void PlaceAboveGift()
        {
            var parent = transform.parent;
            if (parent == null) return;

            bool has = false;
            Bounds b = default;
            foreach (var r in parent.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(transform)) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            Vector3 top = has ? new Vector3(b.center.x, b.max.y, b.center.z) : parent.position;
            transform.position = top + Vector3.up * heightAboveGift;
        }

        void LateUpdate()
        {
            if (cam == null)
            {
                var c = Camera.main;
                if (c == null) return;
                cam = c.transform;
            }

            // 朝向相机（quad 正面朝 -forward，所以 forward 指向远离相机的方向）
            transform.rotation = Quaternion.LookRotation(transform.position - cam.position, cam.up);

            // 呼吸灯：sin 波做 1-(1-w)^n，亮端被拉宽成平台（慢慢亮、长时间亮），
            // 暗端被压成一个短而平滑的低谷（暗的间隔短）。两端导数连续，不会"跳"。
            float wave = 0.5f + 0.5f * Mathf.Sin((Time.time / Mathf.Max(0.1f, breathSeconds) + phase) * Mathf.PI * 2f);
            float k = Mathf.Lerp(minScale, 1f, 1f - Mathf.Pow(1f - wave, brightHold));

            // 抵消父物体缩放，保证世界尺寸固定
            var ps = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            float s = worldSize * k;
            transform.localScale = new Vector3(s / SafeAbs(ps.x), s / SafeAbs(ps.y), s / SafeAbs(ps.z));
        }

        static float SafeAbs(float v) => Mathf.Max(1e-4f, Mathf.Abs(v));
    }
}
