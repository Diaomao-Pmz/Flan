using UnityEngine;

namespace Flandre.CombatSystem.Gems
{
    /// <summary>
    /// 【回响 Echo】
    ///
    /// 动作槽：该动作多一段（三段跳 / 二冲 / 二滑）
    /// 主动槽：全局被动（减CD、连招宽恕更宽松）+ 主动技能「记录并重放 1 秒前的状态」
    ///
    /// 注意这里体现了统一宝石体系的价值：
    /// 同一颗宝石插在不同槽位效果不同，但只有一个资产、一个类。
    /// </summary>
    [CreateAssetMenu(fileName = "Gem_Echo", menuName = "Flandre/Gems/Echo (回响)")]
    public class EchoGemSO : GemSO
    {
        [Header("动作槽效果")]
        [Tooltip("装在动作槽时，该动作额外增加几段")]
        public int extraUses = 1;

        [Header("主动槽被动")]
        [Tooltip("全局冷却缩减，0.15 = 减15%")]
        public float cooldownReductionBonus = 0.15f;

        [Tooltip("连招宽恕期额外宽容度（秒）")]
        public float comboWindowBonus = 0.2f;

        [Header("主动槽技能")]
        [Tooltip("记录状态后，可在多少秒内重放")]
        public float recordDuration = 1.0f;

        public override GemRuntime CreateRuntime() => new EchoGemRuntime();
    }

    public class EchoGemRuntime : GemRuntime
    {
        private EchoGemSO Cfg => Definition as EchoGemSO;

        // ---- 主动技能的运行时状态（放在草稿本里，不污染 SO 资产）----
        private bool isRecording = false;
        private float recordTimer = 0f;
        private Vector3 savedPosition;
        private Vector2 savedVelocity;
        private int savedFacingDirection;

        public override void OnEquip()
        {
            var cfg = Cfg;
            if (cfg == null) return;

            if (ctx.IsActionSlot)
            {
                // ===== 动作槽：该动作多一段 =====
                if (Slot == GemSlot.Jump)
                {
                    // 跳跃用 maxJumps 计数，贴在属性面板上
                    ctx.stats.maxJumps.AddModifier(
                        StatModifier.Flat(cfg.extraUses, this));
                    Log($"跳跃段数 +{cfg.extraUses}");
                }
                else
                {
                    // 冲刺/滑铲用 ComboSkill 计数，贴在充能器上
                    Skill?.ApplyGemModifier(this, comboDelta: cfg.extraUses);
                    Log($"动作段数 +{cfg.extraUses}");
                }
            }
            else
            {
                // ===== 主动槽：注册全局被动 =====
                ctx.stats.cooldownReduction.AddModifier(
                    StatModifier.Flat(cfg.cooldownReductionBonus, this));

                ctx.stats.comboWindowTolerance.AddModifier(
                    StatModifier.Flat(cfg.comboWindowBonus, this));

                Log($"全局被动已激活：减CD {cfg.cooldownReductionBonus:P0}，连招宽恕 +{cfg.comboWindowBonus}秒");
            }
        }

        public override void OnUnequip()
        {
            // 基类会撕掉属性面板上所有本宝石签名的便利贴
            base.OnUnequip();

            // 充能器上的便利贴要自己撕
            Skill?.RemoveGemModifiers(this);

            // 清理运行时状态，别把脏数据留给下一次装备
            isRecording = false;
            recordTimer = 0f;

            Log("已卸载，全部修饰已还原");
        }

        public override void UpdateRuntime(float deltaTime)
        {
            if (!isRecording) return;

            recordTimer -= deltaTime;
            if (recordTimer <= 0f)
            {
                isRecording = false;
                Log("记录消散");
            }
        }

        // ==========================================
        // 主动技能：记录 / 重放
        // ==========================================
        public override void ExecuteActive()
        {
            var cfg = Cfg;
            if (cfg == null || ctx.controller == null) return;

            if (!isRecording)
            {
                savedPosition = ctx.transform.position;
                savedVelocity = ctx.rb.linearVelocity;
                savedFacingDirection = ctx.controller.facingDirection;

                isRecording = true;
                recordTimer = cfg.recordDuration;

                Log($"第一段：已记录状态，{cfg.recordDuration} 秒内可重放");
            }
            else
            {
                ctx.transform.position = savedPosition;
                ctx.rb.linearVelocity = savedVelocity;
                ctx.controller.SetFacingDirection(savedFacingDirection);

                isRecording = false;
                Log("第二段：动作再现！");
            }
        }
    }
}
