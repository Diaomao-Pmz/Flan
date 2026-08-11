# Boss 系统说明书

> 面向接手 Boss 模块的开发者。
> 分三部分：**它怎么跑** → **怎么加招式** → **你接下来要做什么**。

---

# 第一部分：Boss 是怎么运作的

## 1.1 一句话概括

Boss = **黑板**（记数据）+ **状态机**（管流转）+ **大脑**（抽卡）+ **一堆执行器**（演出）。

四者互不认识对方的内部细节，靠事件和接口连接。

## 1.2 三层解耦

```
┌─ 数据层 ────────────────────────────────────┐
│  ActionNode (SO 资产)   ← 招式的全部参数     │
│  BossState (黑板)       ← HP/护盾/死角/阶段  │
└──────────────────────────────────────────────┘
                  ↓ 读
┌─ 决策层 ────────────────────────────────────┐
│  BossAIDecider          ← 抽哪张牌           │
│  BossCombatState        ← 找谁来演           │
│  BossMoveState          ← 站到哪             │
└──────────────────────────────────────────────┘
                  ↓ 派发
┌─ 执行层 ────────────────────────────────────┐
│  BAE_BulletEmitter / BAE_Teleporter          │
│  BAE_MeleeAttacker / BAE_LaserAttacker       │
│  BAE_DashAttacker                            │
└──────────────────────────────────────────────┘
```

`BAE_` = **B**oss **A**ction **E**xecutor 的前缀，所有执行器都用它。

## 1.3 状态机：只有三个状态

| 状态 | 干什么 | 什么时候离开 |
|---|---|---|
| `BossMoveState` | 风筝走位，靠近/拉开到目标距离 | 计时 1~2.5 秒后 → CombatState |
| `BossCombatState` | 抽卡 → 找执行器 → 等它演完 | 演完 → MoveState |
| `BossStunState` | 破盾硬直，被击飞 | 计时结束 → 恢复护盾 → CombatState |

状态切换统一走 `BossController.ChangeState()`，它保证先 `Exit()` 再 `Enter()`。

## 1.4 一次出招的完整时序

```
MoveState.Enter()
  └→ 问 AI：我该保持多远？ → GetOptimalEngagementDistance()
  └→ 朝那个距离走 1~2.5 秒

MoveState.Update() 计时结束
  └→ ChangeState(CombatState)

CombatState.Update()  发现 AI.canAttack
  └→ 启动协程 DoCombat()
       ① AI.SelectSkill()  抽一张 ActionNode
       ② boss.GetExecutorFor(node)  查表找执行器
       ③ yield return executor.Execute(node, boss.Context)
       ④ 演完 → ChangeState(MoveState)
```

**关键点：`DoCombat()` 里没有任何 `if (node is XNode)` 分支。** 它靠 `Dictionary<Type, IBossActionExecutor>` 查表，完全不认识具体技能。

## 1.5 执行器是怎么被找到的

`BossController.Awake()` 里：

```csharp
foreach (IBossActionExecutor e in GetComponents<IBossActionExecutor>())
    executors[e.NodeType] = e;
```

**挂在 Boss 身上就会被自动发现**，不需要在任何地方登记。每个执行器声明自己认领哪种卡：

```csharp
public System.Type NodeType => typeof(LaserNode);
```

一种 Node 类型只能被一个执行器认领，重复认领会报错。

## 1.6 AI 的五层决策

`BossAIDecider.SelectSkill()` 从上往下走：

| 层 | 做什么 | 备注 |
|---|---|---|
| ① 死角逃生 | 被逼墙角就从 `emergencyTeleports` 强行抽一张 | 无视权重和距离 |
| ② 兑现预约 | 上轮想用但够不着的牌，靠位成功就打 | 见下方 |
| ③ 常规抽卡 | 距离合适 + 不在冷却，按 `baseWeight` 加权抽 | 主路径 |
| ④ 够不着就预约 | 无视距离抽一张记为意图，返回 null 去靠位 | 见下方 |
| ⑤ 兜底 | 全在冷却时用 `fallbackNode` | 防状态抖动 |

### 「意图预约」是什么

**这是让 Boss 能主动冲上去近战的机制**，很重要。

原先的流程是「先移动到某个距离 → 再看哪些卡在射程内」。结果招式池里一旦混了远近两种手段，够不着的那一类**永远抽不中**——Boss 停在 8 米外，近战卡直接被距离过滤掉。

现在改成：抽到够不着的牌**不丢弃**，记成 `PendingNode`，`SelectSkill()` 返回 null。`MoveState` 问「我该站多远」时，Decider 回答**预约那张牌的理想距离**，Boss 就主动靠过去。下一轮进战斗状态时距离够了，预约兑现。

