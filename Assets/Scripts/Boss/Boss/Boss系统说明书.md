# Boss 系统说明书

> 更新于 2026-10-15，对照当时 `V0.2.1` / `main` 分支的代码逐条核对过。
> **主要读者：下一个对话里的 Claude。** 也适合接手 Boss 模块的开发者。
> 分四部分：**它怎么跑** → **怎么加招式** → **接下来要做什么** → **已知小问题**。
> 仓库状态、工作方式、git 事故记录另见项目文档 `claude/交接笔记_Boss与弹幕系统.md`；弹幕的详细做法见 Claude Docs 文档《Boss 弹幕制作说明（新版）》。

---

## 0. 给 Claude 的速查

**路径**：下文所有脚本路径都相对于 `D:\Unity project\Flan\Assets\Scripts\Enemies\Boss\`（这也是本会话连接的文件夹；Player、Items 等其他文件夹需要另外申请访问）。

**用户的规则**（项目说明里写着，必须遵守）：
1. 商量改动先讲功能目的，再讲代码方向；
2. 解释架构用通俗比喻；
3. 改文件前先说明目的、等用户确认；改完列出所有文件路径（含新文件）。

**文件地图**

| 文件 | 角色 |
|---|---|
| `BossLogic/BossController.cs` | 总控：状态机、执行器注册表、事件订阅、打断、调试文字 |
| `BossLogic/BossAIDecider.cs` | 大脑：意图、单招 / 套路抽卡、位移卡池、战斗节奏、冷却 |
| `BossLogic/BossState.cs` | 黑板：`EnemyHealth`、`BossMechanic`（护盾 / 阶段 / 回盾计时）、`BossCombatMode` |
| `BossLogic/StateCard/BossMoveState.cs` | 移动状态：定意图 → 走位 → 判断出手 / 位移传送 |
| `BossLogic/StateCard/BossCombatState.cs` | 战斗状态（**类名是 `BossActionExecuter`**）：兑现意图 → 找执行器 → 等演完 |
| `BossLogic/StateCard/BossStunState.cs` | 破盾眩晕 |
| `Core/BossContext.cs` | 只读情报总线（readonly struct） |
| `Core/HitboxUtility.cs` | 伤害结算、过滤器、射线工具 |
| `DataAssets/ActionNode.cs` | 所有招式卡的基类 + `SkillPhaseTag` |
| `DataAssets/BulletNode.cs` / `MeleeNode.cs` / `DashAttackNode.cs` / `LaserNode.cs` / `TeleportNode.cs` | 五种招式卡 |
| `DataAssets/BossRoutine.cs` | 套路资产 + `RoutineCondition` / `WeightModifier` / `StepVariant` / `RoutineStep` |
| `DataAssets/BulletNode/PatternScripts/*.cs` | 弹幕图案（内嵌式） |
| `Execution/IBossActionExecutor.cs` | 执行器接口 |
| `Execution/Bullet/` | `BAE_BulletEmitter`、`EmitterTrack`、`BulletSpawnContext`、阵型类 |
| `Execution/Melee/` | `BAE_MeleeAttacker`、`MeleeSwingRunner`（近战 / 突进共用）、`BossMeleeVisual`（占位方块） |
| `Execution/Special_Bullet/BAE_LaserAttacker.cs` | 激光（光束已走对象池，key = `Laser`） |
| `Execution/Special_Melee/BAE_DashAttacker.cs` | 突进斩 |
| `Execution/Tp/BAE_Teleporter.cs` | 传送：主动 / 位移卡池 / 受击传送 + 落点安检 |
| `Editor/SubclassSelectorDrawer.cs` | 弹幕类型下拉框，**不能删** |
| `Editor/BulletNodeMigration.cs` | 一次性迁移工具，迁移已完成，**待用户删除** |
| `UI/EnemyHealthBar.cs` / `EnemyShieldBar.cs` | 血条、护盾条 |

Boss 文件夹外相关：`Items/Bullets/Enemy_Projectile.cs`（`SetLifetime` / `SetScale` / `SetKnockback` / `isControlledByFormation`）、`Core/Combat/DamageInfo.cs`、`Player_V0.2/Config/PlayerCombatConfig.cs`（`meleeHitsBodyCollider`）、`Player_V0.2/FSM/PassThroughController.cs`（冲刺 / 滑铲穿过敌人）。

---

# 第一部分：Boss 是怎么运作的

## 1.1 一句话概括

Boss = **黑板**（记数据）+ **状态机**（管流转）+ **大脑**（先想好下一招）+ **一堆执行器**（演出）。

比喻：大脑先决定"下一招打什么"（意图），状态机按这一招的需要去站位，到位了把卡交给对应的执行器去演。四者靠事件和接口连接，互不认识内部细节。

## 1.2 三层结构

```
┌─ 数据层 ─────────────────────────────────────────────┐
│ ActionNode 派生的 SO 资产   ← 招式参数                 │
│ BossRoutine 资产            ← 套路（招式按顺序串起来）    │
│ BossState / BossMechanic    ← HP、护盾、阶段、姿态       │
└──────────────────────────────────────────────────────┘
                    ↓ 读
┌─ 决策层 ─────────────────────────────────────────────┐
│ BossAIDecider   ← 定意图、抽卡、位移卡池、节奏、冷却      │
│ BossMoveState   ← 站到哪、什么时候出手、要不要闪          │
│ BossCombatState ← 兑现意图、找谁来演                     │
└──────────────────────────────────────────────────────┘
                    ↓ 派发
┌─ 执行层（BAE_ = Boss Action Executor）───────────────┐
│ BAE_BulletEmitter / BAE_MeleeAttacker / BAE_LaserAttacker │
│ BAE_DashAttacker / BAE_Teleporter                        │
└──────────────────────────────────────────────────────┘
```

## 1.3 作战姿态（由护盾决定）

| 护盾 | 姿态 `BossCombatMode` | 含义 |
|---|---|---|
| 在 | `Ranged` 远程为主 | 远程类招式权重 ×3、近战 ×1（默认，`rangedModeWeights`） |
| 碎 | `Melee` 近战为主 | 先眩晕 `stunDuration` 秒，之后近战 ×3、远程 ×1（`meleeModeWeights`） |

- 护盾：近战一刀扣 `meleeShieldDamage`（100），远程一发扣 `rangedShieldDamage`（3）。
- 回盾倒计时在 `BossMechanic.Tick` 里，**从破盾那一刻开始算**，实际回盾时间 = `max(shieldRecoverTime, stunDuration)`。
- 护盾碎时才扣真实血量；血量 ≤ `phase2Threshold` 触发二阶段。
- 姿态切换（破盾 / 回盾）会作废当前意图（`AI.OnCombatModeChanged`）。
- 招式属于哪一类（远程 / 近战）由它被放进**哪个卡池桶**决定：Bullets / SpecialBullets = 远程，Melees / SpecialMelees = 近战。

## 1.4 状态机：三个状态

| 状态 | 干什么 | 什么时候离开 |
|---|---|---|
| `BossMoveState` | 定意图 → 朝意图卡的理想距离走 → 判断出手；可触发位移传送 | 满足出手条件 → CombatState |
| `BossCombatState`（类名 `BossActionExecuter`） | `AI.SelectSkill()` 兑现意图 → 查执行器 → `yield return executor.Execute(...)` | 演完 → MoveState；套路下一步能立刻接（`HasImmediateFollowUp`）则留在本状态直接出下一招 |
| `BossStunState` | 破盾眩晕：向上击飞，停弹幕 | `stunDuration` 秒后 → MoveState（此时护盾仍碎，进入近战为主） |

状态切换统一走 `BossController.ChangeState()`（先 `Exit()` 再 `Enter()`）。

## 1.5 一次出招的完整流程

```
MoveState.Enter()
  ├ BeginPhase()：重置计时，随机一个换位时间（CombatTempo.repositionTime）
  ├ AI.PlanIntent()：定意图（见 1.6）
  └ TryMovementTeleport(false)：问位移卡池要不要闪（TooFar / TooClose / Random）

MoveState.Update() 每帧
  ├ 意图被作废了（姿态切换、受击打断、出手前校验失败…）→ 重新定意图、重新计时
  ├ 位移传送前摇中 → 原地站住
  ├ AI.IsIntentReady（在射程内且不在冷却 / 套路下一步能出 / 兜底卡）
  │    且（勾了 fireImmediatelyInRange 或 已移动满换位时间）
  │    → ChangeState(CombatState)
  ├ 不在射程内且移动超过 maxApproachTime → AI.OnApproachTimeout()：
  │    放弃意图，改抽一张射程内的卡；没有就兜底卡；再没有就重新定意图
  └ MoveTowardIntent()：距离 < 目标-0.5 就后退，> 目标+0.5 就靠近，否则站住
       后退时朝后方打射线检测撞墙 → isCornered；第一次撞墙问一次位移卡池的 Cornered 条目

CombatState.Update()
  └ DoCombat() 协程
       ① node = AI.SelectSkill()            兑现意图（null → 回 MoveState）
       ② 套路步骤有 delayBefore → 等待，之后 AI.RefreshStepChoice() 按此刻情况重选候补
       ③ executor = boss.GetExecutorFor(node)
       ④ yield return executor.Execute(node, boss.Context)
       ⑤ Finish()：套路能立刻接下一步就留在 CombatState，否则回 MoveState
```

**关键点**：
- 理想距离（`preferredDistance`）**只决定往哪走，不参与出手判断**。只要在射程内、换位时间到了就会出手，哪怕还没走到理想位置（见第三部分 3.2）。
- 出招期间 Boss 站桩：`CombatState.Enter` 把水平速度清零，执行器演完才回移动状态（见第三部分 3.1）。
- `DoCombat()` 里没有任何 `if (node is XNode)`，靠字典查表。

## 1.6 AI 决策（`BossAIDecider`）

### 定意图 `PlanIntent()`（MoveState 调用；已有意图时什么都不做）

1. **套路**：当前阶段的套路池里，按条件筛选后按权重挑一个（有 `singleInterleaveChance` 的概率跳过这一层，改出单招，让 Boss 不那么好预判）。
   筛选条件：套路的 `mode` 与当前姿态匹配、套路不在冷却、玩家距离在 `minPlayerDistance ~ maxPlayerDistance`、玩家空中状态符合 `playerAirCondition`。权重 = `baseWeight` × 情境加成。
2. **单招**：只看**阶段标签允许 + 不在冷却**的卡，**不看当前距离**——够不着就让 Boss 走过去。所以走位完全由"下一招要什么"决定。
3. **兜底卡** `fallbackNode`：不管射程直接出。建议一定配一张，否则全在冷却时会状态抖动。

### 单招权重

```
实际权重 = baseWeight × 姿态倍率（ranged / melee 两类）× 情境加成（所有满足条件的 WeightModifier 倍率相乘）
```

情境加成 `WeightModifier` = 条件 `RoutineCondition`（玩家在背后 / 面前、距离范围、玩家空中状态，全部满足才算）+ 倍率（>1 更爱出、<1 少出、0 不出）。挂在卡池条目和套路条目上。

### 出手 `SelectSkill()`（CombatState 调用）

- 兜底卡 → 直接出。
- 套路 → `AdvanceRoutine()` 推进下一步（见下）。
- 单招 → 出手前再校验一次射程、冷却、阶段标签，不通过就作废、回去重新定意图。
- 出手时 `Commit()` 记冷却。

### 套路（`BossRoutine`）

- 一个套路 = 若干步骤 `RoutineStep`，每步：`action`（任意 ActionNode）、`delayBefore`（出招前停顿）、`whenOutOfRange`（够不着时：`Approach` 先走过去 / `Skip` 跳过 / `Abort` 整个作废）、`variants`（候补招式）。
- **候补招式** `StepVariant`：从上往下找第一个"条件满足且够得着"的。
  - `Replace`：出候补，这一步算完成；
  - `Insert`：先出候补，再出原本的招；**每一步最多插入一次**，避免玩家一直站背后导致无限插入。
- 有 `delayBefore` 时，停顿结束后会再检查一次 Replace 候补（`RefreshStepChoice`），玩家停顿期间的走位也会被看到。
- 套路里的招式**不检查自己的卡片冷却**（套路有自己的冷却，从套路开始那一刻算），但出招后照常记卡片冷却，影响单招抽卡。
- 套路进行中，Boss 走位的目标是"下一步实际会出的招"（含候补）的理想距离（`PeekRoutine`）。
- 示例：`DataAssets/BossRoutine/P1_X_M1.asset` = 击退近战 → 激光；激光那一步配了候补"玩家在背后且距离 ≤ 3 → 插入一次击退"。

### 卡池条目 `PoolEntry<T>`

每个条目 = `phaseTag`（Mix / Ranged / Melee）+ `action` + `weightModifiers`。比喻："卡套"——卡本身不变，卡套上写着"哪个姿态能用"，同一张卡放进不同池子可以套不同的卡套。同一张卡被放进多个桶时，以第一次出现的类别 / 标签为准。

### 战斗节奏 `CombatTempo`（远程姿态 / 近战姿态各一套）

| 字段 | 作用 |
|---|---|
| `repositionTime` (x~y) | 两招之间至少移动多久，随机取 |
| `fireImmediatelyInRange` | 勾 = 意图一进射程立刻出手；不勾 = 还要等换位时间 |
| `maxApproachTime` | 为一张意图卡靠位的最长时间，超时换卡 |
| `moveSpeed` | 这个姿态的移动速度（`BossController` 上已经没有移动速度字段） |
| `phase2SpeedMultiplier` | 二阶段时速度再乘这个倍率 |

### 转阶段

`SwitchToPhase2()`：作废意图、换成二阶段卡池 / 套路池 / 位移卡池、清空所有冷却。`BossController.HandlePhase2` 还会停掉所有执行器、传送到中央、立刻回满护盾、回到移动状态。

## 1.7 位移与传送（`BAE_Teleporter`）

传送卡 `TeleportNode` 有三种用法，**冷却全部共用卡上的 `cooldown`**（`AI.IsOffCooldown` / `AI.StampCooldown`）：

| 用法 | 怎么触发 | 配在哪 |
|---|---|---|
| 位移卡池 | 每个移动阶段开始问一次（TooFar / TooClose / Random，按权重抽，再按 `chance` 决定闪还是走）；撞墙时问一次 Cornered（按列表顺序取第一张）。每个移动阶段最多传送一次 | `BossAIDecider` 的 Phase1 / Phase2 Movement（`MovementEntry`） |
| 受击传送 | Boss 挨打时 `OnBossHit`：按卡上的姿态、伤害类型筛选；概率从 `baseChance` 起，每挨一下没触发就加 `chanceGainPerHit`，触发后回到基础值，`chanceResetAfter` 秒没挨打也回到基础值；眩晕中不触发；`interruptAction` 勾上会打断当前招式和套路 | 卡上勾 `reactToHit`，并拖进 `BAE_Teleporter` 的 Hit Reactions 列表 |
| 主动出招 / 套路步骤 | 作为普通招式卡被抽到 | 套路步骤里（传送卡已不在出招卡池里） |

**落点类型** `TeleportTargetType`：`RandomPoint`（一 / 二阶段各一组传送点）、`BehindPlayer`（玩家背后 `behindPlayerOffset` 格）、`Center`（中央锚点）、`IntentDistance`（离玩家正好是意图理想距离的位置，位移卡池推荐用这个）。

**落点安检**：用 Boss 身体碰撞框检查不压地面 / 墙（`landingBlockLayers`）、到落点的连线不穿墙、脚下 `groundCheckDistance` 内有地面、不和玩家重叠；一侧不行换另一侧，两侧都不行就这次不传。移动用 Rigidbody 设置位置。`matchPlayerHeight` 建议不勾（勾了会跟着玩家高度传，曾导致卡进地底 / 传出地图）。

## 1.8 打断链路（重要）

```
破盾：BossMechanic.BreakShield()
  └→ OnShieldBroken → BossController.HandleShieldBroken()
       ├ AI.OnCombatModeChanged()（作废意图）
       └ ChangeState(StunState)
            └→ CombatState.Exit()：StopCoroutine(出招协程) + activeExecutor.Cancel()
            └→ StunState.Enter()

受击传送（interruptAction 勾选）：BossController.InterruptCurrentAction()
  └ AI.ClearIntent() + 若在 CombatState 则 ChangeState(MoveState)（眩晕中不处理）

转阶段 / 死亡：BossController.CancelAllExecutors()
```

- **每个执行器都必须实现 `Cancel()` 且可重入**（没在执行时调用是安全的空操作）。
- `DoCombat()` 里必须是 `yield return executor.Execute(...)`，**不是** `yield return boss.StartCoroutine(...)`；后者派生的子协程不会随父协程停止，打断时会残留动作。
- `MoveState.Exit` 会作废还在前摇的位移传送（`CancelMovementTeleport`）。

## 1.9 伤害通路

- 契约（`Core/Combat/DamageInfo.cs`）：`DamageInfo { amount, type, sourcePosition, instigator, overrideKnockback, knockbackForce }` + `IDamageable.TakeDamage(in DamageInfo)`。
- 击退默认归受击方（玩家按 `PlayerCombatConfig` 的默认力度被击飞）；招式卡勾了 `overrideKnockback` 时用卡上的 `knockbackForce`（X = 推离 Boss，Y = 向上）。弹幕通过 `Enemy_Projectile.SetKnockback` 带上，近战 / 激光通过 `HitboxUtility.ApplyDamage` 的参数带上。
- `DamageType` 分 `Melee` / `Ranged`，影响护盾扣量和受击传送的筛选。
- 近战判定：`PlayerCombatConfig.meleeHitsBodyCollider` 勾选（默认）时，判定框直接和玩家身体碰撞箱比重叠（`BossContext.MeleeHitsPlayerBody`），不依赖图层。
- 所有判定统一走 `HitboxUtility.ApplyDamage()`：找 `IDamageable`、去重、防自伤。

## 1.10 BossContext：只读情报总线

`BossController.Start()` 组装一次，`readonly struct`，用 `in` 传递，零分配。

```
ctx.boss / player / bossState / playerState / controller / playerBodyCollider
ctx.PlayerAimPoint        ← 远程瞄准点：玩家的 Hurtbox_Core（递归查找），找不到退回碰撞体中心
ctx.MeleeHitsPlayerBody   ← 近战是否打玩家身体碰撞箱
ctx.IsPlayerGrounded / IsPlayerFlying
ctx.IsPlayerBehindBoss    ← 玩家站在 Boss 上一次近战 / 突进出手方向的反侧（还没出过近战时为 false）
ctx.DistanceToPlayer / HorizontalSignToPlayer / PlayerHPRatio / BossHPRatio
```

- **远程攻击一律瞄 `ctx.PlayerAimPoint`**，不要用 `player.position`（玩家轴心在脚底）。
- Boss 的轴心在**底部中心**；近战判定框以底边中点为锚点（`hitboxOffset.y = 0` 表示贴地往上长）。

---

# 第二部分：怎么创建一个招式

## 2.1 现有的五种招式卡

| 卡 | 执行器 | 能做什么 | 关键字段 |
|---|---|---|---|
| `BulletNode` | `BAE_BulletEmitter` | 多段组合弹幕，每段内嵌一种图案 | `phases` |
| `MeleeNode` | `BAE_MeleeAttacker` | 多段挥击连招 | `swings`、`autoRangeFromHitbox` |
| `DashAttackNode` | `BAE_DashAttacker` | 冲到玩家面前再挥砍 | `dashSpeed`、`swings`、`autoStopFromHitbox` |
| `LaserNode` | `BAE_LaserAttacker` | 预警 → 光束，可追踪 | `telegraphTime`、`fireTime`、`telegraphTrackTime`、`fireTrackTime`、`trackTurnSpeed` |
| `TeleportNode` | `BAE_Teleporter` | 传送（见 1.7） | `targetType`、`teleportDelay`、受击反应字段 |

## 2.2 所有卡共有的字段（`ActionNode`）

| 字段 | 作用 | 注意 |
|---|---|---|
| `actionName` | 显示名，用于日志和调试文字 | 留空则显示资产名 |
| `baseWeight` | 抽卡权重 | 0 = 永不抽中 |
| `cooldown` | 冷却秒数 | 套路里的步骤不看冷却，但会记冷却 |
| `minCastDistance` / `maxCastDistance` | 射程 | AI 一律读 `MaxCastDistance` 属性：近战卡勾 `autoRangeFromHitbox` 时按第一段判定框最远边 + 余量自动算，手填的不生效 |
| `preferredDistance` | Boss 准备出这招时走到离玩家多远 | -1 = 射程中点；填的数会被夹在射程内 |
| `overrideKnockback` / `knockbackForce` | 命中时覆盖玩家默认击飞力度 | |
| `chargeAnimName` / `activeAnimName` / `recoverAnimName` | 前摇 / 释放 / 后摇动画名 | 留空则不切 |

突进卡的停止距离同理：勾 `autoStopFromHitbox` 时按第一段挥击判定框自动算（`EffectiveStopDistance`），保证冲到位后第一下够得着。

## 2.3 创建流程

1. **建资产**：Project 右键 → Create → ScriptableObjects → XxxNode。按类型放在 `DataAssets/` 下对应文件夹（`BulletNode/Combo`、`BulletNode/Prototype`、`MeleeNode`、`Sp_B_Node`（远程特殊）、`Sp_M_Node`（近战特殊）、`TpNode`）。
2. **填共有字段**（2.2），尤其是射程和理想距离。
3. **填专属字段**：每个字段都有 Tooltip。
4. **确认执行器挂在 Boss 上**：没挂会报 `没有执行器认领 XxxNode`。
5. **放进 `BossAIDecider`**：

```
Phase1/2 Bullets          ← BulletNode（远程类）
Phase1/2 Melees           ← MeleeNode（近战类）
Phase1/2 Special Bullets  ← 任意 ActionNode，算远程类（激光等）
Phase1/2 Special Melees   ← 任意 ActionNode，算近战类（突进斩等）
Phase1/2 Routines         ← BossRoutine（套路 + 情境加成）
Phase1/2 Movement         ← 位移卡池（MovementEntry：触发条件 + 差距区间 + 权重 + 概率 + TeleportNode）
Fallback Node             ← 兜底卡
```

每个卡池条目填 `Phase Tag` + `Action` + `Weight Modifiers`（可空）。列表按语义分桶，新增特殊技能丢进对应的桶即可，不用改 `BossAIDecider`。

**加一种全新的招式类型**：新建 `ActionNode` 派生 SO（参数）+ 实现 `IBossActionExecutor` 的 MonoBehaviour（逻辑，`NodeType` 返回新卡类型）并挂到 Boss 上，现有文件零修改。

## 2.4 弹幕怎么配

- `BulletNode.phases` 每段 = `startDelay` + `duration` + `pattern`（`[SerializeReference, SubclassSelector]`，Inspector 下拉选类型）。
- `startDelay` 全 0 = 同时发；递增 = 依次发；混着填 = 交错。整招时长 `TotalDuration` = 所有段 `startDelay + duration` 的最大值。
- 图案基类 `BulletPatternBase` 是普通 `[Serializable]` 类（**不是 ScriptableObject**），通用设置：`interval`（每段开始立刻发第一次）、`bulletSpeed`、`bulletScale`（0 = 预制体大小）、`lifetime`（0 = 不限）、`projectileKey`（留空 = 发射器默认子弹）、加速三项。
- 现有 10 种：混沌、环形、扇形、直线瞄准、随机、旋转、方阵、星形、三角、全屏直线。阵型类（方阵 / 三角 / 星形）继承 `FormationPatternBase`，只需实现 `BuildShape()`。
- 写新图案：`[Serializable, BulletPatternInfo("显示名")]` + 继承 `BulletPatternBase` + 实现 `Spawn(ctx)`，发子弹用 `ctx.host.SpawnBullet(this, 方向, 位置[, 速度])`，下拉框自动出现。不要 `using UnityEditor`。
- 详细说明（给队友写的）见 Claude Docs《Boss 弹幕制作说明（新版）》。

## 2.5 调试

- **头顶调试文字**：`bossStatusText` 显示 `[意图] xxx` / 正在出的招（套路会带 `[套路名]` 前缀）；`movementStatusText` 显示 `[移动] 靠近 / 后退 当前距离 → 目标距离`、`到位，等待出手`、`传送：xxx`、`出招中`。中文字体已加到 TMP 全局 Fallback。
- **Console 关键日志**：
  - `[AI] 意图：xxx（射程 a~b，理想距离 c，当前 d）`
  - `[AI] 出牌: xxx`
  - `[AI] 意图 xxx 靠位超时，放弃` → 射程或节奏配置可能有问题
  - `[AI] 开始套路 / 第 N 步插入候补 / 改出候补 / 套路完成 / 套路中断`
  - `[AI] 位移（TooFar，差距 x）：xxx`
  - `[BAE_Teleporter] 受击传送触发：xxx`；`debugTeleport` 勾选时会打印绕背传送的落点详情
  - `[Emitter] 卡片 xxx 第 N 段没有选弹幕类型，已跳过`
- **Gizmos**：近战判定框橙红、突进橙黄、激光红。
- 想单独测一张卡：临时把卡池里只留它。

---

# 第三部分：接下来要做的两件事

两件都**还没开始**，做之前按用户规则先讲功能、等确认。

## 3.1 边移动边开火

**目标**：Boss 放弹幕时不再站桩，可以一边发射一边移动（例如边后退边放），远程阶段更有压迫感。

**现状**：
- `CombatState.Enter` 把水平速度清零；`BAE_BulletEmitter.Execute` 先 `StartCombo` 铺开轨道，再 `WaitForSeconds(TotalDuration)`，等完才回移动状态。
- 弹幕轨道（`EmitterTrack`）在发射器自己的 Update 里计时，本身不依赖 Boss 站着不动。

**可选方向**（未定，需和用户商量）：
- A. 给 `BulletNode` 加开关「边移动边释放」：勾上时 `Execute` 铺开轨道后立刻返回，发射器自己在 `TotalDuration` 后停止，Boss 回到移动状态。要处理：放完之前不能再抽另一张弹幕卡（否则 `StartCombo` 会清掉正在放的轨道）；破盾打断时仍要能停掉（`StunState` 里现在恰好有一行 `BulletEmitter.StopAttack()`）。
- B. 在 `CombatState` 执行弹幕卡期间复用 `MoveState` 的走位逻辑（把 `MoveTowardIntent` 的移动部分抽成可共用的方法）。

## 3.2 到达理想距离才出手

**目标**：Boss 先走到理想距离附近再出手，而不是一进射程、换位时间一到就开火。
（已知现象：射程 4~20、理想距离 7.5 的招式，Boss 在 4.5 格就开火，一步都没后退。）

**现状**：`BossMoveState.Update` 的出手条件是 `AI.IsIntentReady`（在射程内）且（`fireImmediatelyInRange` 或换位时间已过）；理想距离只决定 `GetOptimalEngagementDistance()` 返回的移动目标。

**方向**：
- 在 `CombatTempo` 加 `requirePreferredDistance`（开关）和 `preferredTolerance`（容差，格）。
- 出手条件再加：`|当前距离 - GetOptimalEngagementDistance()| <= 容差`。
- **必须有保底**：被墙挡住或玩家追身时可能永远到不了位。现在 `maxApproachTime` 超时只在"不在射程内"时触发 `OnApproachTimeout`（换卡）；新条件下应改为"在射程内但没到位且超时 → 直接出手"，而不是放弃这张卡。

---

# 附录：已知的小问题（2026-10-15 核对）

不影响运行，改相关代码时顺手清掉：

1. **`BossCombatState.cs` 里的类名还是 `BossActionExecuter`**（文件名已改）。改名要同步 `BossController` 里的属性类型和 `new`。
2. **`BossStunState.Enter()` 里直接调了 `boss.BulletEmitter.StopAttack()`**。`CombatState.Exit()` 已经会调 `activeExecutor.Cancel()`，这行是多余的；语义上应换成 `boss.CancelAllExecutors()`。（做 3.1 方向 A 时要注意它。）
3. **`Editor/BulletNodeMigration.cs` 还在**，迁移已完成，可以删（用户在 Unity 里删，`SubclassSelectorDrawer.cs` 不能删）。
4. **遗留枚举**：`BulletNode.cs` 里的 `BossAttackType`、`BAE_BulletEmitter.cs` 里的 `BossAttackFlags` 已无实际用途，只为兼容保留。
5. **`BAE_LaserAttacker.cs` 有一行多余的 `using Unity.VisualScripting;`**；`Execution/Laser/LaserBeamView.cs` 只是空的 `IPoolable` 壳。
6. **`BAE_Teleporter.debugTeleport` 默认是 true**，每次绕背传送都打印详情，稳定后可关。
7. **`BossMechanic.comboExtendDuration` 是预留字段，没有生效。**
8. **`BossAIDecider.canAttack` 在 Boss 文件夹内已无调用方**（旧流程遗留；其他文件夹未核对）。
9. **`FullScreenLinePattern` 用的是挂在发射器上的协程**：Boss 在子弹"定住"期间被销毁的话，这批子弹会一直定在原地。目前 Boss 不会中途销毁，暂不处理。
10. **`fallbackNode` 建议配一张**：全在冷却时没有兜底会在移动 / 战斗之间空转。（场景里现在配没配未核对。）
11. **玩家子弹是否已走对象池未核对**（Player 文件夹不在本会话连接范围内）。旧记录是 `Player_Projectile` / `Player_HomingProjectile` 仍用 `Instantiate` + `Destroy`。
