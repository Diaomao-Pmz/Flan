<<<<<<< Updated upstream
=======
ï»¿using System;
>>>>>>> Stashed changes
using UnityEngine;

/// <summary>ç›´çº¿ï¼šæœç©å®¶ç„å‡†ï¼Œä¸€æ¬¡ä¸€é¢—</summary>
[Serializable, BulletPatternInfo("ç›´çº¿ç„å‡† Line")]
public class LinePattern : BulletPatternBase
{
<<<<<<< Updated upstream
    [Header("--- Line ---")]
    public float lineBulletSpeed = 15f;
    public float lineBulletScale = 1.5f;
    [Tooltip("ÂäµãYÖáÆ«ÒÆ£ºÃé×¼Íæ¼ÒÈõµãÉÏ·½(+)»òÏÂ·½(-)")]
    public float lineOffsetY = 0f;
=======
    [Header("--- ç›´çº¿ä¸“å± ---")]
    [Tooltip("ç„å‡†ç‚¹ Y åç§»ï¼šç„å‡†ç©å®¶å—å‡»ç‚¹ä¸Šæ–¹(+)æˆ–ä¸‹æ–¹(-)")]
    public float aimOffsetY = 0f;
>>>>>>> Stashed changes

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector3 spawnPos = ctx.firePoint.position;
        Vector2 dir = Vector2.left;

        if (ctx.player != null)
        {
<<<<<<< Updated upstream
            // 1. »ñÈ¡×î¾«È·µÄÍæ¼ÒÈõµã×ø±ê
            Vector3 targetPos = ctx.playerTargetPosition;

            // 2. ½«ÂäµãÆ«ÒÆ¼ÓÔÚ"×¼ĞÇ"ÉÏ£¡
            targetPos.y += lineOffsetY;

            // 3. Ç¹¿ÚÒÀÈ»ÔÚÀÏµØ·½£¬µ«Ãé×¼µÄÊÇÆ«ÒÆºóµÄÈõµã
            dir = (targetPos - ctx.firePoint.position).normalized;
        }

        // Ç¹¿ÚÎ»ÖÃ²»½øĞĞÈÎºÎÆ«ÒÆ£¬ÔÚÔ­µãÉú³É
        Vector3 spawnPos = ctx.firePoint.position;

        GameObject bullet = ctx.host.SpawnProjectile(dir, spawnPos, ctx.projectileKey, lineBulletSpeed);
        if (bullet != null)
        {
            bullet.transform.localScale = new Vector3(lineBulletScale, lineBulletScale, 1f);
        }
=======
            Vector2 target = ctx.playerTargetPosition + new Vector2(0f, aimOffsetY);
            Vector2 delta = target - (Vector2)spawnPos;
            if (delta.sqrMagnitude > 0.0001f) dir = delta.normalized;
        }

        ctx.host.SpawnBullet(this, dir, spawnPos);
>>>>>>> Stashed changes
    }
}