`maxPendingAttempts`（默认 3）是放弃阈值——连续 N 轮靠不到位就放弃，避免玩家站在够不到的地方时 Boss 原地罚站。

## 1.7 打断链路（重要）

Boss 出招到一半被破盾或转阶段时：

```
BossMechanic.BreakShield()
  └→ OnShieldBroken 事件
       └→ BossController.HandleShieldBroken()
            └→ ChangeState(StunState)
                 └→ CombatState.Exit()          ← 先执行
                      ├ StopCoroutine(攻击协程)
                      └ activeExecutor.Cancel()  ← 通知执行器收摊
                 └→ StunState.Enter()
```

**每个执行器都必须实现 `Cancel()`**，且必须可重入（没在执行时调用应当是安全的空操作）。

`Cancel()` 里该做什么因技能而异：

- 近战 / 冲刺：清判定黑名单、把冲刺速度归零
- 激光：**真的隐藏光束**（否则 Boss 躺下了激光还挂在屏幕上）
- 弹幕：`StopAttack()` 清掉所有发射轨道

另外注意 `BossCombatState.DoCombat()` 里是：

```csharp
yield return executor.Execute(node, boss.Context);   // ✅
```

**不是** `yield return boss.StartCoroutine(executor.Execute(...))`。后者会派生独立子协程，父协程被 `StopCoroutine` 时子协程**不会**被连带停止，打断时会残留一个还在跑的动作。

## 1.8 伤害通路

敌我双方共用一份契约（`Core/Combat/DamageInfo.cs`）：

```csharp
readonly struct DamageInfo { amount, type, sourcePosition, instigator }
interface IDamageable { void TakeDamage(in DamageInfo info); }
```

**载荷里没有击退力度，这是有意的。** 击退力度归受击方所有（玩家的力度在 `PlayerStateMachine.hitKnockbackForce`），攻击方只提供 `sourcePosition`——「我从哪来」。受击方自己算方向。这样霸体、击退抗性、方向修正全部集中在受击方一处，不用去改每一种攻击。

`DamageType` 分 `Melee` / `Ranged`，Boss 护盾对两者扣血量不同（`meleeShieldDamage` 100 vs `rangedShieldDamage` 3），且**只有近战会触发防反传送**。

所有判定统一走 `HitboxUtility.ApplyDamage()`，它负责找 `IDamageable`、去重、防自伤。

## 1.9 BossContext：只读情报总线

`BossController.Start()` 组装一次，之后所有执行器共用：

```csharp
ctx.boss / ctx.player / ctx.bossState / ctx.playerState / ctx.controller
ctx.PlayerAimPoint          ← 瞄准点，优先取玩家的 Hurtbox_Core
ctx.DistanceToPlayer
ctx.HorizontalSignToPlayer  ← 玩家在左还是右（±1）
ctx.PlayerHPRatio / ctx.BossHPRatio
```

**攻击一律瞄 `ctx.PlayerAimPoint`，不要用 `player.position`**——玩家轴心点在脚底，直接瞄会全打在地面上。

它是 `readonly struct`，用 `in` 传递，零堆分配。

---

# 第二部分：怎么创建一个招式

## 2.1 现有的五种 Node

| Node | 执行器 | 能做什么 | 关键字段 |
|---|---|---|---|
| `BulletNode` | `BAE_BulletEmitter` | 七种弹幕形态，支持多段组合 | `phases` |
| `MeleeNode` | `BAE_MeleeAttacker` | 多段挥击连招 | `swings` |
| `TeleportNode` | `BAE_Teleporter` | 随机点 / 绕背 / 回中央 | `targetType` |
| `LaserNode` | `BAE_LaserAttacker` | 固定方向激光 | `telegraphTime` / `beamSprite` |
| `DashAttackNode` | `BAE_DashAttacker` | 冲到玩家面前再挥砍 | `dashSpeed` + `swings` |

## 2.2 所有 Node 共有的字段（来自 `ActionNode` 基类）

| 字段 | 作用 | 填错的后果 |
|---|---|---|
| `actionName` | 显示名，会出现在 Console 日志和状态文字上 | 留空则显示资产文件名 |
| `baseWeight` | 抽卡权重，越大越常出 | 填 0 = 永不抽中 |
| `cooldown` | 冷却秒数 | 填 0 = 无冷却 |
| `minCastDistance` / `maxCastDistance` | 允许释放的距离区间 | **最容易填错的一项，见下** |
| `chargeAnimName` / `activeAnimName` / `recoverAnimName` | 前摇 / 释放 / 后摇动画名 | 留空则不切动画 |

