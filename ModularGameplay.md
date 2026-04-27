# ModularGameplay 系统详解

## 1. 类比：智能家居插座

买一座精装房，墙上有很多插座。你不知道将来会插什么电器——可能是台灯、风扇、空气净化器——但只要插座在那里，电器就能即插即用。

**ModularGameplayActors 就是给你的游戏角色、控制器、GameMode 这些核心类"预装插座"。** 每个类在初始化时向系统注册："我身上有空插座，可以对接到我的组件。" 这样一来，GameFeature 不需要修改你的类，就能往上面"插"技能系统、输入映射、AI 行为等组件。

这个过程就像 USB 热插拔——类自己不需要知道以后会被插上什么，但它承诺"我有插口，随时可插"。

---

## 2. 架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                  UGameFrameworkComponentManager                  │
│                  (UE Engine 中央协调器)                             │
│                                                                  │
│  "我知道所有注册过的Actor，以及谁在监听它们"                         │
│                                                                  │
│  AddGameFrameworkComponentReceiver(Actor)  ← 注册一个"插座"Actor   │
│  RemoveGameFrameworkComponentReceiver(Actor) ← 注销               │
│  SendGameFrameworkComponentExtensionEvent(Actor, Event) ← 发信号  │
│  AddExtensionHandler(ActorClass, Delegate) ← 监听某类Actor         │
│  AddComponentRequest(ActorClass, ComponentType) ← 请求添加组件     │
└──────┬──────────────┬──────────────┬──────────────┬──────────────┘
       │ 注册          │ 注册          │ 注册          │ 注册
       ▼              ▼              ▼              ▼
┌────────────┐ ┌───────────┐ ┌────────────┐ ┌──────────────┐
│ Character  │ │ Player    │ │ GameState  │ │ AIController │
│ & Pawn     │ │ Controller│ │            │ │              │
├────────────┤ ├───────────┤ ├────────────┤ ├──────────────┤
│PreInit:    │ │PreInit:   │ │PreInit:    │ │PreInit:      │
│ Register() │ │ Register()│ │ Register() │ │ Register()   │
│            │ │           │ │            │ │              │
│BeginPlay:  │ │Received   │ │BeginPlay:  │ │BeginPlay:    │
│ SendReady()│ │ Player(): │ │ SendReady()│ │ SendReady()  │
│            │ │ SendReady │ │            │ │              │
│EndPlay:    │ │ + tick    │ │HandleMatch │ │EndPlay:      │
│ Unregister │ │ modular   │ │ HasStarted │ │ Unregister   │
│            │ │ components│ │ → fwd to   │ │              │
│            │ │           │ │ components │ │              │
└────────────┘ └───────────┘ └────────────┘ └──────────────┘
       │              │              │              │
       └──────────────┴──────────────┴──────────────┘
                          │
                    它们全是 "Receiver"
                    可以被 GameFeature Action 扩展

┌──────────┐  ┌───────────┐
│ GameMode │  │PlayerState│
├──────────┤  ├───────────┤
│ 不注册自己│  │PreInit:   │
│ 只设置默认│  │ Register()│
│ 类指向   │  │BeginPlay: │
│ Modular  │  │ SendReady │
│ 变体     │  │Reset:     │
│          │  │ → fwd to  │
│          │  │ components│
│          │  │CopyProps: │
│          │  │ → fwd to  │
│          │  │ components│
└──────────┘  └───────────┘
```

**继承层级（以 TPGame 项目为例）：**

```
ACharacter                       APawn
    │                               │
    ├─ AModularCharacter            ├─ AModularPawn
    │      │                        │
    │      └─ ATPGameCharacter      │
    │             │                 │
    │             ├─ (抽象基类)       │
    │             │                 │
    └─ ACombatCharacter             │
    └─ APlatformingCharacter        │
    └─ ASideScrollingCharacter      │
    └─ ACombatEnemy
    └─ ASideScrollingNPC
