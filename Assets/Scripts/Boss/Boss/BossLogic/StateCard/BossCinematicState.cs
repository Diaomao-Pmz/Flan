using UnityEngine;

/// <summary>
/// 【演出状态】Boss 被演出导演（BossCinematicDirector）接管时所处的状态。
/// 这个状态自己什么都不做：AI 不做决定、不走位，Boss 只听演出卡的步骤。
/// 演完由导演把 Boss 切回移动状态。
/// 如果在演出途中被别的逻辑切走（例如死亡），离开时会让导演中止演出、还原一切。
/// </summary>
public class BossCinematicState : IState
{
    private readonly BossController boss;

    public BossCinematicState(BossController bc)
    {
        boss = bc;
    }

    public void Enter()
    {
        boss.StopHorizontal();
    }

    public void Update() { }

    public void FixedUpdate() { }

    public void Exit()
    {
        if (boss.Cinematics != null && boss.Cinematics.IsPlaying)
            boss.Cinematics.Abort("Boss 离开了演出状态");
    }
}
