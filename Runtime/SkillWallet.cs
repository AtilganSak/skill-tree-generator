using System;
using UnityEngine;

namespace HeatInteractive.SkillTreeGenerator
{
    /// One part of a price: an amount of one of the tree's currencies.
    [Serializable]
    public struct SkillCost
    {
        public SkillCurrency Currency;
        [Min(0)] public int Amount;
    }

    /// Where the player's money is: the one thing the game writes to sell skills (SkillBook does the rest).
    /// Map each currency to the game's own money by its Key.
    public interface ISkillWallet
    {
        int Balance(SkillCurrency currency);

        /// Money coming back (a respec).
        void Add(SkillCurrency currency, int amount);

        /// Money paid for a rank. Only called after Balance said it is there.
        void Remove(SkillCurrency currency, int amount);

        /// The game's own veto, asked after the tree's rules and before the price: false = can't be bought right now
        /// (mid-fight, a tutorial, a node not finished yet...).
        bool CanBuy(SkillNode node);
    }
}
