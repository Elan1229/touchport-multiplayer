// PortalClipPlaneProvider.cs
// 挂在portal那个圆盘/quad的Transform上(比如PortalRender)
// 每帧把"裁剪平面"位置和朝向存到静态变量,给下面两个Render Feature读
using UnityEngine;


[ExecuteAlways]   // 加这行,编辑器里预览(不点Play)也能让LateUpdate正常跑
public class PortalClipPlaneProvider : MonoBehaviour
{
    public static Vector3 PlanePosition;
    public static Vector3 PlaneNormal;
    public static bool Active;

    void OnEnable() => Active = true;
    void OnDisable() => Active = false;

    void LateUpdate()
    {
        PlanePosition = transform.position;
        // 朝向要指向"该保留"的那一侧(B世界更深处)
        // 跑起来发现裁剪方向反了(该留的被切/该切的留着),把这行换成 -transform.forward
        PlaneNormal = transform.forward;
    }
}