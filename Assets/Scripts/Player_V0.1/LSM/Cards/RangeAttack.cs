using UnityEngine;

public class RangeAttackState : IState
{
    private PlayerStateMachine sm;

    public RangeAttackState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        Debug.Log("进入了：远程施法状态");

        // 1. 核心：原地施法！瞬间清零X轴惯性，把玩家“钉”在地上（保留Y轴重力）
        sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);

        // 2. 播放施法动画 (请替换为你实际的远程攻击动画名称)
        sm.anim.Play("Flandre_Fire");
    }

    public void Update()
    {
        // 绝对的硬直！
        // 在这里，我们故意【不写】任何读取 A/D 键移动的代码。
        // 玩家按破键盘也走不动，必须乖乖把施法动作做完。

        // 我们只需要保证她还在受重力影响掉落即可（防止在空中施法时悬停）
        sm.rb.linearVelocity = new Vector2(0f, sm.rb.linearVelocity.y);
    }

    public void Exit()
    {
        Debug.Log("退出了：远程施法状态");
    }
}