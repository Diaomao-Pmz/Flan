using UnityEngine;

/// <summary>
/// Boss 的只读情报总线（「工牌」）。
///
/// 【为什么需要它】
/// 执行器（近战、激光、弹幕、传送）和条件对象（玩家血量低、距离过远、被逼墙角）
/// 都需要同一批资料：玩家在哪、玩家血多少、Boss 黑板上记了什么。
///
/// 若不统一供给，只有两条糟糕的路：
///  1. 给每个新类都写一遍 Init(player, bossState, ...)，参数列表越来越长；
///  2. 各自 FindObjectOfType&lt;PlayerState&gt;() —— 每次判定一次全场搜索，
///     而条件系统每帧要跑好几个条件，性能会崩。
///
/// 用 readonly struct + in 传递，零堆分配（与 DamageInfo 同理）。
/// </summary>
public readonly struct BossContext
{
    /// <summary>玩家身上受击核心的物体名。瞄准点优先取它。</summary>
    public const string HurtboxName = "Hurtbox_Core";

    public readonly Transform boss;
    public readonly Transform player;
    public readonly BossState bossState;
    public readonly PlayerState playerState;
    public readonly BossController controller;

    // 【新增】瞄准点相关。在构造时解析一次并缓存，避免每帧 Find。
    private readonly Transform playerHurtbox;
    private readonly Collider2D playerCollider;

    // 玩家状态机：读"在不在地上 / 在不在飞"，供套路条件使用
    private readonly PlayerStateMachine playerSM;

    /// <summary>
    /// 玩家的身体碰撞箱（根节点上的 BoxCollider2D，PlayerSensor 用的那个，蹲下会被压扁）。
    /// Boss 近战在 PlayerCombatConfig.meleeHitsBodyCollider 勾选时打它。
    /// </summary>
    public readonly Collider2D playerBodyCollider;

    public BossContext(BossController controller, Transform player)
    {
        this.controller = controller;
        this.boss = controller != null ? controller.transform : null;
        this.bossState = controller != null ? controller.bossState : null;

        this.player = player;
        // 玩家的碰撞体常挂在子物体上，用 InParent 兜住这种结构
        this.playerState = player != null ? player.GetComponentInParent<PlayerState>() : null;

        this.playerHurtbox = FindHurtbox(player);
        this.playerCollider = player != null ? player.GetComponentInChildren<Collider2D>() : null;
        this.playerSM = playerState != null ? playerState.GetComponent<PlayerStateMachine>() : null;
        this.playerBodyCollider = playerState != null ? playerState.GetComponent<BoxCollider2D>() : null;
    }

    /// <summary>Boss 近战是否改打玩家身体碰撞箱（读 PlayerCombatConfig 的开关；读不到时按默认"是"）</summary>
    public bool MeleeHitsPlayerBody
        => playerBodyCollider != null
           && (playerState == null || playerState.combatConfig == null || playerState.combatConfig.meleeHitsBodyCollider);

    /// <summary>玩家站在地上（找不到玩家状态机时按"在地上"算）</summary>
    public bool IsPlayerGrounded => playerSM == null || playerSM.IsGrounded();

    /// <summary>玩家正在飞行（巡航或飞行战斗）</summary>
    public bool IsPlayerFlying => playerSM != null && playerSM.IsFlying;

    /// <summary>
    /// 玩家绕到了 Boss 背后：玩家此刻站在 Boss 上一次近战 / 突进出手方向的反侧。
    /// Boss 还没出过近战 / 突进（没有朝向记录）时返回 false。
    /// 玩家几乎与 Boss 重叠时也算"不在背后"，避免左右抖动。
    /// </summary>
    public bool IsPlayerBehindBoss
    {
        get
        {
            if (controller == null) return false;
            float facing = controller.LastAttackFacing;
            if (facing == 0f) return false;

            float side = HorizontalSignToPlayer;
            return side != 0f && side != facing;
        }
    }

    private static Transform FindHurtbox(Transform player)
    {
        if (player == null) return null;

        // 先找直接子物体
        Transform direct = player.Find(HurtboxName);
        if (direct != null) return direct;

        // 再往下递归找一层以上的情况
        Transform[] all = player.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == HurtboxName) return all[i];
        }

        return null;
    }

    /// <summary>情报是否完整可用。执行器和条件在使用前应先检查。</summary>
    public bool IsValid => boss != null && player != null && bossState != null;

    /// <summary>
    /// 【瞄准点】所有攻击都应该瞄这里，而不是 player.position。
    ///
    /// 原因：玩家的轴心点通常在脚底，直接瞄 transform.position 会导致
    /// 激光、弹幕全部打在地面上。优先取 Hurtbox_Core，
    /// 没有则退回碰撞体中心，最差才用脚底。
    /// </summary>
    public Vector2 PlayerAimPoint
    {
        get
        {
            if (playerHurtbox != null) return playerHurtbox.position;
            if (playerCollider != null) return playerCollider.bounds.center;
            return player != null ? (Vector2)player.position : Vector2.zero;
        }
    }

    /// <summary>与玩家的直线距离。玩家不存在时返回 float.MaxValue（视为够不着）。</summary>
    public float DistanceToPlayer =>
        (boss != null && player != null)
            ? Vector2.Distance(boss.position, player.position)
            : float.MaxValue;

    /// <summary>玩家相对 Boss 的水平方向：右为 +1，左为 -1。拿不到玩家时返回 0。</summary>
    public float HorizontalSignToPlayer
    {
        get
        {
            if (boss == null || player == null) return 0f;
            float dx = player.position.x - boss.position.x;
            return Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : 0f;
        }
    }

    /// <summary>玩家当前血量占比 0~1。拿不到玩家时返回 1（视为满血，条件默认不触发）。</summary>
    public float PlayerHPRatio
    {
        get
        {
            if (playerState == null || playerState.health == null) return 1f;
            int max = playerState.health.maxHP;
            return max > 0 ? (float)playerState.health.currentHP / max : 1f;
        }
    }

    /// <summary>Boss 自身血量占比 0~1。</summary>
    public float BossHPRatio
    {
        get
        {
            if (bossState == null || bossState.health == null) return 1f;
            int max = bossState.health.maxHP;
            return max > 0 ? (float)bossState.health.currentHP / max : 1f;
        }
    }
}