```

注意：只有 `ATPGameCharacter` 继承了 `AModularCharacter`，三个 Variant 的具体角色直接继承 `ACharacter`。这是因为变体角色需要更灵活的基类选择（有些还需要实现接口如 `ICombatAttacker`、`ICombatDamageable`）。

---

## 3. 逐步解析代码

### 第一层：核心三步协议

每个 Modular Actor 都遵循完全相同的三步协议。以 `AModularCharacter` 为例（`ModularCharacter.cpp:6-25`）：

**Step 1 — PreInitializeComponents：注册为接收器**
```cpp
void AModularCharacter::PreInitializeComponents()
{
    Super::PreInitializeComponents();
    // "我身上有插座，谁需要往我身上装东西都可以"
    UGameFrameworkComponentManager::AddGameFrameworkComponentReceiver(this);
}
```
为什么是 `PreInitializeComponents` 而不是构造函数？因为在这个时间点，Actor 的组件已经被创建，但还没有被初始化。如果有 GameFeature 已经激活，它需要在这个时机把额外组件注入进来，这样这些组件就能和原生组件一起初始化。

**Step 2 — BeginPlay：广播"我已就绪"**
```cpp
void AModularCharacter::BeginPlay()
{
    // "我的所有组件都初始化完了，谁还在等我？现在可以动手了"
    UGameFrameworkComponentManager::SendGameFrameworkComponentExtensionEvent(
        this, UGameFrameworkComponentManager::NAME_GameActorReady);
    Super::BeginPlay();
}
```
这个事件会触发之前通过 `AddExtensionHandler` 注册的所有回调。例如 `GameFeatureAction_AddAbilities` 就是在收到 `NAME_GameActorReady` 时才开始授予技能——因为此时 ASC 组件一定已经存在且初始化完毕。

**Step 3 — EndPlay：注销**
```cpp
void AModularCharacter::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
    // "我要被销毁了，把我身上装的扩展都摘下来"
    UGameFrameworkComponentManager::RemoveGameFrameworkComponentReceiver(this);
    Super::EndPlay(EndPlayReason);
}
```
这会触发 `NAME_ExtensionRemoved` / `NAME_ReceiverRemoved` 事件，让 GameFeature Actions 清理它们添加的扩展。

### 第二层：各变体的额外职责

**`AModularPlayerController`** — 最复杂的 Modular 类（`ModularPlayerController.cpp:11-50`）

除了基础三步协议外，它把 `ReceivedPlayer` 作为"就绪"信号（而不是 `BeginPlay`），因为 PlayerController 在获得 Player 之前没什么可做的：

```cpp
void AModularPlayerController::ReceivedPlayer()
{
    // 此时才真正"就绪"，因为有 Player 才能添加上下文相关的扩展
    UGameFrameworkComponentManager::SendGameFrameworkComponentExtensionEvent(
        this, UGameFrameworkComponentManager::NAME_GameActorReady);
    Super::ReceivedPlayer();

    // 把生命周期事件转发给所有 ControllerComponent
    TArray<UControllerComponent*> ModularComponents;
    GetComponents(ModularComponents);
    for (UControllerComponent* Component : ModularComponents)
    {
        Component->ReceivedPlayer();
    }
}
```

同样，`PlayerTick` 也会转发给所有 `ControllerComponent`：
```cpp
void AModularPlayerController::PlayerTick(float DeltaTime)
{
    Super::PlayerTick(DeltaTime);
    for (UControllerComponent* Component : ModularComponents)
    {
        Component->PlayerTick(DeltaTime);
    }
}
```

**`AModularGameState`** — 转发比赛事件（`ModularGameState.cpp:54-64`）

```cpp
void AModularGameState::HandleMatchHasStarted()
{
    Super::HandleMatchHasStarted();
    // 把所有 GameStateComponent 的对应方法都调一遍
    for (UGameStateComponent* Component : ModularComponents)
    {
        Component->HandleMatchHasStarted();
    }
}
```

**`AModularPlayerState`** — 转发 Reset 和 CopyProperties（`ModularPlayerState.cpp:36-61`）

```cpp
void AModularPlayerState::Reset()
{
    Super::Reset();
    for (UPlayerStateComponent* Component : ModularComponents)
    {
        Component->Reset();
    }
}
```

`CopyProperties` 更精巧 —— 它通过 `FindObjectWithOuter` 查找目标 PlayerState 上同名的组件，然后逐一复制属性。这样在无缝旅行（Seamless Travel）时，模块化组件的状态也能被正确传递。

### 第三层：GameMode 的自动化配置

`AModularGameMode` 和 `AModularGameModeBase` 不注册为 Receiver（GameMode 自己不需要被扩展），它们做的是**自动配置默认类**（`ModularGameMode.cpp:11-27`）：

```cpp
AModularGameModeBase::AModularGameModeBase(const FObjectInitializer& ObjectInitializer)
    : Super(ObjectInitializer)
{
    GameStateClass = AModularGameStateBase::StaticClass();  // → 自动使用 Modular 版
    PlayerControllerClass = AModularPlayerController::StaticClass();
    PlayerStateClass = AModularPlayerState::StaticClass();
    DefaultPawnClass = AModularPawn::StaticClass();
}
```

这意味着任何继承 `AModularGameMode` 的 GameMode，不需要手动指定这四个类，就能自动获得全套 Modular 生态——玩家控制器、玩家状态、游戏状态、默认 Pawn 全部支持 GameFeature 扩展。

### 第四层：与 GameFeature 的联动

回顾之前的 GameFeature 分析，现在可以看清楚完整的链条：

```
GameFeature 激活
    │
    ▼