### ⚠️ 距离区间填错会导致「招式永远不出」

`maxCastDistance` 基类默认是 **15**。近战卡如果不改，就会在 12 米外也被判定为「合法」——Boss 因此**没有冲上来的动机**，会站在原地挥空气。

建议值：

| 类型 | min | max |
|---|---|---|
| 近战 | 0 | 2.5 |
| 突进斩 | 3 | 12 |
| 普通弹幕 | 2 | 15 |
| 激光 | 4 | 20 |
| 传送 | 0 | 999 |

另外 `GetOptimalEngagementDistance()` 取的是 `(min + max) / 2` —— 这是 Boss 会站的位置。填 `0 / 15` 就是站在 7.5 米，这对近战毫无意义。

## 2.3 创建流程（六步）

**① 建资产**
Project 里右键 → `Create → ScriptableObjects → XxxNode`

**② 填基类字段**
参考 2.2 的表，尤其是距离区间。

**③ 填专属字段**
每种 Node 的字段都有 Tooltip，鼠标悬停能看说明。

**④ 确认执行器已挂在 Boss 上**
比如新建的是 `LaserNode`，Boss 身上必须有 `BAE_LaserAttacker`。
没挂的话运行时 Console 会报：`没有执行器认领 LaserNode`。

**⑤ 塞进卡池**
选中 Boss → `BossAIDecider` → 找到对应的列表：

```
Phase 1 Bullets          ← BulletNode
Phase 1 Melees           ← MeleeNode
Phase 1 Teleports        ← TeleportNode
Phase 1 Special Bullets  ← 远程系特殊技（激光等）
Phase 1 Special Melees   ← 近战系特殊技（突进斩等）
（Phase 2 同上五项）
Emergency Teleports      ← 死角保命牌，只放 TeleportNode
```

> **列表是按语义分的，不是按类型分的。**
> `Special Bullets` / `Special Melees` 的类型是 `List<ActionNode>`，意味着以后新增任何特殊技能，丢进对应的桶就行，**不需要改 BossAIDecider 一行代码**。

**⑥ 调试时看 Console**
三条日志能定位绝大多数问题：

```
[AI] 出牌: XXX（LaserNode）                     ← 抽中了
[AI] 想用 XXX 但距离不合适（当前 8.0，需要 0~2.5），先去靠位
[AI] 预约 XXX 靠位失败 3 次，放弃。             ← 距离配置有问题
```

Scene 视图打开 Gizmos 能看到判定框（近战橙红 / 冲刺橙黄 / 激光红），调数值主要靠它。

## 2.4 组合弹幕怎么配

`BulletNode.phases` 是一个列表，每段一条独立发射轨道：

| 字段 | 含义 |
|---|---|
| `type` | 七种形态之一 |
| `startDelay` | 相对组合开始的延迟 |
| `duration` | 这一段持续多久 |
| `intervalOverride` | 发射间隔，填 0 用 Emitter 上的默认值 |
| `formationDuration` | 阵型托管时长，仅 Square/Triangle/Star 生效 |

- `startDelay` **全填 0** → 并发（直线 + 环形同时喷）
- `startDelay` **递增** → 序列（直线 → 环形 → 五角星）
- 混着填 → 交错叠加

`TotalDuration` 自动算成所有段 `startDelay + duration` 的最大值，不用手填。

**旧卡兼容**：`phases` 留空时会回退到 `AttackName` + `attackDuration` 的单形态模式。

---

# 第三部分：接下来要做的两件事

两个任务**代码上完全不重叠**，可以并行。真正的冲突风险在 Unity 资产（见 3.3）。

## 3.1 任务 A：激光池化

### 要解决什么

现在场景里有一个手拖进 Inspector 的 `LaserBeam` 物体，`BAE_LaserAttacker` 持有它的 `SpriteRenderer` 引用。

**只有一块画布，就只能同时画一条激光。** 想做扇形五连、依次扫射、双色交叉，全都做不到。

弹幕系统早就解决了同样的问题——子弹从来不是手拖的固定物体，而是**向对象池借的**。激光应该照做。

顺带收益：场景里那个手拖的引用消失，少一处「忘拖就不显示」的坑。

### 架构方向

```
现在：  BAE_LaserAttacker → [SerializeField] SpriteRenderer beamRenderer  （固定一个）
改后：  BAE_LaserAttacker → ObjectPoolManager.Get("LaserBeam")            （借 N 个）
```

