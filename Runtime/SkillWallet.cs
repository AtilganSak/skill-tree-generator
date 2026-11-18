using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HeatInteractive.SkillTreeGenerator
{
    /// One part of a price: an amount of some currency. The currency is any asset the game uses for money (a coin
    /// asset, a loot type...); the tree never looks inside it, it only hands it to the game's ISkillWallet.
    [Serializable]
    public struct SkillCost
    {
        public Object Currency;
        [Min(0)] public int Amount;
    }

    /// Optional: a currency asset that tells the skill UI how to show itself (else its asset name, no icon).
    public interface ISkillCurrency
    {
        string CurrencyName { get; }
        Sprite CurrencyIcon { get; }
    }

    public static class SkillCurrencies
    {
        public static string NameOf(Object currency) =>
            currency == null ? "?" : currency is ISkillCurrency c && !string.IsNullOrEmpty(c.CurrencyName) ? c.CurrencyName : currency.name;

        public static Sprite IconOf(Object currency) => currency is ISkillCurrency c ? c.CurrencyIcon : null;
    }

    /// Where the player's money is: the one thing the game writes to sell skills (SkillBook does the rest).
    public interface ISkillWallet
    {
        int Balance(Object currency);

        /// Money coming back (a respec).
        void Add(Object currency, int amount);

        /// Money paid for a rank. Only called after Balance said it is there.
        void Remove(Object currency, int amount);

        /// The game's own veto, asked after the tree's rules and before the price: false = can't be bought right now
        /// (mid-fight, a tutorial, a node not finished yet...).
        bool CanBuy(SkillNode node);
    }
}
