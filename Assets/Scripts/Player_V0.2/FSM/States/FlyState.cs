using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 飞行状态（巡航）—— 只管飞，不管打。
///
/// ==========================================================
/// 【本次重写】删掉了旧的两套进入方式
///   ✗ 长按飞行键悬停蓄力 0.7 秒才起飞（HoverChargeHandler）
///   ✗ 打完蓄力招点按飞行键跃起（PrepareLeap / 跃起阶段）
///
/// 现在只有一种：按 Space 起飞，再按一次 Space 落下。
/// 起飞/落下的开关在 PlayerCommandRouter.TryToggleFly，本状态不读飞行键。
/// ==========================================================
///
/// 【飞与打分开】
/// 飞行中按攻击键会切到 FlyCombatState（交战模式），打完再切回来。
/// 两个状态共用 sm.flightMotor 这台"发动机"，所以飞行手感和耗蓝完全一致，
/// 切换时重力不会闪回来，耗蓝零头也不会被抹掉。
///
/// 【离开飞行的出口】
///   再按一次 Space      → FallState（路由器发起）
///   蓝耗光              → FallState（本状态发起）
///   受击                → HitState（状态机发起）
/// </summary>
public class FlyState : PlayerStateBase
{
    public FlyState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        sm.flightMotor.Engage();
        sm.animDriver.SetBase(PlayerAnimHash.Fly);
    }

    public override void Update()
    {
        if (!sm.flightMotor.TickMana(Time.deltaTime))
        {
            sm.ChangeState(sm.fallState);
            return;
        }

        UpdateFacing(sm.playerController.moveInput.x);
    }

    public override void FixedUpdate()
    {
        sm.flightMotor.FixedTick();
    }

    public override void Exit()
    {
        sm.flightMotor.Disengage();
    }
}