**建议新建一个 `LaserBeamView` 组件**挂在光束预制体上，实现 `IPoolable`，把「设位置/角度/尺寸/颜色/贴图」这些绘制逻辑从执行器里搬进去。执行器只负责借、配、还。

这样做的好处：执行器变薄，而且之后做多轨时，一条轨道对应一个 `LaserBeamView` 实例，天然隔离。

`OnDespawn()` 里记得把 `localScale` 和 `rotation` 重置——参考 `Enemy_Projectile.OnDespawn()` 的做法，它有出厂快照。

### 涉及的文件

| 文件 | 操作 |
|---|---|
| `Execution/Laser/LaserBeamView.cs` | 🆕 新建 |
| `Execution/Laser/BAE_LaserAttacker.cs` | ✏️ 改：去掉 SerializeField，改走对象池 |
| `LaserNode.cs` | 可能不用改 |

**不要动**：`ObjectPoolManager.cs`、`PooledObject.cs`、`IPoolable.cs`。池的机制已经完备，只需要注册一个新 key。

### Unity 侧要做的

1. 把现在场景里的 `LaserBeam` 做成预制体
2. 挂上 `LaserBeamView`
3. 在 `ObjectPool` 物体的 `poolConfigs` 里加一条：key = `"LaserBeam"`，prewarm 少量（比如 4）

`PooledObject` 组件**不用手动加**，池在创建实例时会自动补上。

### 验收标准

- 场景里不再有手拖的 LaserBeam 引用，执行器 Inspector 上那个槽位消失
- 激光表现与改动前完全一致
- 破盾打断时光束立刻消失且**归还到池**（不是 SetActive(false) 就完事）
- 连放十次激光后，池的数量守恒（没有泄漏也没有暴涨）

### 之后（不在本次范围）

池化做完之后，多轨激光就是纯增量改造了——`LaserNode` 加 `phases`（每段含 `angleOffset` / `startDelay` / `beamSprite`），执行器改成持有 `List<LaserBeamTrack>`。结构和 `EmitterTrack` 完全同构，可以直接参考。

**注意**：暂时**不要**尝试把激光轨道和 `EmitterTrack` 抽成公共泛型。两者 phase 里装的东西差别很大（一个是弹幕形态+间隔，一个是角度偏转+宽度），强行抽象会得到一个谁都不好用的中间层。等第三个系统也需要这套机制时再说。

## 3.2 任务 B：弹幕参数 SO 化

### 要解决什么

打开 `BAE_BulletEmitter` 的 Inspector，会看到约 30 个形态参数：

```
lineShootInterval / lineBulletSpeed / lineBulletScale / lineOffsetY
circleShootInterval / circleBulletSpeed / circleBulletCount / circleOffsetY
triangleBulletCount / tri_rmin / tri_rmax / triangleOffsetY
...
```

**这些参数是全局的，不是每张卡的。**

所以所有用 Circle 的卡，`circleBulletCount` 永远是同一个值。**做不出**「小环形」和「大环形」两张不同的卡。多轨化之后更明显：一张组合卡里两段都是 Circle，想让第一段稀疏、第二段密集？做不到，共用同一套参数。

这也违反了项目军规里的「不要在执行脚本里硬编码数值」。

### 架构方向

**每种形态一个 SO 类，参数跟着形态走。**

```
abstract BulletPatternBase : ScriptableObject
{
    abstract float DefaultInterval { get; }
    abstract void Spawn(BulletSpawnContext ctx);
}
  ├ LinePattern     { speed, scale, offsetY, interval }
  ├ CirclePattern   { bulletCount, speed, offsetY, interval }
  ├ TrianglePattern { bulletCount, rmin, rmax, offsetY, interval }
  ├ StarPattern     { ... }
  ├ SquarePattern   { ... }
  ├ RotationPattern { angleIncrement, speed, interval }
  └ RandomPattern   { speed, interval }
```

`BulletPhase` 里的 `BossAttackType type` 换成 `BulletPatternBase pattern`。

**两个 switch 会一起消失**：`ExecutePattern()` 和 `GetDefaultInterval()`。加第八种弹幕形态时不用改 Emitter。

### `BulletSpawnContext` 是什么

Pattern 是 ScriptableObject 资产，**它不在场景里，拿不到 firePoint、对象池、玩家位置**。所以需要一个上下文对象把这些递进去：

```
struct BulletSpawnContext
{
    Transform firePoint;
    Transform player;
    string projectileKey;
    EmitterTrack track;      // 拿 angle / formationDuration
    BAE_BulletEmitter host;  // 需要建阵型父物体时用
}
```

