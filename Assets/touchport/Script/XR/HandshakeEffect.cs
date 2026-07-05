using Unity.Netcode;
using UnityEngine;
using UnityEngine.VFX;

public class HandshakeEffect : MonoBehaviour
{
    [SerializeField] VisualEffect vfx1;
    [SerializeField] VisualEffect vfx2;
    // PlayerManager playerManager;

    // void Awake()
    // {
    //     playerManager = FindFirstObjectByType<PlayerManager>();
    //     if (playerManager == null)
    //     {
    //         Debug.LogError($"[{this.GetType()}] Can't find PlayerManager.");
    //     }
    // }

    // void Update()
    // {
    //     if (GameManager.Instance.GameMode == GameMode.Undefined)
    //         return;

    //     if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null || NetworkManager.Singleton.LocalClient.PlayerObject == null || NetworkManager.Singleton.LocalClient.PlayerObject.IsSpawned == false)
    //         return;

    //     //Player player = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Player>();

    //     Player player = playerManager.ActivePlayer;
    //     if (player == null || player.IsSpawned == false)
    //         return;

    //     //if (GameManager.Instance.IsRolePlayer(player) == false)
    //     //    return;


    //     bool is_handshaking = player.handshakeFrameCount.Value > 0 && player.handshakeTargetPosition.Value != Vector3.zero;

    //     if(is_handshaking)
    //     {
    //         float alpha = player.handshakeFrameCount.Value / playerManager.HandshakeFrameThreshold;
    //         vfx1.SetBool("IsOn", true);
    //         vfx1.SetVector3("StartPoint", player.Hand.position);
    //         vfx1.SetVector3("EndPoint", player.handshakeTargetPosition.Value);
    //         vfx1.SetFloat("Alpha", alpha);

    //         vfx2.SetBool("IsOn", true);
    //         vfx2.SetVector3("EndPoint", player.Hand.position);
    //         vfx2.SetVector3("StartPoint", player.handshakeTargetPosition.Value);
    //         vfx2.SetBool("ReverseColor", true);
    //         vfx2.SetFloat("Alpha", alpha);
    //     }
    //     else
    //     {
    //         vfx1.SetBool("IsOn", false);
    //         vfx2.SetBool("IsOn", false);
    //     }
        
    // }

    //-------- single player test
   

    [SerializeField] Transform handA; // 自己的手,或者随便一个测试物体
    [SerializeField] Transform handB; // 对方的手,或者另一个测试物体

    [SerializeField] float handshakeDistance = 0.3f; // 两手距离小于这个值才算"握手中"
    [SerializeField] float handshakeFrameThreshold = 60f; // 从0渐变到1需要多少帧,对应原脚本的HandshakeFrameThreshold

    float handshakeFrameCount = 0;

    void Update()
    {
        // 没拖Transform进去就直接关掉两个特效,不报错
        if (handA == null || handB == null)
        {
            vfx1.SetBool("IsOn", false);
            vfx2.SetBool("IsOn", false);
            return;
        }

        float dist = Vector3.Distance(handA.position, handB.position);
        bool is_handshaking = dist < handshakeDistance;

        // 用距离代替原来的NetworkVariable判断,靠近就累计帧数,离开就清零
        if (is_handshaking)
            handshakeFrameCount = Mathf.Min(handshakeFrameCount + 1, handshakeFrameThreshold);
        else
            handshakeFrameCount = 0;

        if (handshakeFrameCount > 0)
        {
            float alpha = handshakeFrameCount / handshakeFrameThreshold;

            vfx1.SetBool("IsOn", true);
            vfx1.SetVector3("StartPoint", handA.position);
            vfx1.SetVector3("EndPoint", handB.position);
            vfx1.SetFloat("Alpha", alpha);

            vfx2.SetBool("IsOn", true);
            vfx2.SetVector3("StartPoint", handB.position);
            vfx2.SetVector3("EndPoint", handA.position);
            vfx2.SetBool("ReverseColor", true);
            vfx2.SetFloat("Alpha", alpha);
        }
        else
        {
            vfx1.SetBool("IsOn", false);
            vfx2.SetBool("IsOn", false);
        }
    }
}

