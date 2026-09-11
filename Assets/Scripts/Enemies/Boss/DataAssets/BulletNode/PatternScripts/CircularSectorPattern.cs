using UnityEngine;

[CreateAssetMenu(fileName = "NewCircularSectorPattern", menuName = "ScriptableObjects/BulletPattern/CircularSector")]
public class CircularSectorPattern : BulletPatternBase
{
    [System.Serializable]
    public struct Bullet
    {
        public float speed;
    }

    [Header("--- Sector ---")]
    public Bullet[] sectorBulletPerSection;
    public int sectionCount = 18;
    public float sectorOffsetY = 0f; // 圆环作为整体，依然保留出生点偏移
    public float angleRange = 90f;
    public float radius = 0;

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector3 spawnPos = ctx.firePoint.position + new Vector3(0, sectorOffsetY, 0);
        float angleStep = angleRange / sectionCount;

        float targetAngle = Vector2.SignedAngle(new Vector2(1, 0), ctx.playerTargetPosition - (Vector2)spawnPos);

        float startAngle = targetAngle - angleRange / 2;

        for (int i = 0; i <= sectionCount; i++)
        {
            float currentAngle = startAngle + i * angleStep;
            Vector2 dir = new Vector2(Mathf.Cos(currentAngle * Mathf.Deg2Rad), Mathf.Sin(currentAngle * Mathf.Deg2Rad));

            Vector3 adjustedSpawnPos = spawnPos + (Vector3)(radius*dir);
            for (int j = 0; j < sectorBulletPerSection.Length; j++)
            {
                ctx.host.SpawnProjectile(dir, adjustedSpawnPos, ctx.projectileKey, sectorBulletPerSection[j].speed);
            }
        }
    }
}