GameFeatureAction_AddAbilities::AddToWorld()
    │
    ├─ ComponentMan->AddExtensionHandler(ACombatCharacter, Callback)
    │      "当任何 ACombatCharacter 注册为 Receiver 时，通知我"
    │
    └─ 等待...
           │
           ▼
    ACombatCharacter 生成 (继承自 ACharacter，实现了 Combat 接口)
           │
    PreInitializeComponents() → AddGameFrameworkComponentReceiver(this)
           │                    ↑ 此时 GameFeature 被通知，可以注入 ASC 组件
           │
    BeginPlay() → SendGameFrameworkComponentExtensionEvent(NAME_GameActorReady)
           │        ↑ 此时 GameFeature 收到"已就绪"，开始授予技能
           │
           ▼
    HandleActorExtension(Actor, NAME_GameActorReady, EntryIndex)
        └─ AddActorAbilities()
            ├─ FindOrAddComponentForActor<UAbilitySystemComponent>()
            ├─ GiveAbility(Fireball)
            └─ InputComponent->SetInputBinding(...)
```

---

## 4. 容易踩的坑

**坑：不是所有 Variant 角色都用 Modular 基类**

看前面的继承图，`ATPGameCharacter` 继承 `AModularCharacter`，但三个 Variant 的具体角色（`ACombatCharacter`、`APlatformingCharacter`、`ASideScrollingCharacter`）都直接继承 `ACharacter`。

这意味着：
- 如果你的 GameFeature 要往角色上添加组件或技能，目标 `ActorClass` 必须是实现了三步协议的类（或其子类）。
- 直接继承 `ACharacter` 的 Variant 角色**不会**自动注册为 Receiver，GameFeature 的 `AddExtensionHandler` 无法感知到它们的创建。
- 如果需要让 Variant 角色也支持模块化扩展，需要把它们的基类改为 `AModularCharacter` 或手动实现三步协议。

**为什么项目这么做？** 因为 Variant 角色还实现了接口（`ICombatAttacker`、`ICombatDamageable`、`ISideScrollingInteractable`），C++ 多重继承的限制使得直接从 `AModularCharacter` 继承会更复杂。这是一种务实的选择——不是所有角色都需要可扩展性。
