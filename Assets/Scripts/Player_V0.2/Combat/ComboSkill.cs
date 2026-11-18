using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 动作充能/冷却计时器。dashSkill 与 slideSkill 各持有一份。
/// </summary>
[System.Serializable]
public class ComboSkill
{
    [Header("时间与连段配置 (基础值 · 运行时只读)")]
    public float totalCD = 2.0f;        // 动作大CD
    public float interval = 0.15f;      // 连按硬直限制
    public float comboWindow = 0.3f;    // 派生超时等待窗口期（基础值）
    public int maxCombo = 1;            // 最大连段数（基础值）

    public int currentCombo { get; private set; } = 0;

    private float cdStartTime = -10f;
    private float lastTapTime = -10f;

    private PlayerStats stats;

    // ==========================================
    // 宝石修饰器（便利贴）
    // ==========================================

    private struct GemMod
    {
        public object source;
        public int comboDelta;
        public float windowOverride;    // < 0 表示不覆写
    }

    private readonly List<GemMod> gemMods = new List<GemMod>();

    /// <summary>
    /// 贴一张宝石便利贴。
    /// </summary>
    /// <param name="source">签名，传宝石实例本身</param>
    /// <param name="comboDelta">段数增量，Echo/Relay 传 1</param>
    /// <param name="windowOverride">覆写派生窗口期。传负数表示不覆写</param>
    public void ApplyGemModifier(object source, int comboDelta = 0, float windowOverride = -1f)
    {
        if (source == null) return;

        RemoveGemModifiers(source);     // 同一来源不重复叠加
        gemMods.Add(new GemMod
        {
            source = source,
            comboDelta = comboDelta,
            windowOverride = windowOverride
        });
    }

    /// <summary>撕掉某来源贴的所有便利贴。宝石 OnUnequip 时调用</summary>
    public void RemoveGemModifiers(object source)
    {
        if (source == null) return;
        for (int i = gemMods.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(gemMods[i].source, source)) gemMods.RemoveAt(i);
        }
    }

    public void ClearGemModifiers() => gemMods.Clear();

    /// <summary>实际最大段数 = 基础值 + 所有宝石增量</summary>
    public int EffectiveMaxCombo
    {
        get
        {
            int total = maxCombo;
            for (int i = 0; i < gemMods.Count; i++) total += gemMods[i].comboDelta;
            return Mathf.Max(1, total);
        }
    }

    /// <summary>实际派生窗口期。有宝石覆写时取最大的那个（锚点存在时间越长越宽松）</summary>
    public float EffectiveComboWindow
    {
        get
        {
            float best = comboWindow;
            for (int i = 0; i < gemMods.Count; i++)
            {
                if (gemMods[i].windowOverride >= 0f && gemMods[i].windowOverride > best)
                    best = gemMods[i].windowOverride;
            }
            return best;
        }
    }

    // ==========================================
    // 冷却缩减接线（批次A）
    // ==========================================

    public void BindStats(PlayerStats playerStats) => stats = playerStats;

<<<<<<< Updated upstream
=======
    // ==========================================================
    // 【2026-11-16】冷却的基础值可以由外部提供（PlayerMovementConfig 上的 Dash / Slide Cooldown）。
    // 绑了来源之后，上面的 totalCD 不再被读取；没绑则照旧用 totalCD。
    // ==========================================================
    private System.Func<float> cooldownSource;

    public void BindCooldownSource(System.Func<float> source) => cooldownSource = source;

    private float BaseCooldown
        => cooldownSource != null ? Mathf.Max(0f, cooldownSource()) : totalCD;

>>>>>>> Stashed changes
    public float EffectiveCooldown
        => stats != null ? stats.ApplyCooldownReduction(totalCD) : totalCD;

    // ==========================================
    // 1. 超时检测
    // ==========================================
    public void UpdateTimeout()
    {
        if (currentCombo > 0 && currentCombo < EffectiveMaxCombo)
        {
            if (Time.time - cdStartTime > EffectiveComboWindow)
            {
                PlayerLog.Info("[ComboSkill] 派生超时！已自动转入大CD");
                currentCombo = 0;
            }
        }
    }

    // ==========================================
    // 2. 准入拦截
    // ==========================================
    public bool CanExecute()
    {
        if (currentCombo == 0)
        {
            return (Time.time - cdStartTime >= EffectiveCooldown);
        }
        else
        {
            return (Time.time - lastTapTime >= interval);
        }
    }

    // ==========================================
    // 3. 执行推进
    // ==========================================
    public void Execute()
    {
        lastTapTime = Time.time;
        currentCombo++;

        if (currentCombo >= EffectiveMaxCombo)
        {
            currentCombo = 0;
            cdStartTime = Time.time;
        }
    }

    // ==========================================
    // 4. 记录大CD/超时时间
    // ==========================================
    public void StartCooldownIfFirstHit(bool isFirstHit)
    {
        if (isFirstHit && currentCombo != 0)
        {
            cdStartTime = Time.time;
        }
    }

    public float GetRemainingCooldown()
    {
        if (currentCombo != 0) return 0f;
        return Mathf.Max(0f, EffectiveCooldown - (Time.time - cdStartTime));
    }

    /// <summary>本轮连段是否已经全部结束（供宝石判断锚点该不该回收）</summary>
    public bool IsComboIdle => currentCombo == 0;
}
