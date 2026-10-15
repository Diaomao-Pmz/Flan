<<<<<<< Updated upstream
=======
ï»¿using System;
>>>>>>> Stashed changes
using UnityEngine;

/// <summary>æ˜Ÿå½¢ï¼šå­å¼¹æ²¿äº”è§’æ˜Ÿè½®å»“æ’å¸ƒ</summary>
[Serializable, BulletPatternInfo("æ˜Ÿå½¢ Star")]
public class StarPattern : FormationPatternBase
{
    [Header("--- æ˜Ÿå½¢ä¸“å± ---")]
    [Tooltip("ä¸€å…±å‡ é¢—å­å¼¹")]
    [Min(1)]
    public int bulletCount = 10;

    [Tooltip("å†…åŠå¾„ï¼ˆå‡¹ç‚¹ï¼‰")]
    public float rMin = 2f;

    [Tooltip("å¤–åŠå¾„ï¼ˆæ˜Ÿå°–ï¼‰")]
    public float rMax = 4f;

    protected override Vector2[] BuildShape()
    {
<<<<<<< Updated upstream
        Vector2[] spawnPositions = new Vector2[starBulletCount];

        //¸ù¾İ¹«Ê½µÃ³öÌØ¶¨½Ç¶ÈÏÂµÄxy×ø±ê
        for (int i = 0; i < starBulletCount; i++)
=======
        Vector2[] points = new Vector2[bulletCount];
        for (int i = 0; i < bulletCount; i++)
>>>>>>> Stashed changes
        {
            float angle = (i / (float)bulletCount) * 2f * Mathf.PI;
            points[i] = MathHelper.N_StarEquation(5, angle, rMin, rMax);
        }
<<<<<<< Updated upstream

        Vector3 parentSpawnPos = ctx.firePoint.position + new Vector3(0, starOffsetY, 0);

        ShapeFormationController formationCtrl = ctx.host.CreateFormationParent(spawnPositions, "BossTriangleFormation", parentSpawnPos);

        RotatingFormation rot = formationCtrl.gameObject.AddComponent<RotatingFormation>();
        float directionX = (ctx.player != null && ctx.player.position.x - ctx.host.transform.position.x >= 0) ? 1f : -1f;
        rot.direction = new Vector2(directionX, 0);
        rot.speed = starBulletSpeed;
        rot.rotationSpeed = Random.Range(0, 2) == 0 ? Random.Range(45f, 120f) : Random.Range(-120f, -45f);

        // ¡¾ĞŞ¸Ä¡¿£ºÊ¹ÓÃĞÂµÄÎ»ÔËËã¼ì²é·½ÕóÊÇ·ñĞèÒª¼ÓËÙ
        rot.enableAcceleration = enableAcceleration;
        rot.accelerationRate = accelerationRate;

        formationCtrl.StartFormation(ctx.activeTrack.formationDuration, rot);
=======
        return points;
>>>>>>> Stashed changes
    }
}
