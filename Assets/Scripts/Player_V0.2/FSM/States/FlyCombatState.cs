using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 飞行战斗状态（交战）—— 边飞边打。
///
/// ==========================================================
/// 【规则】
///   · 左键只出 A1、右键只出 B1（由武器资产上的 Fly Attack 栏位决定）
///   · 没有连段、没有蓄力
///   · 按下立刻出招；按住就连续出招
///   · 身体不僵直：攻击期间照常八方向飞行
///   · 但动画要演完才能出下一招 —— 出招间隔 = 这一招动画的长度
///     （ComboNode 上的 recoveryTime 是"行动僵直"，飞行中不生效）
///   · 一次只演一个攻击动画；两个键都按着时左右手交替
///   · 攻击动画期间朝向锁定，免得近战判定框挥到一半翻到身后
/// ==========================================================
///
/// 【和地面 ComboState 的区别】
/// ComboState 是"站定挥剑"：进去就锁移动、打完再出来。
/// 这里是"边开车边伸手开枪"：飞行由 sm.flightMotor 照常驱动，
/// 本状态只多管一件事 —— 手上这一招演到哪了、下一招轮到谁。
///
/// 【判定框和子弹】仍由 A1/B1 动画里原有的动画事件触发
/// （TriggerAttackHitbox / SpawnProjectile），它们读的是 ComboInputBuffer.currentNode，
/// 所以出招时由 Buffer.BeginFlyAttack 把这一招登记上去即可，动画资产一行不用改。
/// 动画末尾的 OnAttackAnimationEnd 事件在这里会被状态机忽略（它只认 ComboState）。
///
/// 【离开本状态的出口】
///   打完且两个键都没按着 → FlyState
///   再按一次 Space       → FallState（路由器发起）
///   蓝耗光               → FallState
///   受击                 → HitState（状态机发起）
/// </summary>
public class FlyCombatState : PlayerStateBase
{
    /// <summary>招式没配动画剪辑时，用这个时长兜底，免得卡死在攻击里</summary>
    private const float FallbackAttackDuration = 0.3f;

    // 按下了、但还没轮到出手的招（每只手一个标记）。
    // 作用：攻击动画中点按另一个键，不会被吞，演完立刻接上。
    private readonly bool[] queued = new bool[2];

    private bool isAttacking;
    private float attackTimer;
    private InputCmd lastCmd = InputCmd.MainAttack;

    private ComboInputBuffer buffer;
    private PlayerHitDetection hitDetection;
    private PlayerWeaponEmitter weaponEmitter;

    private ComboInputBuffer Buffer
        => buffer != null ? buffer : (buffer = sm.GetComponent<ComboInputBuffer>());

    private PlayerHitDetection HitDetection
        => hitDetection != null ? hitDetection : (hitDetection = sm.GetComponent<PlayerHitDetection>());

    private PlayerWeaponEmitter WeaponEmitter
        => weaponEmitter != null ? weaponEmitter : (weaponEmitter = sm.GetComponent<PlayerWeaponEmitter>());

    public FlyCombatState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    private static int HandIndex(InputCmd cmd) => cmd == InputCmd.SubAttack ? 1 : 0;

    /// <summary>
    /// 登记一次飞行中的攻击按键。由 ComboInputBuffer.OnAttackPressedInFlight 调用，
    /// 必须在切进本状态【之前】调，这样 Enter 一进来就知道该出哪一招。
    /// </summary>
    public void QueueAttack(InputCmd cmd)
    {
        if (cmd != InputCmd.MainAttack && cmd != InputCmd.SubAttack) return;
        queued[HandIndex(cmd)] = true;
    }

    public override void Enter()
    {
        sm.flightMotor.Engage();
        isAttacking = false;
        attackTimer = 0f;

        if (!TryStartNextAttack()) sm.ChangeState(sm.flyState);
    }

    public override void Update()
    {
        if (!sm.flightMotor.TickMana(Time.deltaTime))
        {
            sm.ChangeState(sm.fallState);
            return;
        }

        // 动画还在演 → 等着。朝向不更新（锁定）
        if (isAttacking)
        {
            attackTimer -= Time.deltaTime;
            if (attackTimer > 0f) return;
            isAttacking = false;
        }

        // 演完了 → 有人还想打就接着打，没有就回巡航
        if (!TryStartNextAttack()) sm.ChangeState(sm.flyState);
    }

    public override void FixedUpdate()
    {
        // 攻击不影响飞行 —— 这就是"无行动僵直"的实现处
        sm.flightMotor.FixedTick();
    }

    public override void Exit()
    {
        sm.flightMotor.Disengage();

        // 被打断时（受击、按 Space 落下、蓝耗光）判定框和连射必须立刻停，
        // 否则人已经掉下去了，判定框还挂在半空
        HitDetection?.ForceStopHitbox();
        WeaponEmitter?.CancelBurst();

        // 飞行攻击不计入地面连段：落地后从第 1 段重新开始
        Buffer?.EndFlyAttack();

        queued[0] = false;
        queued[1] = false;
        isAttacking = false;
    }

    // ==========================================================
    // 出招
    // ==========================================================

    /// <summary>这只手想不想出招：刚按过（排队中），或者一直按着</summary>
    private bool Wants(InputCmd cmd)
    {
        if (queued[HandIndex(cmd)]) return true;

        var pc = sm.playerController;
        return cmd == InputCmd.MainAttack ? pc.isMainAttackHeld : pc.isSubAttackHeld;
    }

    /// <summary>
    /// 挑下一招并开打。两只手都想打时，优先上一招【没用】的那只手 —— 左右交替。
    /// </summary>
    /// <returns>false = 没有要打的（或招式没配），调用方应回到 FlyState</returns>
    private bool TryStartNextAttack()
    {
        InputCmd other = lastCmd == InputCmd.MainAttack ? InputCmd.SubAttack : InputCmd.MainAttack;

        if (Wants(other)) return StartAttack(other);
        if (Wants(lastCmd)) return StartAttack(lastCmd);
        return false;
    }

    private bool StartAttack(InputCmd cmd)
    {
        queued[HandIndex(cmd)] = false;
        lastCmd = cmd;

        ComboNode node = Buffer != null ? Buffer.BeginFlyAttack(cmd) : null;
        if (node == null) return false;   // 诊断日志已在 BeginFlyAttack 里打过

        // 出手前按方向键定一次朝向，之后整招锁定
        UpdateFacing(sm.playerController.moveInput.x);

        // 上一招残留的判定窗口 / 连射清掉，避免和新一招叠在一起
        HitDetection?.ForceStopHitbox();
        WeaponEmitter?.CancelBurst();

        if (string.IsNullOrEmpty(node.animName))
        {
            Debug.LogWarning(
                $"[FlyCombatState] 招式「{node.nodeName}」没有指定 Attack Clip，" +
                $"动画不会播放，按 {FallbackAttackDuration} 秒兜底计时。", sm);
            attackTimer = FallbackAttackDuration;
        }
        else
        {
            sm.animDriver.SetBase(node.animName, restart: true);
            attackTimer = node.attackClip.length > 0f ? node.attackClip.length : FallbackAttackDuration;
        }

        isAttacking = true;
        return true;
    }
}
