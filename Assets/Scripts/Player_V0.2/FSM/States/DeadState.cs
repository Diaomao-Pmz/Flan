using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 【死亡状态】—— 骨架已备好，当前【未接线】。
///
/// 按你的要求，测试阶段维持「0血不死」，所以：
///   PlayerHealth.OnPlayerDeath        —— 已写好但整段注释
///   PlayerStateMachine.deadState      —— 已声明但注释，未实例化
///   PlayerStateMachine.HandlePlayerDeath —— 已写好但注释
///
/// 想启用时，把上述三处的 TODO 注释解除即可，本文件不用改。
///
/// 目前的行为设计（可按需调整）：
///   进入时锁死输入、清空速度、播死亡动画、请求无敌避免尸体继续挨打
/// </summary>
public class DeadState : PlayerStateBase
{
    private static readonly object DeathToken = new object();

    public DeadState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    public override void Enter()
    {
        Debug.Log("[DeadState] 玩家死亡");

        // 清空预输入缓存，防止死后还兑现指令
        sm.commandRouter?.ClearBuffer();

        // 停止一切位移
        sm.rb.linearVelocity = Vector2.zero;

        // 尸体不该继续被打
        sm.playerState?.health.RequestUntargetable(DeathToken);

        sm.anim.Play(PlayerAnimHash.Death);   // TODO: 有死亡动画后换成 Flandre_Death
    }

    public override void Update()
    {
        // 死亡状态不响应任何输入。
        // 复活/重开局的流程以后接在这里。
    }

    public override void Exit()
    {
        // 复活时释放无敌
        sm.playerState?.health.ReleaseUntargetable(DeathToken);
    }
}
