using System;
using UnityEngine;

/// <summary>
/// 阵型类图案（方阵 / 三角 / 星形）的共同部分：
/// 子弹先从发射点"展开"成形状，保持阵型整体朝玩家一侧平移并自转。
/// 通用设置里的子弹速度 = 阵型移动速度；加速度作用于整个阵型。
/// </summary>
[Serializable]
public abstract class FormationPatternBase : BulletPatternBase
{
    [Header("--- 阵型通用 ---")]
    [Tooltip("出生点 Y 偏移（相对发射点）")]
    public float offsetY = 0f;

    [Tooltip("子弹从中心展开成形状用多久（秒）。0 = 瞬间成形")]
    [Min(0f)]
    public float formationDuration = 0.5f;

    [Tooltip("阵型自转速度范围（度/秒），每次随机取一个值、随机正反方向")]
    public Vector2 spinSpeedRange = new Vector2(45f, 120f);

    /// <summary>各子类给出形状上每颗子弹的局部坐标</summary>
    protected abstract Vector2[] BuildShape();

    public override void Spawn(BulletSpawnContext ctx)
    {
        Vector3 spawnPos = ctx.firePoint.position + new Vector3(0f, offsetY, 0f);
        ShapeFormationController ctrl = ctx.host.CreateFormationParent(this, BuildShape(), GetType().Name, spawnPos);

        RotatingFormation rot = ctrl.gameObject.AddComponent<RotatingFormation>();
        float directionX = ctx.playerTargetPosition.x - ctx.host.transform.position.x >= 0f ? 1f : -1f;
        rot.direction = new Vector2(directionX, 0f);
        rot.speed = bulletSpeed;

        float lo = Mathf.Min(spinSpeedRange.x, spinSpeedRange.y);
        float hi = Mathf.Max(spinSpeedRange.x, spinSpeedRange.y);
        float spin = UnityEngine.Random.Range(lo, hi);
        rot.rotationSpeed = UnityEngine.Random.value < 0.5f ? spin : -spin;

        rot.enableAcceleration = enableAcceleration;
        rot.accelerationRate = accelerationRate;

        ctrl.StartFormation(formationDuration, rot);
    }
}
