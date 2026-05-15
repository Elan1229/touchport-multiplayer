using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class DreamCore : MonoBehaviour
{
    public List<string> slots = new List<string>();

    [Header("Slots")]
    [SerializeField] private int maxSlots = 3;

    [Header("Absorb")]
    [SerializeField] private Key absorbKey = Key.S;

    [Header("Expand")]
    [SerializeField] private bool expandWhenPlacedInAvatar = true;
    [SerializeField] private string avatarForeheadTag = "Head";

    [Header("Trigger Sensor")]
    [SerializeField] private Collider triggerSensor;
    [SerializeField] private bool autoFindTriggerSensor = true;

    [Header("Element UI")]
    [SerializeField] private GameObject elementUIRoot;
    [SerializeField] private TMP_Text elementUILabel;
    [SerializeField] private float elementUIDuration = 5f;

    private DreamElement currentElement;
    private float elementUIHideTime;
    private bool avatarExpandRequested;

    // 启动时解析拾取范围 trigger，DreamCore 自己只负责交互和 slots。
    private void Awake()
    {
        if (triggerSensor == null && autoFindTriggerSensor)
        {
            foreach (var childCollider in GetComponentsInChildren<Collider>(true))
            {
                if (childCollider.transform == transform || !childCollider.isTrigger) continue;

                triggerSensor = childCollider;
                break;
            }
        }

        if (triggerSensor == null)
            Debug.LogWarning("[DreamCore] Trigger sensor is not assigned. Drag a child trigger collider into Trigger Sensor, or enable Auto Find Trigger Sensor.", this);
        else if (!triggerSensor.isTrigger)
            Debug.LogWarning("[DreamCore] Trigger Sensor collider must have Is Trigger enabled.", triggerSensor);

        if (elementUIRoot != null)
            elementUIRoot.SetActive(false);
    }

    // 物体被禁用时隐藏 DreamCore 身上的唯一提示 UI。
    private void OnDisable()
    {
        currentElement = null;

        if (elementUIRoot != null)
            elementUIRoot.SetActive(false);
    }

    // 每帧检查吸收按键；只有 5 秒提示窗口内记录下来的词条可以被吸收。
    private void Update()
    {
        if (elementUIHideTime > 0f && Time.unscaledTime >= elementUIHideTime)
        {
            currentElement = null;
            elementUIHideTime = 0f;

            if (elementUIRoot != null)
                elementUIRoot.SetActive(false);
        }

        var keyboard = Keyboard.current;
        if (currentElement != null && keyboard != null && absorbKey != Key.None && keyboard[absorbKey].wasPressedThisFrame)
            TryAbsorb(currentElement.elementID);
    }

    // sensor 进入别的 Collider 时，读取对方 DreamElement，并在没有当前提示时显示 5 秒。
    private void OnTriggerEnter(Collider other)
    {
        var element = other.GetComponentInParent<DreamElement>();
        if (element != null)
        {
            currentElement = element;

            if (elementUILabel != null)
                elementUILabel.text = element.DisplayName;

            if (elementUIRoot != null)
                elementUIRoot.SetActive(true);

            Debug.Log($"[DreamCore] Triggered by {other.gameObject.name}: {element.elementType} / {element.elementID} / {element.DisplayName}", element);
            elementUIHideTime = Time.unscaledTime + Mathf.Max(0f, elementUIDuration);
        }

        // sensor 碰到 Head 或 Head 子物体时，只记录“请求展开”，由 DreamManager 读取后切世界。
        if (expandWhenPlacedInAvatar && !string.IsNullOrWhiteSpace(avatarForeheadTag))
        {
            var current = other.transform;
            while (current != null)
            {
                if (current.gameObject.tag == avatarForeheadTag)
                {
                    avatarExpandRequested = true;
                    break;
                }

                current = current.parent;
            }
        }
    }

    // 保留普通碰撞触发展开：如果主球碰到 Head collider，也可以请求展开。
    private void OnCollisionEnter(Collision collision)
    {
        if (!expandWhenPlacedInAvatar || string.IsNullOrWhiteSpace(avatarForeheadTag))
            return;

        var current = collision.collider.transform;
        while (current != null)
        {
            if (current.gameObject.tag == avatarForeheadTag)
            {
                avatarExpandRequested = true;
                break;
            }

            current = current.parent;
        }
    }

    // 尝试把词条 ID 加入最多 3 个 slots；重复不加，满了顶出最旧词条。
    public bool TryAbsorb(string elementID)
    {
        if (string.IsNullOrWhiteSpace(elementID))
            return false;

        if (slots.Contains(elementID))
            return false;

        while (slots.Count >= Mathf.Max(1, maxSlots))
            slots.RemoveAt(0);

        slots.Add(elementID);
        Debug.Log($"[DreamCore] Absorbed {elementID}. Slots: {string.Join(", ", slots)}", this);
        return true;
    }

    // DreamManager 读取这个标记；读到 true 后会消费掉，避免同一次触碰反复展开。
    public bool ConsumeAvatarExpandRequest()
    {
        if (!avatarExpandRequested)
            return false;

        avatarExpandRequested = false;
        return true;
    }

}
