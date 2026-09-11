/*using UnityEngine;

[CreateAssetMenu(fileName = "NewRotationPattern", menuName = "ScriptableObjects/BulletPattern/Rotation")]
public class RotationPattern : BulletPatternBase
{
    [Header("--- Rotation ---")]
    public float rotationBulletSpeed = 12f;
    public float rotationAngleIncrement = 15f;
    public bool enableAccerlation = true;
    public float accelerationRate = 5f;
    public float coef = 0.3f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        // 【多轨化关键】旋转累加器改从轨道上取。
        // 原先是 Emitter 的共享字段，两条 Rotation 轨道并发时会互相抢，角度会乱跳。
        float currentAngle = ctx.activeTrack != null ? ctx.activeTrack.angle : 0f;

        Quaternion rotation = Quaternion.Euler(0, 0, currentAngle);
        Vector2 dir = rotation * new Vector2(1, 0);
        GameObject bullet = ctx.host.SpawnProjectile(dir, ctx.firePoint.position, ctx.projectileKey, rotationBulletSpeed);

        // 【修改】：使用新的位运算检查是否需要挂载加速器
        if (enableAccerlation)
        {
            BulletAcceleration acc = bullet.GetComponent<BulletAcceleration>();
            if (acc != null)
            {
                acc.Configure(accelerationRate, coef);
            }
            else
            {
                Debug.LogWarning($"[Emitter] {bullet.name} 缺少 BulletAcceleration 组件，加速未生效。", bullet);
            }
        }

        if (ctx.activeTrack != null) ctx.activeTrack.angle += rotationAngleIncrement;
    }
}*/
using UnityEngine;

[CreateAssetMenu(fileName = "NewRotationPattern", menuName = "ScriptableObjects/BulletPattern/Rotation")]
public class RotationPattern : BulletPatternBase
{
    [Header("--- Rotation ---")]
    public float rotationBulletSpeed = 12f;

    [Tooltip("开始发射的初始角度")]
    public float initialAngle = 0f;

    [Tooltip("达到此角度后重新从初始角度开始")]
    public float endAngle = 360f;

    [Tooltip("从初始角度旋转到结束角度所需要的时间（秒）")]
    public float rotationDuration = 3f;

    public bool enableAccerlation = true;
    public float accelerationRate = 5f;
    public float coef = 0.3f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        if (ctx.activeTrack == null)
        {
            Debug.LogWarning("[RotationPattern] activeTrack 为空，无法保存旋转状态。", this);
            return;
        }

        // 第一次 Spawn：强制从 initialAngle 开始
        float currentAngle = ctx.activeTrack.angle;

        if (float.IsNaN(currentAngle))
        {
            currentAngle = initialAngle;
        }

        // 生成子弹
        Quaternion rotation = Quaternion.Euler(0, 0, currentAngle);
        Vector2 dir = rotation * new Vector2(1, 0);

        GameObject bullet = ctx.host.SpawnProjectile(
            dir,
            ctx.firePoint.position,
            ctx.projectileKey,
            rotationBulletSpeed
        );

        // 加速
        if (enableAccerlation)
        {
            BulletAcceleration acc = bullet.GetComponent<BulletAcceleration>();

            if (acc != null)
            {
                acc.Configure(accelerationRate, coef);
            }
            else
            {
                Debug.LogWarning(
                    $"[Emitter] {bullet.name} 缺少 BulletAcceleration 组件，加速未生效。",
                    bullet
                );
            }
        }

        // =========================================================
        // 计算每次 Spawn 应该增加多少角度
        // =========================================================

        if (rotationDuration <= 0f)
        {
            ctx.activeTrack.angle = initialAngle;
            return;
        }

        float totalAngle = endAngle - initialAngle;

        // 每次 Spawn 的时间间隔 / 完整旋转所需时间
        float angleIncrement = totalAngle * (DefaultInterval / rotationDuration);

        float nextAngle = currentAngle + angleIncrement;

        // =========================================================
        // 到达结束角度后，重新从初始角度开始
        // =========================================================

        if (totalAngle > 0f)
        {
            if (nextAngle >= endAngle)
            {
                nextAngle = initialAngle;
            }
        }
        else if (totalAngle < 0f)
        {
            if (nextAngle <= endAngle)
            {
                nextAngle = initialAngle;
            }
        }
        else
        {
            // initialAngle == endAngle
            nextAngle = initialAngle;
        }

        ctx.activeTrack.angle = nextAngle;
    }
}
