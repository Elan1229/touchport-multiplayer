using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMove : MonoBehaviour
{
    // first person view
    public float moveSpeed = 5f;       // 移动速度
    public float lookSensitivity = 2f; // 鼠标灵敏度
    public float verticalSpeed = 3f;   // 上下移动速度

    private float rotationX = 0f;      // 用于垂直旋转

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        // 获取键盘输入
        float horizontal = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float vertical   = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);

        // 基础移动（前后左右）
        Vector3 move = transform.right * horizontal + transform.forward * vertical;

        // 上下移动（Q/E）
        if (kb.qKey.isPressed) move += Vector3.down * verticalSpeed;
        if (kb.eKey.isPressed) move += Vector3.up * verticalSpeed;

        transform.position += move * moveSpeed * Time.deltaTime;

        // 鼠标控制视角
        var mouseDelta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
        float mouseX = mouseDelta.x * lookSensitivity * Time.deltaTime;
        float mouseY = mouseDelta.y * lookSensitivity * Time.deltaTime;

        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f); // 限制垂直角度

        transform.localRotation = Quaternion.Euler(rotationX, transform.localEulerAngles.y + mouseX, 0f);
    }
}
