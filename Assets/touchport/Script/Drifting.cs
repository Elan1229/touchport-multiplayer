using UnityEngine;



public class Drifting : MonoBehaviour
{
    [Header("Transform")]
    public float moveSpeed = 0.3f;
    public float moveRange = 0.2f;

    [Header("Rotation")]
    public float rotateSpeed = 0.5f;
    public float rotateRange = 15f;

    private Vector3 startPos;
    private Quaternion startRot;
    private float offsetX, offsetY, offsetZ;

    void Start()
    {
        startPos = transform.position;
        startRot = transform.rotation;

        // 每个物体随机偏移，不会所有东西同步漂
        offsetX = Random.Range(0f, 100f);
        offsetY = Random.Range(0f, 100f);
        offsetZ = Random.Range(0f, 100f);
    }

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        IsPaused = true;
    }

    public void Resume()
    {
        startPos = transform.position;
        startRot = transform.rotation;
        offsetX = Random.Range(0f, 100f);
        offsetY = Random.Range(0f, 100f);
        offsetZ = Random.Range(0f, 100f);
        IsPaused = false;
    }

    void Update()
    {
        if (IsPaused) return;

        // 位置漂移
        float x = Mathf.Sin((Time.time + offsetX) * moveSpeed) * moveRange;
        float y = Mathf.Sin((Time.time + offsetY) * moveSpeed * 0.7f) * moveRange;
        float z = Mathf.Sin((Time.time + offsetZ) * moveSpeed * 0.5f) * moveRange * 0.5f;
        transform.position = startPos + new Vector3(x, y, z);

        // 轻微旋转
        float tilt = Mathf.Sin((Time.time + offsetX) * rotateSpeed) * rotateRange;
        transform.rotation = startRot * Quaternion.Euler(0, 0, tilt);
    }
}
