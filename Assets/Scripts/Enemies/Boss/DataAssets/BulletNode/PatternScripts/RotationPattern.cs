using UnityEngine;

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
}
