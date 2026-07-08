using UnityEngine;

public class CameraLockedBackground : MonoBehaviour
{
    public Camera targetCamera;
    public Vector2 offset;          // 想让天空稍微偏上/偏下可以调这个
    public bool lockX = true;
    public bool lockY = true;

    void LateUpdate()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        Vector3 camPos = targetCamera.transform.position;
        Vector3 pos = transform.position;

        if (lockX) pos.x = camPos.x + offset.x;
        if (lockY) pos.y = camPos.y + offset.y;

        // 2D 一般保持自己的 z（比如 0 / 10 / -10 都行）
        transform.position = pos;
    }
}
