/**
 * 仓库备份：技能系统架构 Canvas（与 Cursor 工作区画布同源）。
 * 对话旁实时预览仍打开 Cursor 的 canvases/skill-system-architecture.canvas.tsx。
 * 对应规格：docs/superpowers/specs/2026-08-31-skill-definition-design.md
 */
import {
  Button,
  Callout,
  Card,
  CardBody,
  CardHeader,
  Code,
  Divider,
  Grid,
  H1,
  H2,
  Pill,
  Row,
  Stack,
  Stat,
  Table,
  Text,
  computeDAGLayout,
  useCanvasState,
  useHostTheme,
  useMemo,
} from "cursor/canvas";

type ViewId = "blueprint" | "activate" | "events";

const NODE_LABELS: Record<string, { title: string; sub: string }> = {
  actor: { title: "ActorDefinition", sub: "角色唯一模板" },
  job: { title: "JobDefinition", sub: "职业槽" },
  attack: { title: "Attack", sub: "Kind = Basic" },
  skill: { title: "Skill", sub: "Kind = Skill" },
  ult: { title: "Ultimate", sub: "Kind = Skill" },
  def: { title: "SkillDefinition", sub: "ConfigId + Modules" },
  play: { title: "PlayAttackModule", sub: "当帧开招" },
  apply: { title: "ApplyEffectModule", sub: "按 Targeting 施加" },
  grant: { title: "GrantListenerModule", sub: "只挂施放者" },
  spec: { title: "AttackSpec", sub: "Startup / Active / Recovery" },
  hit: { title: "HitboxEntry", sub: "Start–End + Offset/Size" },
  ge: { title: "GameplayEffect", sub: "Duration / Period / Charge" },

  input: { title: "PlayerInput", sub: "X 普攻 · Z 战技 · V 大招" },
  try: { title: "TryActivate", sub: "门禁 / CD / Replace" },
  inst: { title: "SkillInstance", sub: "ConfigId + RuntimeId" },
  begin: { title: "BeginPlayAttack", sub: "同一播放器" },
  applyRt: { title: "ApplyModuleEffect", sub: "当帧挂效果" },
  box: { title: "Hitbox.Activate", sub: "新 AttackId" },
  holder: { title: "EffectHolder", sub: "挂在目标 Actor 上" },
  query: { title: "AABB 查询", sub: "Hitbox → Hurtbox" },
  dmg: { title: "TakeDamage", sub: "GetAttackPower()" },

  started: { title: "*AttackStarted", sub: "由 Kind 分通道" },
  hitSig: { title: "*AttackHit", sub: "FromListener 不发" },
  fan: { title: "扇出监听", sub: "ApplyOrder → RuntimeId" },
  extra: { title: "附加盒", sub: "独立 AttackId" },
  charge: { title: "Charge += 1", sub: "仅 Basic* 默认" },
  burst: { title: "Burst", sub: "满能或 OnExpire" },
  replace: { title: "Replace", sub: "卸旧不走 Expire" },
};

const BLUEPRINT_GRAPH = {
  nodes: [
    { id: "actor" },
    { id: "job" },
    { id: "attack" },
    { id: "skill" },
    { id: "ult" },
    { id: "def" },
    { id: "play" },
    { id: "apply" },
    { id: "grant" },
    { id: "spec" },
    { id: "ge" },
    { id: "hit" },
  ],
  edges: [
    { from: "actor", to: "job" },
    { from: "job", to: "attack" },
    { from: "job", to: "skill" },
    { from: "job", to: "ult" },
    { from: "attack", to: "def" },
    { from: "skill", to: "def" },
    { from: "ult", to: "def" },
    { from: "def", to: "play" },
    { from: "def", to: "apply" },
    { from: "def", to: "grant" },
    { from: "play", to: "spec" },
    { from: "spec", to: "hit" },
    { from: "apply", to: "ge" },
    { from: "grant", to: "ge" },
    { from: "ge", to: "hit" },
  ],
};

const ACTIVATE_GRAPH = {
  nodes: [
    { id: "input" },
    { id: "try" },
    { id: "inst" },
    { id: "begin" },
    { id: "applyRt" },
    { id: "box" },
    { id: "holder" },
    { id: "query" },
    { id: "dmg" },
  ],
  edges: [
    { from: "input", to: "try" },
    { from: "try", to: "inst" },
    { from: "inst", to: "begin" },
    { from: "inst", to: "applyRt" },
    { from: "begin", to: "box" },
    { from: "applyRt", to: "holder" },
    { from: "box", to: "query" },
    { from: "query", to: "dmg" },
  ],
};

