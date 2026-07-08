using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(SpriteRenderer))]
public class PlayerShooter : MonoBehaviour
{
    [Header("Input Actions (New Input System)")]
    public InputActionReference attackAction;   // 绑定 Attack（K）

    [Header("Projectile")]
    public GameObject projectilePrefab;         // 子弹预制体
    public Transform firePoint;                 // 发射点
    public float projectileSpeed = 12f;         // 子弹速度

    [Header("Runtime Control")]
    public bool allowInput = true;              // 回放时可设 false，禁玩家手动开火

    public event Action Fired;                  // ★ 关键：每次真的发射都会触发（用于 TimeEcho 记录）

    SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    void OnEnable()
    {
        if (attackAction != null)
        {
            attackAction.action.Enable();
            attackAction.action.performed += OnAttackPerformed;
        }
    }

    void OnDisable()
    {
        if (attackAction != null)
        {
            attackAction.action.performed -= OnAttackPerformed;
            attackAction.action.Disable();
        }
    }

    void OnAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (!allowInput) return;
        Fire();
    }

    // ★ 回放也会调用这个，确保逻辑一致
    public void Fire()
    {
        Shoot();
        Fired?.Invoke();
    }

    void Shoot()
    {
        if (projectilePrefab == null || firePoint == null)
        {
            Debug.LogWarning("PlayerShooter: projectilePrefab 或 firePoint 没绑好！");
            return;
        }

        int dir = (sr != null && sr.flipX) ? -1 : 1;
        Vector2 shootDir = new Vector2(dir, 0f);

        GameObject proj = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);

        var rb = proj.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = shootDir * projectileSpeed;
        }

        var projSr = proj.GetComponent<SpriteRenderer>();
        if (projSr != null)
        {
            projSr.flipX = (dir < 0);
        }
    }
}
