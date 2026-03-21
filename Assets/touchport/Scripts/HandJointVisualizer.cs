using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Demo
{
    /// <summary>
    /// 本地手部：从 OVRSkeleton 读取所有骨骼，显示蓝色小方块（24个/手）
    /// 远端手部：从 HandsManager NetworkVariable 读取 7 个关键点，显示红色小方块
    /// </summary>
    public class HandJointVisualizer : MonoBehaviour
    {
        [SerializeField] private OVRSkeleton leftSkeleton;
        [SerializeField] private OVRSkeleton rightSkeleton;
        [SerializeField] private float jointSize = 0.012f;

        private GameObject[] _localL;
        private GameObject[] _localR;
        private GameObject[] _remoteL;  // 7 个红色（对方左手）
        private GameObject[] _remoteR;  // 7 个红色（对方右手）

        private Material _blueMat;
        private Material _redMat;

        private void Awake()
        {
            _blueMat = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.2f, 0.5f, 1f) };
            _redMat  = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.3f, 0.3f) };

            // 本地骨骼数量在运行时才知道，先分配24个（OVR手部标准数量）
            _localL  = MakeCubes(24, _blueMat);
            _localR  = MakeCubes(24, _blueMat);
            _remoteL = MakeCubes(7,  _redMat);
            _remoteR = MakeCubes(7,  _redMat);
        }

        private void LateUpdate()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient)
            {
                SetAllActive(false);
                return;
            }

            // 本地骨骼（蓝色）
            UpdateSkeletonCubes(leftSkeleton,  _localL);
            UpdateSkeletonCubes(rightSkeleton, _localR);

            // 远端关键点（红色）
            var hm = HandsManager.Instance;
            if (hm == null)
            {
                SetActive(_remoteL, false);
                SetActive(_remoteR, false);
                return;
            }

            ulong localId = nm.LocalClientId;
            var kpL = localId == 0 ? hm.KP1L.Value : hm.KP0L.Value;
            var kpR = localId == 0 ? hm.KP1R.Value : hm.KP0R.Value;
            UpdateKeyPointCubes(_remoteL, kpL);
            UpdateKeyPointCubes(_remoteR, kpR);
        }

        private void UpdateSkeletonCubes(OVRSkeleton sk, GameObject[] cubes)
        {
            bool valid = sk != null && sk.IsDataValid && sk.Bones != null;
            int count = valid ? Mathf.Min(sk.Bones.Count, cubes.Length) : 0;

            for (int i = 0; i < cubes.Length; i++)
            {
                if (i < count)
                {
                    cubes[i].SetActive(true);
                    cubes[i].transform.position = sk.Bones[i].Transform.position;
                }
                else
                {
                    cubes[i].SetActive(false);
                }
            }
        }

        private void UpdateKeyPointCubes(GameObject[] cubes, HandsManager.HandKeyPoints kp)
        {
            bool valid = kp.wrist != Vector3.zero;
            if (!valid) { SetActive(cubes, false); return; }

            Vector3[] pts = { kp.wrist, kp.palm, kp.thumbTip, kp.indexTip, kp.middleTip, kp.ringTip, kp.pinkyTip };
            for (int i = 0; i < cubes.Length; i++)
            {
                cubes[i].SetActive(true);
                cubes[i].transform.position = pts[i];
            }
        }

        private GameObject[] MakeCubes(int count, Material mat)
        {
            var arr = new GameObject[count];
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(transform);
                go.transform.localScale = Vector3.one * jointSize;
                go.GetComponent<Renderer>().sharedMaterial = mat;
                Destroy(go.GetComponent<Collider>());
                go.SetActive(false);
                arr[i] = go;
            }
            return arr;
        }

        private static void SetActive(GameObject[] arr, bool active)
        {
            foreach (var go in arr) go.SetActive(active);
        }

        private void SetAllActive(bool active)
        {
            SetActive(_localL,  active);
            SetActive(_localR,  active);
            SetActive(_remoteL, active);
            SetActive(_remoteR, active);
        }
    }
}
