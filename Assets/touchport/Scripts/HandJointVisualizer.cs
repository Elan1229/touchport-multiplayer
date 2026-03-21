using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Demo
{
    /// <summary>
    /// 本地手部：从 OVRSkeleton 读取所有骨骼，显示蓝色小方块（24个/手）
    /// 远端手部：从 HandsManager NetworkVariable 读取 7 个关键点，显示红色小方块 + 灰色连线
    /// </summary>
    public class HandJointVisualizer : MonoBehaviour
    {
        [SerializeField] private OVRSkeleton leftSkeleton;
        [SerializeField] private OVRSkeleton rightSkeleton;
        [SerializeField] private LocalHandsReporter reporter;
        [SerializeField] private float jointSize = 0.006f;

        private GameObject[] _localL;
        private GameObject[] _localR;
        private GameObject[] _remoteL;
        private GameObject[] _remoteR;

        // 每只远端手 6 根线：wrist-palm, palm-thumb, palm-index, palm-middle, palm-ring, palm-pinky
        private LineRenderer[] _linesL;
        private LineRenderer[] _linesR;

        private Material _blueMat;
        private Material _redMat;
        private Material _lineMat;

        // 连线索引对：(from, to) 对应 pts 数组 {wrist=0, palm=1, thumb=2, index=3, middle=4, ring=5, pinky=6}
        private static readonly (int, int)[] LineConnections = {
            (0, 1), // wrist → palm
            (1, 2), // palm → thumbTip
            (1, 3), // palm → indexTip
            (1, 4), // palm → middleTip
            (1, 5), // palm → ringTip
            (1, 6), // palm → pinkyTip
        };

        private void Awake()
        {
            _blueMat = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.2f, 0.5f, 1f) };
            _redMat  = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.3f, 0.3f) };
            _lineMat = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.6f, 0.6f, 0.6f) };

            _localL  = MakeCubes(24, _blueMat);
            _localR  = MakeCubes(24, _blueMat);
            _remoteL = MakeCubes(7, _redMat);
            _remoteR = MakeCubes(7, _redMat);

            _linesL = MakeLines(LineConnections.Length);
            _linesR = MakeLines(LineConnections.Length);
        }

        private void LateUpdate()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient)
            {
                SetAllActive(false);
                return;
            }

            bool leftIsHand  = reporter != null && reporter.LeftMode  == InputMode.Hand;
            bool rightIsHand = reporter != null && reporter.RightMode == InputMode.Hand;
            UpdateSkeletonCubes(leftSkeleton,  _localL, leftIsHand);
            UpdateSkeletonCubes(rightSkeleton, _localR, rightIsHand);

            var hm = HandsManager.Instance;
            if (hm == null)
            {
                SetActive(_remoteL, false);
                SetActive(_remoteR, false);
                SetLinesActive(_linesL, false);
                SetLinesActive(_linesR, false);
                return;
            }

            ulong localId = nm.LocalClientId;
            var kpL = localId == 0 ? hm.KP1L.Value : hm.KP0L.Value;
            var kpR = localId == 0 ? hm.KP1R.Value : hm.KP0R.Value;
            UpdateKeyPointCubes(_remoteL, _linesL, kpL);
            UpdateKeyPointCubes(_remoteR, _linesR, kpR);
        }

        private void UpdateSkeletonCubes(OVRSkeleton sk, GameObject[] cubes, bool modeIsHand)
        {
            bool valid = modeIsHand && sk != null && sk.IsDataValid && sk.Bones != null && sk.Bones.Count > 0;
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

        private void UpdateKeyPointCubes(GameObject[] cubes, LineRenderer[] lines, HandsManager.HandKeyPoints kp)
        {
            bool valid = kp.wrist != Vector3.zero;
            if (!valid)
            {
                SetActive(cubes, false);
                SetLinesActive(lines, false);
                return;
            }

            Vector3[] pts = { kp.wrist, kp.palm, kp.thumbTip, kp.indexTip, kp.middleTip, kp.ringTip, kp.pinkyTip };

            for (int i = 0; i < cubes.Length; i++)
            {
                cubes[i].SetActive(true);
                cubes[i].transform.position = pts[i];
            }

            for (int i = 0; i < lines.Length; i++)
            {
                lines[i].enabled = true;
                lines[i].SetPosition(0, pts[LineConnections[i].Item1]);
                lines[i].SetPosition(1, pts[LineConnections[i].Item2]);
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

        private LineRenderer[] MakeLines(int count)
        {
            var arr = new LineRenderer[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Line_{i}");
                go.transform.SetParent(transform);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = _lineMat;
                lr.positionCount = 2;
                lr.startWidth = 0.003f;
                lr.endWidth   = 0.003f;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.enabled = false;
                arr[i] = lr;
            }
            return arr;
        }

        private static void SetActive(GameObject[] arr, bool active)
        {
            foreach (var go in arr) go.SetActive(active);
        }

        private static void SetLinesActive(LineRenderer[] arr, bool active)
        {
            foreach (var lr in arr) lr.enabled = active;
        }

        private void SetAllActive(bool active)
        {
            SetActive(_localL,  active);
            SetActive(_localR,  active);
            SetActive(_remoteL, active);
            SetActive(_remoteR, active);
            SetLinesActive(_linesL, active);
            SetLinesActive(_linesR, active);
        }
    }
}
