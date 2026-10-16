using Assets.Scripts.Items.Bullets.BulletComponents;
using UnityEngine;

/// <summary>
/// 让子弹方向随时间做正弦摆动：
///     θ(t)  = θ0 + A · sin(ω·t + φ)
///     dθ/dt = A · ω · cos(ω·t + φ)
/// θ0 从 Enemy_Projectile.MoveDirection 读取，每个物理帧用 Rotate(dθ/dt · Δt) 做增量旋转。
/// </summary>
[RequireComponent(typeof(Enemy_Projectile))]
public class BulletSineWave : MonoBehaviour, IBulletComponent
{
    [Tooltip("摆动振幅 A（度）。30 表示左右各偏 30°。")]
    public float amplitude;

    [Tooltip("摆动频率（Hz），每秒完整摆动几次。")]
    public float frequency;

    [Tooltip("初相 φ（度）。")]
    public float phase;

    private Enemy_Projectile projectile;
    private float elapsed;     // t
    private bool initialized;

    void Awake()
    {
        projectile = GetComponent<Enemy_Projectile>();
    }

    void OnEnable()
    {
        initialized = false;
    }

    void FixedUpdate()
    {
        if (projectile.isControlledByFormation) return;

        float phaseRad = phase * Mathf.Deg2Rad;

        if (!initialized)
        {
            // 第一帧读取 θ0（此时 Setup 已经设好方向）
            Vector2 dir = projectile.MoveDirection;
            elapsed = 0f;
            initialized = true;

            // 对齐到 θ(0) = θ0 + A·sin(φ)，使摆动中心正好是 θ0
            projectile.Rotate(amplitude * Mathf.Sin(phaseRad));
        }

        float dt = Time.fixedDeltaTime;
        float omega = 2f * Mathf.PI * frequency;

        float a0 = amplitude * Mathf.Sin(omega * elapsed + phaseRad);
        float a1 = amplitude * Mathf.Sin(omega * (elapsed + dt) + phaseRad);
        projectile.Rotate(a1 - a0);

        elapsed += dt;
    }

    public void OnSpawn()
    {

    }

    public void OnDespawn()
    {
        enabled = false;
    }
}
