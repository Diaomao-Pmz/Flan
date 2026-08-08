using UnityEngine;

/// <summary>
/// 全局屏幕边界缓存。
///
/// 【为什么需要它】
/// 原先每颗子弹每帧都调用 Camera.main.WorldToViewportPoint()，而这个方法内部要走
/// 「世界 → 相机 → 投影 → 视口」的完整矩阵变换链。同屏 300 发子弹就是每帧 300 次
/// 矩阵运算 —— 可这 300 次算出来的相机边界完全相同，纯属重复劳动。
///
/// 改法：每帧只算一次边界并缓存，子弹只需拿自己的坐标做 4 次浮点比较。
/// 判定结果完全一致，成本降一个数量级。
///
/// 注意：这里省的是 **CPU 时间**，不是内存 —— 原来的写法并不产生堆分配。
/// </summary>
public static class ScreenBounds
{
    private static Camera cam;
    private static int cachedFrame = -1;

    private static float minX, maxX, minY, maxY;
    private static float width, height;

    /// <summary>
    /// 手动指定相机。用于 Cinemachine 切换了主相机、或多相机场景的情况。
    /// 不调用则自动取 Camera.main。
    /// </summary>
    public static void SetCamera(Camera camera)
    {
        cam = camera;
        cachedFrame = -1; // 强制下次重算
    }

    /// <summary>本帧是否已经量过边界；没量过就量一次。返回是否成功拿到相机。</summary>
    private static bool Refresh()
    {
        if (cachedFrame == Time.frameCount) return cam != null;
        cachedFrame = Time.frameCount;

        // Unity 的 == 重载能识别「已销毁」的对象，切场景后会自动重新获取
        if (cam == null) cam = Camera.main;
        if (cam == null) return false;

        Vector3 camPos = cam.transform.position;

        if (cam.orthographic)
        {
            // 2D 正交相机：可视高度 = orthographicSize 的两倍
            height = cam.orthographicSize * 2f;
            width = height * cam.aspect;
        }
        else
        {
            // 透视相机兜底：按子弹所在的 z = 0 平面估算可视范围
            float dist = Mathf.Abs(camPos.z);
            height = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            width = height * cam.aspect;
        }

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        minX = camPos.x - halfW;
        maxX = camPos.x + halfW;
        minY = camPos.y - halfH;
        maxY = camPos.y + halfH;

        return true;
    }

    /// <summary>
    /// 判断世界坐标是否已越过「屏幕 + 外扩缓冲」的范围。
    ///
    /// marginRatio 沿用原先的**视口语义**：0.5 表示上下左右各外扩半个屏幕。
    /// 因此 Inspector 里现有的 margin 数值无需改动，行为与改动前完全一致。
    /// （若这里改成世界单位，0.5 会骤缩成半格，绕圈的阵型子弹会被提前回收。）
    /// </summary>
    public static bool IsOutside(Vector3 worldPos, float marginRatio)
    {
        // 拿不到相机时返回 false —— 宁可暂时不回收，也不能误杀满屏子弹
        if (!Refresh()) return false;

        float mx = width * marginRatio;
        float my = height * marginRatio;

        return worldPos.x < minX - mx || worldPos.x > maxX + mx ||
               worldPos.y < minY - my || worldPos.y > maxY + my;
    }
}