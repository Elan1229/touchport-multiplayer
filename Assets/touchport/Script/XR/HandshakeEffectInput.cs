using UnityEngine;

/// <summary>
/// 把左右手食指中间关节(Hand_Index2)的世界坐标喂给两个目标 Transform，
/// 用来当 HandshakeEffect 的 handA / handB 输入，不改 HandshakeEffect 本身。
/// </summary>
public class HandshakeEffectInput : MonoBehaviour
{
    [SerializeField] private OVRSkeleton leftSkeleton;
    [SerializeField] private OVRSkeleton rightSkeleton;

    [Tooltip("拖给 HandshakeEffect 的 Hand A，本脚本每帧把它的位置设为左手食指中间关节位置")]
    [SerializeField] private Transform handATarget;
    [Tooltip("拖给 HandshakeEffect 的 Hand B，本脚本每帧把它的位置设为右手食指中间关节位置")]
    [SerializeField] private Transform handBTarget;

    private void LateUpdate()
    {
        UpdateTarget(leftSkeleton, handATarget);
        UpdateTarget(rightSkeleton, handBTarget);
    }

    private static void UpdateTarget(OVRSkeleton skeleton, Transform target)
    {
        if (skeleton == null || target == null) return;
        if (!skeleton.IsDataValid || skeleton.Bones == null) return;

        int index = (int)OVRSkeleton.BoneId.Hand_Index2;
        if (skeleton.Bones.Count <= index) return;

        target.position = skeleton.Bones[index].Transform.position;
    }
}