const EVENT_GRAPH = {
  nodes: [
    { id: "box" },
    { id: "started" },
    { id: "query" },
    { id: "hitSig" },
    { id: "fan" },
    { id: "extra" },
    { id: "charge" },
    { id: "burst" },
    { id: "replace" },
    { id: "holder" },
  ],
  edges: [
    { from: "box", to: "started" },
    { from: "started", to: "fan" },
    { from: "fan", to: "extra" },
    { from: "box", to: "query" },
    { from: "query", to: "hitSig" },
    { from: "hitSig", to: "charge" },
    { from: "charge", to: "burst" },
    { from: "holder", to: "burst" },
    { from: "replace", to: "holder" },
  ],
};

function FlowDiagram({
  graph,
}: {
  graph: { nodes: { id: string }[]; edges: { from: string; to: string }[] };
}) {
  const theme = useHostTheme();
  const layout = useMemo(
    () =>
      computeDAGLayout({
        ...graph,
        direction: "horizontal",
        nodeWidth: 168,
        nodeHeight: 52,
        rankGap: 52,
        nodeGap: 20,
        padding: 12,
      }),
    [graph],
  );

  return (
    <svg
      width="100%"
      viewBox={`0 0 ${layout.width} ${layout.height}`}
      style={{ display: "block", maxWidth: layout.width }}
    >
      {layout.edges.map((edge) => (
        <line
          key={`${edge.from}-${edge.to}`}
          x1={edge.sourceX}
          y1={edge.sourceY}
          x2={edge.targetX}
          y2={edge.targetY}
          stroke={edge.isBackEdge ? theme.stroke.tertiary : theme.stroke.secondary}
          strokeWidth={1.25}
          strokeDasharray={edge.isBackEdge ? "4 3" : undefined}
        />
      ))}
      {layout.nodes.map((node) => {
        const label = NODE_LABELS[node.id] ?? { title: node.id, sub: "" };
        return (
          <g key={node.id} transform={`translate(${node.x}, ${node.y})`}>
            <rect
              width={168}
              height={52}
              rx={4}
              fill={theme.fill.secondary}
              stroke={theme.stroke.tertiary}
            />
            <text
              x={10}
              y={21}
              fill={theme.text.primary}
              fontSize={12}
              fontFamily="inherit"
            >
              {label.title}
            </text>
            <text
              x={10}
              y={38}
              fill={theme.text.tertiary}
              fontSize={10}
              fontFamily="inherit"
            >
              {label.sub}
            </text>
          </g>
        );
      })}
    </svg>
  );
}

