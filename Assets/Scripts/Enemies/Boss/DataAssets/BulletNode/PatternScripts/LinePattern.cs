using UnityEngine;

[CreateAssetMenu(fileName = "NewLinePattern", menuName = "ScriptableObjects/BulletPattern/Line")]
public class LinePattern : BulletPatternBase
{
    [Header("--- Line ---")]
    public float lineBulletSpeed = 15f;
    public float lineBulletScale = 1.5f;
    [Tooltip("落点Y轴偏移：瞄准玩家弱点上方(+)或下方(-)")]
    public float lineOffsetY = 0f;

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector2 dir = Vector2.left;

        if (ctx.player != null)
        {
            // 1. 获取最精确的玩家弱点坐标
            Vector3 targetPos = ctx.playerTargetPosition;

            // 2. 将落点偏移加在"准星"上！
            targetPos.y += lineOffsetY;

            // 3. 枪口依然在老地方，但瞄准的是偏移后的弱点
            dir = (targetPos - ctx.firePoint.position).normalized;
        }

        // 枪口位置不进行任何偏移，在原点生成
        Vector3 spawnPos = ctx.firePoint.position;

        GameObject bullet = ctx.host.SpawnProjectile(dir, spawnPos, ctx.projectileKey, lineBulletSpeed);
        if (bullet != null)
        {
            bullet.transform.localScale = new Vector3(lineBulletScale, lineBulletScale, 1f);
        }
    }
}
