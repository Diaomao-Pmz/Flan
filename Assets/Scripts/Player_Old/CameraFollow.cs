using UnityEngine;
public class CameraFollow : MonoBehaviour
{
    [Header("追踪目标")]
    public Transform target;

    [Header("---- 功能总开关 ----")]
    public bool useSmoothDamping = true; // 阻尼（丝滑）开关
    public bool useDeadZone = true;      // 死区开关
    public bool useBounds = true;        // 关卡边界（空气墙）开关

    [Header("死区设置 (Dead Zone)")]
    public Vector2 deadZoneSize = new Vector2(1f, 1f);

    [Header("丝滑阻尼设置")]
    [Range(0f, 1f)]
    public float smoothTimeX = 0.2f;
    [Range(0f, 1f)]
    public float smoothTimeY = 0.2f;

    [Header("限制相机范围（空气墙）")]
    public Vector2 minBounds;
    public Vector2 maxBounds;

    // 底层物理计算需要的临时速度变量
    private Vector2 currentVelocity;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 currentPos = transform.position;
        Vector3 targetPos = target.position;

        float desiredX = targetPos.x;
        float desiredY = targetPos.y;

        // 1. 【死区逻辑】如果开启死区，计算相机是否需要移动
        if (useDeadZone)
        {
            desiredX = currentPos.x;
            desiredY = currentPos.y;
            float deltaX = targetPos.x - currentPos.x;
            float deltaY = targetPos.y - currentPos.y;

            if (Mathf.Abs(deltaX) > deadZoneSize.x)
                desiredX = targetPos.x - Mathf.Sign(deltaX) * deadZoneSize.x;

            if (Mathf.Abs(deltaY) > deadZoneSize.y)
                desiredY = targetPos.y - Mathf.Sign(deltaY) * deadZoneSize.y;
        }

        // 2. 【阻尼逻辑】如果开启阻尼，进行平滑追随；否则瞬间位移
        float newX = desiredX;
        float newY = desiredY;

        if (useSmoothDamping)
        {
            newX = Mathf.SmoothDamp(currentPos.x, desiredX, ref currentVelocity.x, smoothTimeX);
            newY = Mathf.SmoothDamp(currentPos.y, desiredY, ref currentVelocity.y, smoothTimeY);
        }
        else
        {
            // 如果关了阻尼，必须把历史速度清零，否则后续重新打开时会乱飞
            currentVelocity = Vector2.zero;
        }

        // 3. 【限制边界逻辑】—— 修复抽搐的终极杀招
        if (useBounds)
        {
            // 核心修复：如果相机的下一步即将越界，强行把该方向的“动能”清零！
            // 这就相当于车头撞墙的一瞬间，直接拔掉车钥匙，彻底消除互相撕扯的力。
            if (newX <= minBounds.x || newX >= maxBounds.x) currentVelocity.x = 0f;
            if (newY <= minBounds.y || newY >= maxBounds.y) currentVelocity.y = 0f;

            // 强制卡死坐标
            newX = Mathf.Clamp(newX, minBounds.x, maxBounds.x);
            newY = Mathf.Clamp(newY, minBounds.y, maxBounds.y);
        }

        // 4. 【像素对齐逻辑】适配 100 PPU，防止边缘模糊
        float pixelAlignedX = Mathf.Round(newX * 100f) / 100f;
        float pixelAlignedY = Mathf.Round(newY * 100f) / 100f;

        transform.position = new Vector3(pixelAlignedX, pixelAlignedY, currentPos.z);
    }

    // 在 Scene 窗口可视化辅助线
    private void OnDrawGizmosSelected()
    {
        if (useDeadZone)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position, new Vector3(deadZoneSize.x * 2, deadZoneSize.y * 2, 0.1f));
        }

        if (useBounds)
        {
            Gizmos.color = Color.red;
            // 防止 min 大于 max 导致画框报错，加入 Max(0) 安全保护
            float width = Mathf.Max(0, maxBounds.x - minBounds.x);
            float height = Mathf.Max(0, maxBounds.y - minBounds.y);
            Vector3 center = new Vector3((minBounds.x + maxBounds.x) / 2f, (minBounds.y + maxBounds.y) / 2f, 0f);

            Gizmos.DrawWireCube(center, new Vector3(width, height, 0.1f));
        }
    }
}

//--------------------------------------------------------------

/*public class CameraFollow : MonoBehaviour
{
    public Transform target;      // 一般拖 CameraTarget
    public float smoothTime = 0.2f;

    // 限制相机范围（根据关卡大小调整）
    public bool useBounds = false;
    public Vector2 minBounds;
    public Vector2 maxBounds;

    private Vector3 velocity = Vector3.zero;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 targetPos = new Vector3(
            target.position.x,
            target.position.y,
            transform.position.z // 保持相机原 z
        );

        // 平滑跟随
        Vector3 newPos = Vector3.SmoothDamp(
            transform.position,
            targetPos,
            ref velocity,
            smoothTime
        );

        if (useBounds)
        {
            newPos.x = Mathf.Clamp(newPos.x, minBounds.x, maxBounds.x);
            newPos.y = Mathf.Clamp(newPos.y, minBounds.y, maxBounds.y);
        }

        transform.position = newPos;
    }
}*/


