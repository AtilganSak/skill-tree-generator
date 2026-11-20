# Skill Tree Generator

A data-driven skill tree for Unity: nodes, links, requirements, ranks and prices, with a visual editor and a ready-made UI panel. No code is needed to build a tree; your game only says where the money is.

Unity 6000.0 or newer. Needs `com.unity.ugui`.

## Install

Package Manager > **+** > **Add package from git URL**:

```
https://github.com/AtilganSak/skill-tree-generator.git
```

## Build a tree

1. **Create > Skill Tree Generator > Skill Tree**, then open the Skill Tree Editor.
2. Click empty space (nothing picked) to define:
   - **Stats**: what skills raise. *Percent* (+10%), *Flat* (+1) or *Flag* (on/off, e.g. "can build turret 2").
   - **Currencies**: what skills cost, e.g. `gold`, `gem`.
3. Double-click the canvas to add skills. Drag a skill's **+** handle to connect it to another one (hold Ctrl for a requirement). Set each skill's effects and cost in the Inspector.
4. Drag `UI/Prefabs/SkillTreePanel.prefab` into your Canvas.

A skill opens once a connected skill is owned (the root is always open) and all of its requirements are owned.

## Connect it to your game

Tell the tree where your money is. Map each currency to your own data by its `Key`:

```csharp
public class GameWallet : ISkillWallet
{
    public int Balance(SkillCurrency c) => c.Key == "gold" ? save.Gold : save.Gems;
    public void Add(SkillCurrency c, int amount) { /* give money back */ }
    public void Remove(SkillCurrency c, int amount) { /* take money */ }
    public bool CanBuy(SkillNode node) => !inCombat;   // your own veto
}
```

Create the player's book once (the panel and your gameplay read it from `SkillBook.Current`):

```csharp
SkillBook.Current = new SkillBook(tree, new SkillRanks(), new GameWallet());
```

## Save and load

The player's ranks live in a `SkillRanks`. It is serializable, so it can sit in your save data as a field, or go through JSON as a string:

```csharp
// save
string json = book.Ranks.ToJson();

// load (empty or null text gives an empty book)
var ranks = SkillRanks.FromJson(json);
SkillBook.Current = new SkillBook(tree, ranks, new GameWallet());
```

`SkillRanks.Changed` fires whenever a rank is set or cleared (a buy, `SetRank`, a respec): a good moment to save.

```csharp
var book = SkillBook.Current;
book.Ranks.Changed += () => SaveToDisk(book.Ranks.ToJson());   // your own save code
```

## Use the skills

```csharp
var book = SkillBook.Current;

float damage = book.GetStat<float>("Damage", 10f);    // percent multiplies the base, flat adds to it
int pierce   = book.GetStat<int>("Pierce", 1);        // flat, rounded to an int
bool turret2 = book.GetStat<bool>("Turret2", false);  // flag: on once a skill of it is active
```

The type can be left out, it comes from the base value: `book.GetStat("Damage", 10f)`. Reading a stat as the wrong type (a flag as `float`) warns and gives the base value.

```csharp
book.RankChanged += (node, from, to) =>
{
    if (from == 0 && to > 0) Debug.Log($"{node.DisplayName} unlocked");
};
```

`TryBuy(node)`, `Respec()` and `SetRank(node, rank)` (free, for cheats and rewards) are on the book too.

## Customize

- **Your own node types:** derive from `SkillNode` (or `EffectSkill`). Every serialized field shows up in the editor's Inspector.
- **Look of a node:** assign a prefab with a `SkillNodeView` to a skill's *View Prefab*. Empty uses the panel's default.
- **Look of the lines:** pick a connection, then its *Look* section: width and colors as a shared preset, with *Apply to all links*.
- **Look of everything else:** the panel's node, link, detail card and currency row are prefabs. Duplicate one and assign your copy on the panel.

## License

See [LICENSE](LICENSE).
