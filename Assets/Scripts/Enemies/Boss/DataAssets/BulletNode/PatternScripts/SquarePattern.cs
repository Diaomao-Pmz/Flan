<<<<<<< Updated upstream
using UnityEngine;
using static UnityEngine.RuleTile.TilingRuleOutput;
=======
﻿using System;
using UnityEngine;
>>>>>>> Stashed changes

/// <summary>方阵：四条边均匀排布子弹</summary>
[Serializable, BulletPatternInfo("方阵 Square")]
public class SquarePattern : FormationPatternBase
{
    [Header("--- 方阵专属 ---")]
    [Tooltip("每条边几颗子弹")]
    [Min(1)]
    public int bulletsPerSide = 5;

    [Tooltip("方阵边长")]
    public float size = 3f;

    protected override Vector2[] BuildShape()
    {
        float h = size / 2f;
        Vector2[] corners = { new Vector2(h, h), new Vector2(-h, h), new Vector2(-h, -h), new Vector2(h, -h) };
        Vector2[] points = new Vector2[bulletsPerSide * 4];

        for (int edge = 0; edge < 4; edge++)
        {
            for (int i = 0; i < bulletsPerSide; i++)
            {
                float t = (float)i / bulletsPerSide;
                points[edge * bulletsPerSide + i] = Vector2.Lerp(corners[edge], corners[(edge + 1) % 4], t);
            }
        }
        return points;
    }
}
