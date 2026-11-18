using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 飞行战斗状态（交战）—— 边飞边打。
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
    /// 登记一次飞行中的攻击按键。由 ComboInputBuffer 在飞行中点按时调用，
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

            // 飞行攻击也按 ComboNode 的三段时长给动画调速，演多久就按总时长算
            AttackInstance inst = Buffer.FlyInstance;
            sm.animDriver.SetAttackOwner(inst);
            if (inst != null && inst.duration > 0f) attackTimer = inst.duration;
            else attackTimer = node.attackClip != null && node.attackClip.length > 0f ? node.attackClip.length : FallbackAttackDuration;
        }

        isAttacking = true;
        return true;
    }
}
