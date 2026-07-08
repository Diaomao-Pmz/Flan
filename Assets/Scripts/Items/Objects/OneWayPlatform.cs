using UnityEngine;
using System.Collections;

public class PlatformPassThrough : MonoBehaviour
{
    private Collider2D playerCollider;
    private Collider2D platformCollider;

    [Header("穿透时间设置")]
    [Tooltip("玩家掉下平台所需的等待时间，根据平台厚度和重力调节")]
    public float passThroughTime = 0.3f; // 开放到 Inspector 面板，默认改为 0.3 秒更清爽

    void Start()
    {
        // 自动获取玩家和平台自身的碰撞体
        playerCollider = GameObject.FindGameObjectWithTag("Player").GetComponent<Collider2D>();
        platformCollider = GetComponent<Collider2D>();
    }

    void Update()
    {
        // 监听按键
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Space))
        {
            // 【核心修复】：只有当玩家的碰撞体此时此刻正接触着这个平台时，才允许穿透！
            // 这样就能防止下方的平台也跟着变成空气
            if (playerCollider.IsTouching(platformCollider))
            {
                StartCoroutine(DisableCollision());
            }
        }
    }

    private IEnumerator DisableCollision()
    {
        // 1. 暂时关闭玩家与当前这一个平台的碰撞
        Physics2D.IgnoreCollision(playerCollider, platformCollider, true);

        // 2. 等待面板里设置的灵活时间
        yield return new WaitForSeconds(passThroughTime);

        // 3. 恢复碰撞
        Physics2D.IgnoreCollision(playerCollider, platformCollider, false);
    }
}