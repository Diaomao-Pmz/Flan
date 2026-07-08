using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>
    /// 宝石效果处理器 (静态类，无状态，只提供强化逻辑)
    /// </summary>
    public static class GemActionProcessor
    {
        // 供 FSM 在进入状态、退出状态时调用
        public static void ExecuteModifier(ActionType action, GemType gem, PlayerController player)
        {
            var sm = player.stateMachine;

            // ========== 跳跃动作强化 ==========
            if (action == ActionType.Jump)
            {
                if (gem == GemType.Echo)
                {
                    // 三段跳
                    sm.maxJumps = 2;
                    sm.isJumpRelay = false;
                }
                else if (gem == GemType.Relay)
                {
                    sm.maxJumps = 1; 
                    sm.isJumpRelay = true;
                }
            }

            // ========== 冲刺动作强化 ==========
            else if (action == ActionType.Dash)
            {
                if (gem == GemType.Echo)
                {
                    // 二冲
                    sm.dashSkill.maxCombo = 2;      // 扩充上限
                    Debug.Log("[GemProcessor] Echo 宝石已激活，上限提至 2，已补发一次冲刺次数！");
                }
                else if (gem == GemType.Relay)
                {
                    sm.dashSkill.maxCombo = 2;
                    sm.dashSkill.comboWindow = sm.dashSkill.totalCD; // 锚点存在时间a
                    sm.isDashRelay = true;
                }
            }

            // ========== 滑行动作强化 ==========
            else if (action == ActionType.Slide)
            {
                if (gem == GemType.Echo)
                {
                    //二滑
                        sm.slideSkill.maxCombo = 2;
                        Debug.Log("[GemProcessor] Echo 宝石已激活，上限提至 2，已补发一次滑铲次数！");
                }
                else if (gem == GemType.Relay)
                {
                    sm.slideSkill.maxCombo = 2;
                    sm.slideSkill.comboWindow = sm.slideSkill.totalCD; // 锚点存在时间
                    sm.isSlideRelay = true;
                }
            }
        }

        // 供 FSM 结束状态时调用 (用于重置不可选中，或重置CD)
        public static void ExecuteModifierEnd(ActionType action, GemType gem, PlayerController player)
        {
            if (action == ActionType.Slide && gem == GemType.Echo)
            {

            }
        }
    }
}