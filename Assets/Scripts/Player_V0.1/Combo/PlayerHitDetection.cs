using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

public class PlayerHitDetection : MonoBehaviour
{
    private PlayerController player;

    [Header("Debug 设置")]
    public bool showHitbox = true;
    public KeyCode toggleKey = KeyCode.F3;

    private bool isHitboxActiveThisFrame = false;

    // ==========================================
    // 新增：协程与命中黑名单
    // ==========================================
    private Coroutine activeHitboxCoroutine;
    private HashSet<Collider2D> alreadyHitEnemies = new HashSet<Collider2D>();

    void Awake()
    {
        player = GetComponent<PlayerController>();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) showHitbox = !showHitbox;
    }

    // 由动画事件 (Animation Event) 触发
    public void TriggerAttackHitbox()
    {
        ComboNode currentNode = player.inputBuffer.currentNode;
        if (currentNode == null) return;

        // 如果上一招的判定还在，强行掐断，并清空黑名单
        if (activeHitboxCoroutine != null) StopCoroutine(activeHitboxCoroutine);
        alreadyHitEnemies.Clear();

        // 启动持续伤害判定协程
        activeHitboxCoroutine = StartCoroutine(HitboxCoroutine(currentNode));
    }

    public void ForceStopHitbox()
    {
        // 1. 掐断持续伤害的协程
        if (activeHitboxCoroutine != null)
        {
            StopCoroutine(activeHitboxCoroutine);
            activeHitboxCoroutine = null;
        }

        // 2. 清空黑名单
        alreadyHitEnemies.Clear();

        // 3. 瞬间关掉可视化的实心绿框
        isHitboxActiveThisFrame = false;
    }

    private IEnumerator HitboxCoroutine(ComboNode node)
    {
        float elapsed = 0f;
        isHitboxActiveThisFrame = true; // 开启实心红框可视化

        // 核心循环：只要时间没到，就每一帧都在检测
        do
        {
            // 1. 实时更新判定框位置（这样可以支持边跑边打的持续判定）
            float dirX = (player.sr != null && player.sr.flipX) ? -1f : 1f;
            Vector2 finalOffset = new Vector2(node.hitboxOffset.x * dirX, node.hitboxOffset.y);
            Vector2 boxCenter = (Vector2)transform.position + finalOffset;

            // 2. 获取框内所有物体
            Collider2D[] hits = Physics2D.OverlapBoxAll(boxCenter, node.hitboxSize, 0f);

            // 3. 结算伤害
            foreach (var hit in hits)
            {
                // 如果是敌人，而且【不在黑名单里】
                if (hit.CompareTag("Enemy") && !alreadyHitEnemies.Contains(hit))
                {
                    alreadyHitEnemies.Add(hit); // 记入黑名单
                    Debug.Log($"砍中了碰撞体: {hit.name}</color>");

                    // 【核心修复】：必须使用 GetComponentInParent，防止砍到子物体没伤害！
                    EntityBase enemy = hit.GetComponentInParent<EntityBase>();

                    if (enemy != null)
                    {
                        Debug.Log($"成功找到父级实体 {enemy.gameObject.name}，调用 TakeDamage！</color>");
                        // 触发基类的统一扣血接口
                        enemy.TakeDamage(node.damage, DamageType.Melee);
                    }
                    else
                    {
                        Debug.LogWarning($"砍中了 {hit.name}，但在它或父节点上没找到 EntityBase！</color>");
                    }
                }
            }

            // 如果策划配的是 0 秒（瞬间伤害），只执行一次就强制退出
            if (node.hitboxDuration <= 0f) break;

            yield return null; // 歇一帧，下一帧继续检测
            elapsed += Time.deltaTime;

        } while (elapsed < node.hitboxDuration);

        // 判定时间结束，关闭实心红框
        isHitboxActiveThisFrame = false;
    }

    private void OnDrawGizmos()
    {
        if (!showHitbox || !Application.isPlaying || !isHitboxActiveThisFrame) return;

        var buffer = GetComponent<ComboInputBuffer>();
        if (buffer == null || buffer.currentNode == null) return;

        ComboNode nodeToDraw = buffer.currentNode;
        PlayerController pc = GetComponent<PlayerController>();
        float dirX = (pc != null && pc.sr != null && pc.sr.flipX) ? -1f : 1f;

        Vector2 finalOffset = new Vector2(buffer.currentNode.hitboxOffset.x * dirX, buffer.currentNode.hitboxOffset.y);
        Vector2 centerPos = (Vector2)transform.position + finalOffset;

        Gizmos.color = new Color(0f, 1f, 0f, 1f); // RGBA中的G拉满，变成鲜艳的绿色
        Gizmos.DrawWireCube(centerPos, buffer.currentNode.hitboxSize);
    }
}