using System;
using System.Collections.Generic;
using UnityEngine;

namespace AtilganSak.Skills
{
    /// Whatever holds the player's ranks (a save, a test book). The tree asks it, never stores ranks itself.
    public interface ISkillRanks
    {
        int GetRank(SkillNode node);
    }

    public static class SkillRanksExtensions
    {
        public static bool IsOwned(this ISkillRanks ranks, SkillNode node) => node != null && ranks.GetRank(node) > 0;
    }

    /// The player's ranks: node Id -> rank. Serializable, so it can sit in any save model as a field (JsonUtility,
    /// Newtonsoft and Unity serialization all read it), or go through ToJson / FromJson as a string. Where it is written
    /// is up to the game; Changed says when.
    /// Ranks of nodes that were deleted from the tree are kept: Ids are never reused, so they can't land on another node.
    [Serializable]
    public class SkillRanks : ISkillRanks
    {
        [Serializable]
        public struct Entry
        {
            public int Id;
            public int Rank;
        }

        /// Owned nodes only (rank > 0). Editing it directly is fine (a save editor, a debug tool); SetRank also fires Changed.
        public List<Entry> Entries = new();

        /// A rank was set or the book cleared: the moment to save.
        public event Action Changed;

        public int GetRank(SkillNode node) => node != null ? GetRank(node.Id) : 0;

        // ponytail: linear scan, no lookup cache, so outside edits to Entries are always seen. A tree has tens of
        // owned nodes; add a dictionary if a profiler ever points here.
        public int GetRank(int id)
        {
            foreach (var e in Entries)
                if (e.Id == id) return e.Rank;
            return 0;
        }

        public void SetRank(SkillNode node, int rank)
        {
            if (node != null) SetRank(node.Id, rank);
        }

        public void SetRank(int id, int rank)
        {
            int i = -1;
            for (int n = 0; n < Entries.Count; n++) // not FindIndex: its lambda allocates per call
                if (Entries[n].Id == id) { i = n; break; }
            if (rank <= 0)
            {
                if (i < 0) return;
                Entries.RemoveAt(i);
            }
            else
            {
                if (i >= 0 && Entries[i].Rank == rank) return;
                var entry = new Entry { Id = id, Rank = rank };
                if (i < 0) Entries.Add(entry);
                else Entries[i] = entry;
            }
            Changed?.Invoke();
        }

        public void Clear()
        {
            Entries.Clear();
            Changed?.Invoke();
        }

        public string ToJson(bool pretty = false) => JsonUtility.ToJson(this, pretty);

        /// Null or empty text gives an empty book.
        public static SkillRanks FromJson(string json) =>
            string.IsNullOrEmpty(json) ? new SkillRanks() : JsonUtility.FromJson<SkillRanks>(json) ?? new SkillRanks();
    }
}