export default function SkillSystemArchitecture() {
  const [view, setView] = useCanvasState<ViewId>("view", "blueprint");

  return (
    <Stack gap={20}>
      <Stack gap={6}>
        <H1>技能系统：数据结构与流向</H1>
        <Text tone="secondary">
          ProjectC · Godot 4.6 / C# · 规格 2026-08-31。普攻、战技、大招共用
          SkillDefinition；运行时用 AttackKind 分事件通道，而不是用 C# 类型分支。
        </Text>
      </Stack>

      <Grid columns={3} gap={12}>
        <Stat value="蓝图 Resource" label="只读 .tres，共享不改" />
        <Stat value="SkillInstance" label="ConfigId + RuntimeId" />
        <Stat value="效果挂目标" label="SourceRuntimeId 追溯来源" />
      </Grid>

      <Row gap={8} wrap>
        <Button
          variant={view === "blueprint" ? "primary" : "secondary"}
          onClick={() => setView("blueprint")}
        >
          蓝图结构
        </Button>
        <Button
          variant={view === "activate" ? "primary" : "secondary"}
          onClick={() => setView("activate")}
        >
          激活与播放
        </Button>
        <Button
          variant={view === "events" ? "primary" : "secondary"}
          onClick={() => setView("events")}
        >
          事件与叠加
        </Button>
      </Row>

      {view === "blueprint" ? (
        <Stack gap={12}>
          <H2>蓝图组合</H2>
          <Text tone="secondary">
            Actor 只 Export Definition。职业槽 Attack / Skill / Ultimate 都是
            SkillDefinition；Dodge 仍是 PackedScene，不进这套。模块数组顺序只决定同一帧稳定次序，默认互不等待。
          </Text>
          <Card>
            <CardHeader>Job → SkillDefinition → 模块 → 载荷</CardHeader>
            <CardBody>
              <FlowDiagram graph={BLUEPRINT_GRAPH} />
            </CardBody>
          </Card>
          <Grid columns={3} gap={12}>
            <Card>
              <CardHeader trailing={<Pill tone="info">Basic 禁止</Pill>}>
                PlayAttack
              </CardHeader>
              <CardBody>
                <Text size="small">
                  引用 AttackSpec。忽略 Targeting。多只 Hitbox 数据合法，当前实现只播第一只。
                </Text>
              </CardBody>
            </Card>
            <Card>
              <CardHeader>ApplyEffect</CardHeader>
              <CardBody>
                <Text size="small">
                  按定义上的 Targeting（Self / 半径敌友全）把 GameplayEffect 挂到目标。
                </Text>
              </CardBody>
            </Card>
            <Card>
              <CardHeader>GrantListener</CardHeader>
              <CardBody>
                <Text size="small">
                  强制挂施放者。默认 SubscribeBasic，技能伤害默认不订阅。
                </Text>
              </CardBody>
            </Card>
          </Grid>
        </Stack>
      ) : null}

      {view === "activate" ? (
        <Stack gap={12}>
          <H2>激活当帧</H2>
          <Text tone="secondary">
            成功过门禁后 new SkillInstance，然后按模块数组跑：授予立刻 Apply，PlayAttack 同一播放器开招。前摇被打断只关盒，已挂上的效果不自动卸。
          </Text>
          <Card>
            <CardHeader>输入 → 实例 → 招式 / 效果并行</CardHeader>
            <CardBody>
              <FlowDiagram graph={ACTIVATE_GRAPH} />
            </CardBody>
          </Card>
          <Callout tone="info" title="物理帧顺序（不可调换）">
            Player 输入 → 位移 → Hitbox 查询命中 → Combat 扣招式时间、附加盒寿命、效果 Tick。先查询再关盒，避免短于一帧的挥击零命中。
          </Callout>
          <Table
            headers={["Kind", "实例寿命", "招式锁", "Replace"]}
            rows={[
              [
                <Code>Basic</Code>,
                "等于本次 PlayAttack 总长，打完丢弃",
                "进行中则拒绝再挥",
                "不按 ConfigId 卸上一刀",
              ],
              [
                <Code>Skill</Code>,
                "max(PlayAttack, 孩子效果剩余)",
                "不挡普攻锁",
                "同 ConfigId 卸旧上新",
              ],
            ]}
          />
        </Stack>
      ) : null}

      {view === "events" ? (
        <Stack gap={12}>
          <H2>事件通道与叠加</H2>
          <Text tone="secondary">
            逻辑真相是 AttackKind，不是类名。技能自己的 PlayAttack 发 Skill*，默认监听不接，避免开头小伤害给自己叠额外弹或充能。
          </Text>
          <Card>
            <CardHeader>Started 扇出 · Hit 充能 · Expire 爆发</CardHeader>
            <CardBody>
              <FlowDiagram graph={EVENT_GRAPH} />
            </CardBody>
          </Card>
          <Grid columns={2} gap={12}>
            <Card>
              <CardHeader>跨 ConfigId</CardHeader>
              <CardBody>
                <Text size="small">
                  extra_blow 与 charge_burst 可同时挂着。一次普攻既开附加盒又 Charge+1。附加盒必须新 AttackId，否则会被「同一刀同一 Hurtbox 只中一次」吃掉。
                </Text>
              </CardBody>
            </Card>
            <Card>
              <CardHeader>同 ConfigId = Replace</CardHeader>
              <CardBody>
                <Text size="small">
                  卸旧实例的 PlayAttack，按 SourceRuntimeId 清场景内效果（含打到别人身上的）。走 OnRemove，不走 OnExpire，重放不会当爆发。
                </Text>
              </CardBody>
            </Card>
          </Grid>
        </Stack>
      ) : null}

      <Divider />

      <H2>三套 Id</H2>
      <Table
        headers={["Id", "谁分配", "认什么", "禁止拿来"]}
        rows={[
          [
            "ConfigId",
            "SkillDefinition 写死",
            "是不是同一份蓝图（Replace 键）",
            "区分「这一次挂上的那份」",
          ],
          [
            "RuntimeId",
            "Combat 递增 uint",
            "这一份 SkillInstance / 效果来源",
            "当叠加入口",
          ],
          [
            "AttackId",
            "每次开盒递增",
            "这一只判定盒的去重集合",
            "当技能身份",
          ],
        ]}
      />

      <H2>当前默认职业（player_default_job）</H2>
      <Table
        headers={["槽", "ConfigId", "模块", "手测键"]}
        rows={[
          [
            "Attack",
            "skill.player_default.attack",
            "PlayAttack 0.2s 单盒",
            "X",
          ],
          [
            "Skill",
            "skill.player_default.extra_blow",
            "技能小伤害 + 6s 普攻附加盒",
            "Z",
          ],
          [
            "Ultimate",
            "skill.player_default.charge_burst",
            "8s 监听，命中 3 次或到期爆发",
            "V",
          ],
        ]}
        rowTone={["info", "success", "warning"]}
      />

      <Text size="small" tone="tertiary">
        附加盒命中只扣血，不发 *Hit、不加充能。Burst 对 EnemiesInRadius 结算 BurstDamage。
      </Text>
    </Stack>
  );
}
