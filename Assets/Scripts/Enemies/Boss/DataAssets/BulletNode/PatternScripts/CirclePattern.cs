using UnityEngine;

[CreateAssetMenu(fileName = "NewCirclePattern", menuName = "ScriptableObjects/BulletPattern/Circle")]
public class CirclePattern : BulletPatternBase
{
    [Header("--- Circle ---")]
    public float circleBulletSpeed = 8f;
    public int circleBulletCount = 18;
    public float circleOffsetY = 0f; // 圆环作为整体，依然保留出生点偏移

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector3 spawnPos = ctx.firePoint.position + new Vector3(0, circleOffsetY, 0);
        float angleStep = 360f / circleBulletCount;

        for (int i = 0; i < circleBulletCount; i++)
        {
            float currentAngle = i * angleStep;
            Vector2 dir = new Vector2(Mathf.Cos(currentAngle * Mathf.Deg2Rad), Mathf.Sin(currentAngle * Mathf.Deg2Rad));
            ctx.host.SpawnProjectile(dir, spawnPos, ctx.projectileKey, circleBulletSpeed);
        }
    }
}
