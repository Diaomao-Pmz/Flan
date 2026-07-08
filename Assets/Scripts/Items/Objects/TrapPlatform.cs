using System.Collections;
using UnityEngine;

public class DisappearingPlatform : MonoBehaviour
{
    [Header("陷阱核心机制")]
    [Tooltip("【关键开关】勾选此项，平台彻底销毁；不勾选，平台会重生")]
    public bool isOneTime = false;

    [Tooltip("玩家踩上去后，平台多久碎裂/消失？(秒)")]
    public float disappearDelay = 0.5f;

    [Tooltip("（仅在不勾选一次性时有效）平台消失后，多久重新长出来？(秒)")]
    public float respawnDelay = 2.0f;

    // 缓存组件
    private SpriteRenderer spriteRenderer;
    private Collider2D platformCollider;
    private bool isTriggered = false;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        platformCollider = GetComponent<Collider2D>();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        // 检查是否是主角踩到了平台上方
        if (collision.gameObject.CompareTag("Player") && !isTriggered)
        {
            if (collision.contacts[0].normal.y < -0.5f)
            {
                StartCoroutine(DisappearSequence());
            }
        }
    }

    private IEnumerator DisappearSequence()
    {
        isTriggered = true;

        // 【等待期】：玩家踩上去了，给一点反应时间
        yield return new WaitForSeconds(disappearDelay);

        // ———— 核心分歧点 ————

        if (isOneTime)
        {
            // 如果策划勾选了“一次性”：
            // 直接施展终极毁灭魔法，把平台从内存里抹除
            Destroy(gameObject);

            // 极其重要的一句代码：直接退出当前协程，后面的重生代码再也不执行了！
            yield break;
        }
        else
        {
            // 如果策划没勾选“一次性”（即允许重生）：
            // 隐身并失去碰撞体
            spriteRenderer.enabled = false;
            platformCollider.enabled = false;

            // 等待冷却时间
            yield return new WaitForSeconds(respawnDelay);

            // 满血复活，重置状态
            spriteRenderer.enabled = true;
            platformCollider.enabled = true;
            isTriggered = false;
        }
    }
}