using System;
using UnityEngine;

/// <summary>三角：子弹沿三角形轮廓排布</summary>
[Serializable, BulletPatternInfo("三角 Triangle")]
public class TrianglePattern : FormationPatternBase
{
    [Header("--- 三角专属 ---")]
    [Tooltip("一共几颗子弹")]
    [Min(1)]
    public int bulletCount = 10;

    [Tooltip("内半径")]
    public float rMin = 2f;

    [Tooltip("外半径")]
    public float rMax = 4f;

    protected override Vector2[] BuildShape()
    {
        Vector2[] points = new Vector2[bulletCount];
        for (int i = 0; i < bulletCount; i++)
        {
            float angle = (i / (float)bulletCount) * 2f * Mathf.PI;
            points[i] = MathHelper.N_PolygonAngleEquation(3, angle, rMin, rMax);
        }
        return points;
    }
}
