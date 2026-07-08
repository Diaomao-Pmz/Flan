using UnityEngine;

[System.Serializable]
public class ComboSkill
{
    [Header("时间与连段配置")]
    public float totalCD = 2.0f;        // 动作大CD
    public float interval = 0.15f;      // 连按硬直限制

    // 【修复报错】：改回你 GemProcessor 认识的名字
    public float comboWindow = 0.3f;    // 派生超时等待窗口期

    public int maxCombo = 1;            // 最大连段数（默认1，宝石可改）

    public int currentCombo { get; private set; } = 0;

    // 复用计时器：平时当大CD用，连段中当“派生超时计时器”用
    private float cdStartTime = -10f;
    private float lastTapTime = -10f;

    // ==========================================
    // 1. 超时检测 (给 PlayerStateMachine 的 Update 用)
    // ==========================================
    public void UpdateTimeout()
    {
        // 如果打了一段且没打满，检查是否超时
        if (currentCombo > 0 && currentCombo < maxCombo)
        {
            if (Time.time - cdStartTime > comboWindow)
            {
                Debug.Log("[ComboSkill] 派生超时！已自动转入大CD");
                currentCombo = 0; // 超时直接归零，下次按键强制走大CD
            }
        }
    }

    // ==========================================
    // 2. 准入拦截 (给卡带 Enter 用)
    // ==========================================
    public bool CanExecute()
    {
        if (currentCombo == 0)
        {
            // 首段：检测大CD
            return (Time.time - cdStartTime >= totalCD);
        }
        else
        {
            // 派生段：检测连按硬直（防连击宏）
            return (Time.time - lastTapTime >= interval);
        }
    }

    // ==========================================
    // 3. 执行推进 (给卡带 Enter 用)
    // ==========================================
    public void Execute()
    {
        lastTapTime = Time.time;
        currentCombo++;

        // 满段即归零（核心防白嫖逻辑）
        if (currentCombo >= maxCombo)
        {
            currentCombo = 0;
            cdStartTime = Time.time; // 满段立刻触发大CD
        }
    }

    // ==========================================
    // 4. 【完美兼容】记录大CD/超时时间 (给卡带 Exit 用)
    // ==========================================
    public void StartCooldownIfFirstHit(bool isFirstHit)
    {
        // 当状态退出时，如果是这套动作的第一段（并且还没打满）
        // 记录此时的时间，交给 UpdateTimeout() 去算是否超时
        if (isFirstHit && currentCombo != 0)
        {
            cdStartTime = Time.time;
        }
    }
}