using UnityEngine;

namespace Flandre.CombatSystem
{
    // 玩家的所有抽象输入指令
    public enum InputCmd
    {
        LightAttack,
        HeavyAttack,
        Jump,
        Dash,
        Slide,
        Shoot // 以后加入射击系统直接在这里加
    }

    // 你之前代码里已经有的枚举，也可以统统搬到这里来集中管理！
    public enum ActionType
    {
        Jump,
        Dash,
        Slide
    }

    public enum GemType
    {
        None,
        Echo,
        Relay,
        Shield,
        Pulse
    }
}