这和 `BossContext` 是同一个套路——**资产不自己去找东西，由外部供料**。

### 涉及的文件

| 文件 | 操作 |
|---|---|
| `DataAssets/Patterns/BulletPatternBase.cs` | 🆕 |
| `DataAssets/Patterns/LinePattern.cs` 等七个 | 🆕 |
| `Execution/Bullet/BulletSpawnContext.cs` | 🆕 |
| `BulletNode.cs` | ✏️ `BulletPhase.type` → `pattern` |
| `BAE_BulletEmitter.cs` | ✏️ 大改：七个 Spawn 方法搬进各 Pattern |
| `EmitterTrack.cs` | ✏️ 小改：`type` → `pattern` |

**不要动**：`FormationCore.cs`、`ShapeFormationController.cs`、`Enemy_Projectile.cs`、`BulletAcceleration.cs`。阵型和子弹本身的逻辑不在本次范围。

### 迁移注意

1. **`BossAttackFlags`（加速多选框）要一起重做。** 它是按枚举位做的，形态变成 SO 之后没法再用位掩码。建议把 `enableAcceleration` / `accelerationRate` / `coef` 直接下放到每个 Pattern 资产上——反正加速本来就是逐形态的事。

2. **先建七个 Pattern 资产、把现有 Inspector 数值抄进去，再改代码。** 数值抄漏了很难发现（表现只是"手感变了"）。建议先截图存档现在的 Emitter Inspector。

3. **现有 6 张 BulletNode 卡要重新指向 Pattern 资产。** 这一步没法自动迁移。

### 验收标准

- 现有 6 张卡表现与改动前一致
- 能做出两张同为 Circle 但 `bulletCount` 不同的卡
- 一张组合卡里两段 Circle 用不同密度
- `BAE_BulletEmitter` 的 Inspector 上不再有形态参数（只剩 `projectilePrefab` / `projectileKey` / `firePoint`）

## 3.3 ⚠️ 协作注意：真正的冲突风险在资产，不在代码

两个任务的 `.cs` 文件完全不重叠，Git 上不会打架。

**但两者都要改 Unity 资产**：

| 任务 | 会动的资产 |
|---|---|
| A（激光池化） | `ObjectPool` 物体（加池配置）、Boss 预制体（去掉手拖引用）、新建 LaserBeam 预制体 |
| B（弹幕 SO） | Boss 预制体（Emitter 参数清空）、6 张 BulletNode 资产 |

`.prefab` / `.unity` / `.asset` 是**几乎无法合并**的文件。两人同时改 Boss 预制体，后提交的人会覆盖前一个人的改动，而且 Git 不会提示冲突。

**建议**：

1. 约定**同一时间只有一个人改 Boss 预制体**，改完立刻提交并通知
2. 或者用 Unity 的 **Force Text** 序列化模式 + `.gitattributes` 标记为 binary，至少让冲突显式暴露
3. 代码先行——两人各自把 `.cs` 写完提交，最后再排队做 Unity 侧配置

---

# 附录：已知的小问题

以下都不影响运行，但改的时候顺手清掉：

1. **`BossCombatState.cs` 里的类名还叫 `BossActionExecuter`。** 文件名已改，类名没改。改的话记得同步 `BossController` 里的两处（字段类型 + `new`）。

2. **`BossStunState.Enter()` 里直接调了 `boss.BulletEmitter.StopAttack()`。** 这是旧代码残留——`CombatState.Exit()` 已经会调 `activeExecutor.Cancel()`，这行是多余的。而且它只管弹幕，语义上应该换成 `boss.CancelAllExecutors()`。

3. **`BossMechanic.TakeDamage()` 里有一堆调试 `Debug.Log`，还带着没闭合的 `</color>` 标签。** 战斗中每次挨打都会打印，正式版要清掉。

4. **`BAE_BulletEmitter.GetPlayerTargetPosition()` 是重复实现。** `BossContext.PlayerAimPoint` 已经做了同样的事（而且支持嵌套查找 Hurtbox）。Emitter 的 `Execute()` 能拿到 ctx，可以直接换掉。

5. **`Enemy_Projectile.lifeTime` 是死字段。** 目前只有「飞出屏幕」一条回收路径。

6. **玩家子弹尚未接入对象池。** `Player_Projectile` / `Player_HomingProjectile` 仍在用 `Instantiate` + `Destroy`。敌方子弹已池化，玩家子弹是漏网的。

7. **`fallbackNode` 建议配一张。** 现在是 `None`。激光配了长冷却之后，「所有牌都在冷却」的时刻迟早出现，届时 Boss 会在 Move/Combat 之间空转抖动。
