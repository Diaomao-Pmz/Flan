using System;
using UnityEngine;

/// <summary>星形：子弹沿五角星轮廓排布</summary>
[Serializable, BulletPatternInfo("星形 Star")]
public class StarPattern : FormationPatternBase
{
    [Header("--- 星形专属 ---")]
    [Tooltip("一共几颗子弹")]
    [Min(1)]
    public int bulletCount = 10;

    [Tooltip("内半径（凹点）")]
    public float rMin = 2f;

    [Tooltip("外半径（星尖）")]
    public float rMax = 4f;

    protected override Vector2[] BuildShape()
    {
        Vector2[] points = new Vector2[bulletCount];
        for (int i = 0; i < bulletCount; i++)
        {
            float angle = (i / (float)bulletCount) * 2f * Mathf.PI;
            points[i] = MathHelper.N_StarEquation(5, angle, rMin, rMax);
        }
        return points;
    }
}
