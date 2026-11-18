using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HeatInteractive.SkillTreeGenerator
{
    /// A player in a tree: their ranks, stat reads, unlocks, and buying with the game's ISkillWallet. The game only
    /// says where the money is, saves on Changed, and sets Current so gameplay can ask from anywhere.
    /// Reading a stat: Get("Key", baseValue, scope). A percent stat multiplies the base, a flat one adds to it: pass 1
    /// for a multiplier, 0 for an amount. A Key the tree doesn't have leaves the base as it is (warned once: a typo).
    /// A null tree (not loaded, missing) is a progress with nothing in it: no stat changes anything, nothing is sold.
    public class SkillBook : ISkillRanks
    {
        public readonly SkillTree Tree;
        public readonly SkillRanks Ranks;
        public readonly ISkillWallet Wallet;

        /// A rank was bought, set or everything was refunded: the moment to save and redraw.
        public event Action Changed;

        private static readonly SkillBook Empty = new(null, null, null);
        private static SkillBook _current;
        private readonly HashSet<string> _warned = new();

        /// The player's progress, set by the game once it is loaded. Until then (or in a scene played on its own) an
        /// empty one, so gameplay can always ask.
        public static SkillBook Current
        {
            get => _current ?? Empty;
            set => _current = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCurrent() => _current = null; // play mode without a domain reload

        /// A null tree or wallet is a progress with nothing for sale.
        public SkillBook(SkillTree tree, SkillRanks ranks, ISkillWallet wallet)
        {
            Tree = tree;
            Ranks = ranks ?? new SkillRanks();
            Wallet = wallet;
        }

        public int GetRank(SkillNode node) => Ranks.GetRank(node);

        public bool IsOwned(SkillNode node) => GetRank(node) > 0;

        /// A rank for free, clamped to 0..MaxRank: dev tools, cheats, rewards. notify = false while setting many in a
        /// row (raise Changed yourself once with NotifyChanged).
        public void SetRank(SkillNode node, int rank, bool notify = true)
        {
            if (node == null) return;
            Ranks.SetRank(node, Mathf.Clamp(rank, 0, node.MaxRank));
            if (notify) Changed?.Invoke();
        }

        public void NotifyChanged() => Changed?.Invoke();

        // ---------------------------------------------------------------- stats and unlocks

        private SkillStat StatOf(string key)
        {
            if (Tree == null) return null;
            var stat = Tree.Stat(key);
            if (stat == null && key != null && _warned.Add(key))
                Debug.LogWarning($"Skills: the tree has no stat \"{key}\", so it reads as no skill. Add it in the Skill Tree Editor (Stats) or fix the Key.");
            return stat;
        }

        /// The base value with the stat applied: percent multiplies, flat adds. scope = the asset asking (effects with
        /// targets only count for those). No tree, or no such stat: the base value.
        public float Get(string stat, float baseValue, Object scope = null)
        {
            var s = StatOf(stat);
            return s == null ? baseValue : Tree.Get(s, baseValue, Ranks, scope);
        }

        /// The asset an owned effect of this stat carries (SkillEffect.Payload), or null.
        public Object PayloadOf(string stat) => Tree != null ? Tree.PayloadOf(StatOf(stat), Ranks) : null;

        /// Some node unlocks this asset, so it stays locked until one is owned.
        public bool IsGated(Object target) => Tree != null && Tree.IsGated(target);

        /// Gated assets need an owned unlocking node; the rest use openByDefault.
        public bool IsUnlocked(Object target, bool openByDefault) =>
            Tree != null ? Tree.IsUnlocked(target, Ranks, openByDefault) : openByDefault;

        // ---------------------------------------------------------------- rules

        /// The root, or a node with an owned neighbour.
        public bool IsReachable(SkillNode node) => Tree != null && Tree.IsReachable(node, Ranks);

        /// Why a reachable node is still held back (a Requires, locked targets, the node's own rule), null = nothing.
        public string LockReason(SkillNode node) => Tree != null ? Tree.LockReason(node, Ranks) : null;

        /// Reachable and nothing holds it back; says nothing about max rank, the wallet or the price (see WhyNot).
        public bool IsBuyable(SkillNode node) => IsReachable(node) && LockReason(node) == null;

        // ---------------------------------------------------------------- buying

        /// The next rank's price: the node's Cost, times CostGrowth per rank owned, through the tree's PriceStat.
        public List<SkillCost> Price(SkillNode node) => PriceAt(node, GetRank(node));

        /// What going from `rank` to `rank + 1` costs today.
        public List<SkillCost> PriceAt(SkillNode node, int rank)
        {
            var price = new List<SkillCost>();
            if (node == null || node.Cost == null) return price;
            float scale = Mathf.Pow(Mathf.Max(1f, node.CostGrowth), rank);
            if (Tree != null) scale = Tree.Get(Tree.Stat(Tree.PriceStat), scale, Ranks);
            foreach (var cost in node.Cost)
                if (cost.Currency != null && cost.Amount > 0)
                    price.Add(new SkillCost { Currency = cost.Currency, Amount = Mathf.Max(1, Mathf.CeilToInt(cost.Amount * scale)) });
            return price;
        }

        /// The wallet holds every part of the price (parts in the same currency are added up).
        public bool CanAfford(IReadOnlyList<SkillCost> price)
        {
            if (Wallet == null) return false;
            foreach (var cost in price)
            {
                int total = 0;
                foreach (var other in price)
                    if (other.Currency == cost.Currency) total += other.Amount;
                if (Wallet.Balance(cost.Currency) < total) return false;
            }
            return true;
        }

        /// Why the next rank can't be bought, null = it can: max rank, the tree's rules (reachable, Requires, locked
        /// targets), the wallet's CanBuy, then the price.
        public string WhyNot(SkillNode node)
        {
            if (node == null || Tree == null || Wallet == null) return "Skills are off.";
            if (GetRank(node) >= node.MaxRank) return Tree.MaxRankText(node);
            string locked = Tree.WhyLocked(node, Ranks);
            if (locked != null) return locked;
            if (!Wallet.CanBuy(node)) return Tree.BlockedText(node);
            return CanAfford(Price(node)) ? null : Tree.CantAffordText(node);
        }

        public bool CanBuy(SkillNode node) => WhyNot(node) == null;

        /// Pays the price and adds a rank, or does nothing (false) if WhyNot has a reason.
        public bool TryBuy(SkillNode node)
        {
            if (!CanBuy(node)) return false;
            foreach (var cost in Price(node)) Wallet.Remove(cost.Currency, cost.Amount);
            Ranks.SetRank(node, GetRank(node) + 1);
            Changed?.Invoke();
            return true;
        }

        /// Takes every rank back and pays each one back at today's price.
        // ponytail: no ledger of what was paid, so a price stat owned now counts for every rank (it can only refund
        // less than was paid, never more). Add a per-node paid list to SkillRanks if exact refunds matter.
        public void Respec()
        {
            var refund = new List<SkillCost>();
            if (Tree != null)
                foreach (var node in Tree.Nodes)
                    for (int rank = GetRank(node) - 1; rank >= 0; rank--)
                        refund.AddRange(PriceAt(node, rank));
            Ranks.Clear();
            if (Wallet != null)
                foreach (var cost in refund) Wallet.Add(cost.Currency, cost.Amount);
            Changed?.Invoke();
        }
    }
}
